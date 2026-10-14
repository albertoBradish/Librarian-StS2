using Librarian.Core;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.LibrarianCode.Powers.Implemented;

/// <summary>Amount is the extra Fire settlement count; each cast has an independent lock deadline.</summary>
public sealed class BlazingChapterPower : ImplementedLibrarianPower, IOrbEndTurnListener
{
    private int[] _pendingLockTurns = [];
    private int _pendingLockReferenceTurn;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DynamicVar("Pending", 0m), new StringVar("Delays", "")];

    [SavedProperty]
    public int[] PendingLockTurns
    {
        get => _pendingLockTurns.ToArray();
        set
        {
            AssertMutable();
            ArgumentNullException.ThrowIfNull(value);
            if (value.Any(turn => turn < 0)) throw new ArgumentOutOfRangeException(nameof(value));
            _pendingLockTurns = value.ToArray();
            UpdateProgress();
        }
    }

    [SavedProperty]
    public int PendingLockReferenceTurn
    {
        get => _pendingLockReferenceTurn;
        set
        {
            AssertMutable();
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            _pendingLockReferenceTurn = value;
            UpdateProgress();
        }
    }

    public int[] PendingLockDelays => _pendingLockTurns
        .Select(turn => Math.Max(0, turn - _pendingLockReferenceTurn)).ToArray();

    public void RegisterDelay(int turns)
    {
        AssertMutable();
        if (turns <= 0) throw new ArgumentOutOfRangeException(nameof(turns));
        _pendingLockReferenceTurn = checked((int)LibrarianRuntime.Get(Owner.Player!).Orbs.OwnerTurn);
        _pendingLockTurns = [.. _pendingLockTurns, checked(_pendingLockReferenceTurn + turns)];
        UpdateProgress();
    }

    private void UpdateProgress()
    {
        DynamicVars["Pending"].BaseValue = _pendingLockTurns.Length;
        string delays = string.Join(LibrarianLanguage.Format("LIST_SEPARATOR"), PendingLockDelays);
        ((StringVar)DynamicVars["Delays"]).StringValue = delays;
        InvokeDisplayAmountChanged();
    }

    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();
        _pendingLockTurns = _pendingLockTurns.ToArray();
    }

    public override async Task BeforeHandDraw(Player player, PlayerChoiceContext choiceContext, ICombatState combatState)
    {
        if (player != Owner.Player || Owner.IsDead || !Owner.Powers.Contains(this)) return;
        var session = LibrarianRuntime.Get(player);
        _pendingLockReferenceTurn = checked((int)session.Orbs.OwnerTurn);
        int due = _pendingLockTurns.Count(turn => turn <= _pendingLockReferenceTurn);
        // Consume deadlines before dispatch: a reentrant callback cannot repeat their lock events.
        _pendingLockTurns = _pendingLockTurns.Where(turn => turn > _pendingLockReferenceTurn).ToArray();
        UpdateProgress();
        if (due == 0) return;
        Flash();
        for (int i = 0; i < due && !Owner.IsDead && Owner.Powers.Contains(this); i++)
        {
            await LibrarianRuntime.Dispatch(session, choiceContext, session.Orbs.Lock(OrbKind.Tide, null, Origin));
            if (Owner.IsDead || !Owner.Powers.Contains(this)) return;
            await LibrarianRuntime.Dispatch(session, choiceContext, session.Orbs.Lock(OrbKind.Growth, null, Origin));
        }
    }

    public Task BeforeOrbSettlements(LibrarianSession session, PlayerChoiceContext context)
    {
        if (BelongsTo(session) && !Owner.IsDead && Owner.Powers.Contains(this))
            session.Orbs.QueueExtraSettlement(OrbSelector.Named(OrbKind.Fire, OrbScope.All), Amount, Id.ToString());
        return Task.CompletedTask;
    }
}
