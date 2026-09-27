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

public sealed class RidgeWardPower : ImplementedLibrarianPower
{
    public override PowerStackType StackType => PowerStackType.Single;
    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        if (Owner.Player is { } player) LibrarianRuntime.Get(player).Waves.Retained = true;
        return Task.CompletedTask;
    }
}
