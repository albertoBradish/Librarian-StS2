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

/// <summary>Source 83/D15. Actual Tide gains and losses grant matching Waves per layer; zero changes do not trigger.</summary>
public sealed class TidalMarkPower : ImplementedLibrarianPower, IOrbEventListener
{
    public async Task OnOrbEvent(LibrarianSession session, PlayerChoiceContext context, OrbEvent change)
    {
        if (!OwnEvent(session, change) || change.Orb != OrbKind.Tide || change.ActualAmount <= 0
            || change.Kind is not (OrbEventKind.Lost or OrbEventKind.Gained)) return;
        Flash();
        session.Waves.Add(checked(change.ActualAmount * Amount));
        LibrarianRuntime.VerifyBlock(session);
        await Task.CompletedTask;
    }
}
