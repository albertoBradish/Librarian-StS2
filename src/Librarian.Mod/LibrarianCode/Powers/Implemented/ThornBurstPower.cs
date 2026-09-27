using Librarian.Core;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace Librarian.LibrarianCode.Powers.Implemented;

/// <summary>V0.4.1 positive duration for Thorn Burst; each owner turn grows six.</summary>
public sealed class ThornBurstPower : ImplementedLibrarianPower
{
    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task BeforeHandDraw(Player player, PlayerChoiceContext choiceContext, ICombatState combatState)
    {
        if (player != Owner.Player || Amount <= 0 || Owner.IsDead) return;
        var session = LibrarianRuntime.Get(player);
        Flash();
        await Gain(choiceContext, session, OrbKind.Growth, 6);
        int remaining = Amount - 1;
        if (remaining <= 0) RemoveInternal();
        else SetAmount(remaining);
    }
}
