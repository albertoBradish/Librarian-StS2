using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Librarian.Core;

/// <summary>The origin of a recorded, actual increase in the engine's Block value.</summary>
public enum BlockBatchKind
{
    Ordinary,
    Tide
}

/// <summary>Chronological phases within one turn belonging to this ledger's player.</summary>
public enum BlockExpiryPhase
{
    OwnerTurnStart = 0,
    OwnerTurnEnd = 1
}

/// <summary>A deadline in owner-turn time, independent of other players' turns.</summary>
public readonly record struct BlockExpiry(long OwnerTurn, BlockExpiryPhase Phase) : IComparable<BlockExpiry>
{
    public int CompareTo(BlockExpiry other)
    {
        int turnOrder = OwnerTurn.CompareTo(other.OwnerTurn);
        return turnOrder != 0 ? turnOrder : Phase.CompareTo(other.Phase);
    }
}

/// <summary>An immutable view of an outstanding batch. A permanent batch has no expiry.</summary>
public sealed record BlockBatchSnapshot(
    long Id,
    BlockBatchKind Kind,
    long CreatedOwnerTurn,
    BlockExpiry? Expiry,
    int Remaining,
    bool IsPermanent);

/// <summary>One batch affected by a consumption, expiry, or explicit ordinary clear.</summary>
public sealed record BlockBatchRemoval(
    long BatchId,
    BlockBatchKind Kind,
    int Removed,
    int Remaining);

/// <summary>
/// Actual ledger reductions. The adapter decides how to reconcile expiry/clear reductions
/// with engine Block; the ledger never issues a second engine damage operation.
/// </summary>
public sealed record BlockLedgerChange(long TotalRemoved, IReadOnlyList<BlockBatchRemoval> Batches);

/// <summary>
/// Per-player source accounting for Block. Inputs must be actual engine deltas, after
/// modifiers and clamping; requested card amounts must not be recorded here.
/// This class has no engine, save, event, or global state and expects serialized access.
/// </summary>
public sealed class TideBlockLedger
{
    private readonly List<Batch> _batches = new();
    private long _lastBatchId;
    private long _total;

    public long Total => _total;

    public long TideRemaining => _batches
        .Where(batch => batch.Kind == BlockBatchKind.Tide)
        .Sum(batch => (long)batch.Remaining);

    public long OrdinaryRemaining => _batches
        .Where(batch => batch.Kind == BlockBatchKind.Ordinary)
        .Sum(batch => (long)batch.Remaining);

    /// <summary>Once enabled, existing and future Tide batches have no expiry.</summary>
    public bool TideIsPermanent { get; private set; }

    /// <summary>
    /// Records ordinary Block. The default deadline is the next owner-turn start.
    /// Supply the deadline established by the engine when its ordinary lifetime differs.
    /// An ordinary deadline establishes consumption priority and eligibility for an
    /// explicit Expire(includeOrdinary:true); it does not itself trigger native clearing.
    /// Zero gain creates no batch and consumes no batch ID.
    /// </summary>
    public BlockBatchSnapshot? RecordOrdinaryGain(
        int actualAmount,
        long createdOwnerTurn,
        BlockExpiry? expiry = null)
    {
        ValidateAmount(actualAmount);
        ValidateOwnerTurn(createdOwnerTurn, nameof(createdOwnerTurn));
        if (expiry is { } explicitExpiry)
        {
            ValidateExpiry(explicitExpiry, nameof(expiry));
            if (explicitExpiry.OwnerTurn < createdOwnerTurn)
            {
                throw new ArgumentOutOfRangeException(nameof(expiry), "Expiry cannot precede the batch's creation turn.");
            }
        }

        if (actualAmount == 0)
        {
            return null;
        }

        BlockExpiry deadline = expiry ?? new BlockExpiry(
            checked(createdOwnerTurn + 1), BlockExpiryPhase.OwnerTurnStart);
        return AddBatch(actualAmount, createdOwnerTurn, BlockBatchKind.Ordinary, deadline, false);
    }

