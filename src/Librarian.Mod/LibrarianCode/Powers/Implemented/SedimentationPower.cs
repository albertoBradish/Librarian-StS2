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

public sealed class SedimentationPower : ImplementedLibrarianPower, IOrbEndTurnListener
{
    public async Task BeforeOrbSettlements(LibrarianSession session, PlayerChoiceContext context)
    {
        if (!BelongsTo(session) || Owner.IsDead) return;
        var targets = session.Orbs.Select(OrbScope.Background, ActivationFilter.Inactive).ToArray();
        if (targets.Length > 0) Flash();
        foreach (var kind in targets)
            await LibrarianRuntime.Dispatch(session, context,
                session.Orbs.Strengthen(kind, Amount, OrbScope.All, Origin));
    }
}
