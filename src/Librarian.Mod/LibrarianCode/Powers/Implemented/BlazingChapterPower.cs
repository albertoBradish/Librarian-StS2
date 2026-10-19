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
public sealed class BlazingChapterPower : ImplementedLibrarianPower
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

    public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay play)
    {
        if (play.Card.Owner != Owner.Player || play.Card.Type != CardType.Attack || Owner.IsDead
            || CombatManager.Instance.IsOverOrEnding) return;
        var session = LibrarianRuntime.Get(Owner.Player!);
        var loss = session.Orbs.Lose(OrbKind.Fire, Amount, OrbScope.All, Origin);
        await LibrarianRuntime.Dispatch(session, context, loss);
        if (loss.ActualAmount > 0) await PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>(context, Owner, loss.ActualAmount, Owner, play.Card);
    }
}
