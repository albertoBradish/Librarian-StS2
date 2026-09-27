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

/// <summary>Source 85. One conversion rule; Amount is the additive number of extra Fire settlements.</summary>
public sealed class BlazingChapterPower : ImplementedLibrarianPower, IOrbEndTurnListener
{
    public async Task BeforeOrbSettlements(LibrarianSession session, PlayerChoiceContext context)
    {
        if (!BelongsTo(session)) return;
        int extra = Amount;
        // Named losses now apply across positions, in text order with callbacks between operations.
        var tide = session.Orbs.LoseAll(OrbKind.Tide, OrbScope.All, Origin);
        await LibrarianRuntime.Dispatch(session, context, tide);
        var growth = session.Orbs.LoseAll(OrbKind.Growth, OrbScope.All, Origin);
        await LibrarianRuntime.Dispatch(session, context, growth);
        await Gain(context, session, OrbKind.Fire, checked(tide.ActualAmount + growth.ActualAmount));
        session.Orbs.QueueExtraSettlement(OrbSelector.Named(OrbKind.Fire, OrbScope.All), extra, Id.ToString());
    }
}
