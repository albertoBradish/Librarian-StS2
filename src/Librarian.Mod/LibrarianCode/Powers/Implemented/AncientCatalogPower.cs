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

/// <summary>v0.4.0: each explicit channel or positive Growth gain strengthens one random other orb exactly once.</summary>
public sealed class AncientCatalogPower : ImplementedLibrarianPower, IOrbEventListener
{
    public async Task OnOrbEvent(LibrarianSession session, PlayerChoiceContext context, OrbEvent change)
    {
        if (!OwnEvent(session, change) || change.Orb != OrbKind.Growth) return;
        if (change.Kind != OrbEventKind.Channelled && !(change.Kind == OrbEventKind.Gained && change.ActualAmount > 0)) return;
        Flash();
        var others = session.Orbs.Positions.Where(k => k != OrbKind.Growth).ToArray();
        var kind = others[new LibrarianRuntime.GameOrbRandom(session.Player).NextInt(others.Length)];
        await LibrarianRuntime.Dispatch(session, context, session.Orbs.Strengthen(kind, checked((int)Amount), OrbScope.All, Origin));
    }
}
