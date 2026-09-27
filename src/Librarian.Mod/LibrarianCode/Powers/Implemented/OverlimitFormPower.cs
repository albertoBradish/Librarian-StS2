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

public sealed class OverlimitFormPower : ImplementedLibrarianPower
{
    public override PowerStackType StackType => PowerStackType.Single;
    public override async Task BeforeHandDraw(Player player, PlayerChoiceContext choiceContext, ICombatState combatState)
    {
        if (player != Owner.Player) return;
        Flash();
        var session = LibrarianRuntime.Get(player);
        foreach (var kind in session.Orbs.Positions.ToArray())
            await LibrarianRuntime.Dispatch(session, choiceContext, session.Orbs.ActivateWithoutSwitch(kind, OrbScope.All, Origin));
    }
}
