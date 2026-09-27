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

public sealed class SpacetimeTwistPower : ImplementedLibrarianPower
{
    public override async Task BeforeHandDraw(Player player, PlayerChoiceContext choiceContext, ICombatState combatState)
    {
        if (player != Owner.Player) return;
        var session = LibrarianRuntime.Get(player);
        var locked = session.Orbs.ResolveSelector(OrbSelector.Random(OrbScope.All), new LibrarianRuntime.GameOrbRandom(player))!.Value;
        var others = session.Orbs.Positions.Where(k => k != locked).ToArray();
        Flash();
        foreach (var kind in others)
        {
            await LibrarianRuntime.Dispatch(session, choiceContext, session.Orbs.Strengthen(kind, Amount, OrbScope.All, Origin));
            await LibrarianRuntime.Dispatch(session, choiceContext, session.Orbs.Activate(kind, OrbScope.All, Origin));
        }
        await LibrarianRuntime.Dispatch(session, choiceContext, session.Orbs.Lock(locked, 1, Origin));
    }
}
