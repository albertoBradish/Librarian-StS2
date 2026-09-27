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
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.LibrarianCode.Powers.Implemented;

/// <summary>Source 58 basic. Each layer separately loses floor(current/2) from the explicitly selected backgrounds.</summary>
public sealed class OverfishingPower : ImplementedLibrarianPower
{
    public override PowerStackType StackType => PowerStackType.Single;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Remaining", 0m)];
    public override int DisplayAmount => DynamicVars["Remaining"].IntValue;
    public void RegisterTurns(int turns)
    {
        AssertMutable();
        if (turns < 0) throw new ArgumentOutOfRangeException(nameof(turns));
        DynamicVars["Remaining"].BaseValue = checked(DynamicVars["Remaining"].IntValue + turns);
        InvokeDisplayAmountChanged();
    }
    public bool TryConsumeTurn()
    {
        AssertMutable();
        if (DynamicVars["Remaining"].IntValue <= 0) return false;
        DynamicVars["Remaining"].BaseValue -= 1;
        InvokeDisplayAmountChanged();
        return true;
    }
    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        if (Owner.Player is { } player) LibrarianRuntime.Get(player).Orbs.SwitchLocked = true;
        return Task.CompletedTask;
    }
    public override Task AfterRemoved(Creature oldOwner)
    {
        if (LibrarianRuntime.For(oldOwner) is { } session) session.Orbs.SwitchLocked = false;
        return Task.CompletedTask;
    }
    public override async Task BeforeHandDraw(Player player, PlayerChoiceContext choiceContext, ICombatState combatState)
    {
        if (player != Owner.Player) return;
        var session = LibrarianRuntime.Get(player);
        if (!session.Orbs.TryMarkFirstThisTurn(Id.ToString()) || !TryConsumeTurn()) return;
        var kind = session.Orbs.Foreground;
        await LibrarianRuntime.Dispatch(session, choiceContext, session.Orbs.Strengthen(kind, session.Orbs.Value(kind), OrbScope.All, Origin));
    }
}
