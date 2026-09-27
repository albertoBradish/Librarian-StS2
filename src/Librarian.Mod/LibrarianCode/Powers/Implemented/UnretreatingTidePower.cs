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

public sealed class UnretreatingTidePower : ImplementedLibrarianPower
{
    // The card applies only the difference to the strongest floor already present.
    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
        => SyncFloor();

    // Native AfterApplied runs only for the first instance, not when an upgraded
    // copy increases its amount. Keep the combat ledger current in the same action.
    public override Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power,
        decimal amount, Creature? applier, CardModel? cardSource)
        => ReferenceEquals(power, this) ? SyncFloor() : Task.CompletedTask;

    private Task SyncFloor()
    {
        if (Owner.Player is { } player) LibrarianRuntime.Get(player).Waves.RetentionFloor = Math.Max(0, Amount);
        return Task.CompletedTask;
    }

    public override Task AfterRemoved(Creature oldOwner)
    {
        if (oldOwner.Player is { } player && LibrarianRuntime.TryGet(player, out var session))
            session!.Waves.RetentionFloor = 0;
        return Task.CompletedTask;
    }
}