    /// <summary>
    /// Records Tide Block that expires at the end of the next owner turn, unless made
    /// permanent. Its creation turn is the owner's turn counter even on another side's turn.
    /// </summary>
    public BlockBatchSnapshot? RecordTideGain(int actualAmount, long createdOwnerTurn, bool expireAtNextEnd = true)
    {
        ValidateAmount(actualAmount);
        ValidateOwnerTurn(createdOwnerTurn, nameof(createdOwnerTurn));
        if (actualAmount == 0)
        {
            return null;
        }

        BlockExpiry? deadline = TideIsPermanent || !expireAtNextEnd
            ? null
            : new BlockExpiry(checked(createdOwnerTurn + 1), BlockExpiryPhase.OwnerTurnEnd);
        return AddBatch(actualAmount, createdOwnerTurn, BlockBatchKind.Tide, deadline, TideIsPermanent);
    }

    /// <summary>
    /// Attributes an actual Block loss to the earliest-expiring batches first, with
    /// permanent batches last and creation-order FIFO for matching deadlines.
    /// Removal is capped by the recorded total. Unblockable damage should pass zero.
    /// </summary>
    public BlockLedgerChange Consume(int actualBlockLost)
    {
        ValidateAmount(actualBlockLost, nameof(actualBlockLost));
        int remainingLoss = actualBlockLost;
        var removals = new List<BlockBatchRemoval>();
        foreach (Batch batch in OrderedBatches())
        {
            if (remainingLoss == 0)
            {
                break;
            }

            int removed = Math.Min(batch.Remaining, remainingLoss);
            ApplyRemoval(batch, removed, removals);
            remainingLoss -= removed;
        }

        RemoveEmptyBatches();
        return Change(actualBlockLost - remainingLoss, removals);
    }

    /// <summary>
    /// Removes due batches through the supplied phase (including overdue deadlines).
    /// Set includeOrdinary=false when native ordinary Block clearing is prevented or
    /// managed separately, and use ClearOrdinary only when native clearing actually occurs.
    /// An expiry check never advances any stored turn counter and is safe to repeat.
    /// </summary>
    public BlockLedgerChange Expire(
        long ownerTurn,
        BlockExpiryPhase phase,
        bool includeOrdinary = true)
    {
        ValidateOwnerTurn(ownerTurn, nameof(ownerTurn));
        var now = new BlockExpiry(ownerTurn, phase);
        ValidateExpiry(now, nameof(phase));
        var removals = new List<BlockBatchRemoval>();
        long removedTotal = 0;
        foreach (Batch batch in OrderedBatches())
        {
            if ((!includeOrdinary && batch.Kind == BlockBatchKind.Ordinary)
                || batch.Expiry is not { } deadline
                || deadline.CompareTo(now) > 0)
            {
                continue;
            }

            int removed = batch.Remaining;
            ApplyRemoval(batch, removed, removals);
            removedTotal += removed;
        }

        RemoveEmptyBatches();
        return Change(removedTotal, removals);
    }

    /// <summary>
    /// Drops all outstanding ordinary batches when the adapter has confirmed native
    /// ordinary Block clearing. Tide batches are unaffected, including permanent Tide.
    /// Do not call merely because an engine clear notification fired: it may be prevented.
    /// </summary>
    public BlockLedgerChange ClearOrdinary()
    {
        var removals = new List<BlockBatchRemoval>();
        long removedTotal = 0;
        foreach (Batch batch in OrderedBatches())
        {
            if (batch.Kind != BlockBatchKind.Ordinary)
            {
                continue;
            }

            int removed = batch.Remaining;
            ApplyRemoval(batch, removed, removals);
            removedTotal += removed;
        }

        RemoveEmptyBatches();
        return Change(removedTotal, removals);
    }

