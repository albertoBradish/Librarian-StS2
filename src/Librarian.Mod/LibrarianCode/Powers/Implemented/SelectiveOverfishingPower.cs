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

/// <summary>Source 58 upgraded. A separate Counter preserves mixed basic/upgraded applications.</summary>
public sealed class SelectiveOverfishingPower : ImplementedLibrarianPower
{
    public override async Task BeforeHandDraw(Player player, PlayerChoiceContext choiceContext, ICombatState combatState)
    {
        if (player != Owner.Player) return;
        var session = LibrarianRuntime.Get(player);
        int layers = Amount;
        for (int i = 0; i < layers; i++)
        {
            var target = session.Orbs.ResolveSelector(OrbSelector.Random(OrbScope.Background), new LibrarianRuntime.GameOrbRandom(player));
            if (target is { } kind)
                await LibrarianRuntime.Dispatch(session, choiceContext, session.Orbs.LoseHalf(kind, OrbScope.Background, Origin));
        }
    }
}
