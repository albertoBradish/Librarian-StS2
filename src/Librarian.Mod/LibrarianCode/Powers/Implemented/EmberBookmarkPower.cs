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

/// <summary>Source 82. Amount stores the Fire gained after every actual extinguish.</summary>
public sealed class EmberBookmarkPower : ImplementedLibrarianPower, IOrbEventListener
{
    public async Task OnOrbEvent(LibrarianSession session, PlayerChoiceContext context, OrbEvent change)
    {
        if (!OwnEvent(session, change) || change.Kind != OrbEventKind.Extinguished || change.Orb != OrbKind.Fire) return;
        Flash();
        await Gain(context, session, OrbKind.Fire, Amount);
    }
}