    /// <summary>
    /// Converts existing Tide batches and all subsequent Tide gains to permanent Block.
    /// Returns the number of converted existing batches. Repeated calls are idempotent.
    /// Ordinary deadlines and total Block are unchanged.
    /// </summary>
    public int MakeTidePermanent()
    {
        TideIsPermanent = true;
        int converted = 0;
        foreach (Batch batch in _batches)
        {
            if (batch.Kind == BlockBatchKind.Tide && !batch.IsPermanent)
            {
                batch.IsPermanent = true;
                batch.Expiry = null;
                converted++;
            }
        }

        return converted;
    }

    /// <summary>
    /// Returns immutable copies of outstanding batches in creation order. This is a
    /// read-only diagnostic/UI view, not a promise of mid-combat disk-save support.
    /// </summary>
    public IReadOnlyList<BlockBatchSnapshot> Snapshot() =>
        Array.AsReadOnly(_batches.Select(ToSnapshot).ToArray());

    private BlockBatchSnapshot AddBatch(
        int actualAmount,
        long createdOwnerTurn,
        BlockBatchKind kind,
        BlockExpiry? expiry,
        bool permanent)
    {
        // Check arithmetic before mutation so invalid extreme inputs leave state intact.
        long batchId = checked(_lastBatchId + 1);
        long newTotal = checked(_total + actualAmount);
        var batch = new Batch(batchId, kind, createdOwnerTurn, expiry, actualAmount, permanent);
        _batches.Add(batch);
        _lastBatchId = batchId;
        _total = newTotal;
        return ToSnapshot(batch);
    }

    private Batch[] OrderedBatches() => _batches
        .OrderBy(batch => batch.Expiry is null ? 1 : 0)
        .ThenBy(batch => batch.Expiry)
        .ThenBy(batch => batch.Id)
        .ToArray();

    private void ApplyRemoval(Batch batch, int amount, List<BlockBatchRemoval> removals)
    {
        if (amount == 0)
        {
            return;
        }

        batch.Remaining -= amount;
        _total -= amount;
        removals.Add(new BlockBatchRemoval(batch.Id, batch.Kind, amount, batch.Remaining));
    }

    private void RemoveEmptyBatches() => _batches.RemoveAll(batch => batch.Remaining == 0);

    private static BlockLedgerChange Change(long total, List<BlockBatchRemoval> removals) =>
        new(total, new ReadOnlyCollection<BlockBatchRemoval>(removals));

    private static BlockBatchSnapshot ToSnapshot(Batch batch) => new(
        batch.Id,
        batch.Kind,
        batch.CreatedOwnerTurn,
        batch.Expiry,
        batch.Remaining,
        batch.IsPermanent);

    private static void ValidateAmount(int amount, string parameterName = "actualAmount")
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Block amounts must be non-negative.");
        }
    }

    private static void ValidateOwnerTurn(long ownerTurn, string parameterName)
    {
        if (ownerTurn < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Owner-turn numbers must be non-negative.");
        }
    }

    private static void ValidateExpiry(BlockExpiry expiry, string parameterName)
    {
        ValidateOwnerTurn(expiry.OwnerTurn, parameterName);
        if (expiry.Phase is not BlockExpiryPhase.OwnerTurnStart and not BlockExpiryPhase.OwnerTurnEnd)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Unknown Block expiry phase.");
        }
    }

    private sealed class Batch(
        long id,
        BlockBatchKind kind,
        long createdOwnerTurn,
        BlockExpiry? expiry,
        int remaining,
        bool isPermanent)
    {
        public long Id { get; } = id;
        public BlockBatchKind Kind { get; } = kind;
        public long CreatedOwnerTurn { get; } = createdOwnerTurn;
        public BlockExpiry? Expiry { get; set; } = expiry;
        public int Remaining { get; set; } = remaining;
        public bool IsPermanent { get; set; } = isPermanent;
    }
}
