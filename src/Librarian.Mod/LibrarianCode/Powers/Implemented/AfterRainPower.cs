using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.LibrarianCode.Powers.Implemented;

/// <summary>V1.4.0: only future, owner-attributed fatal damage heals. Retired Tide-floor state retains its identity.</summary>
public sealed class AfterRainPower : ImplementedLibrarianPower
{
    public override string CustomIconPath => "res://Librarian/images/powers/v0.6.0/unretreating_tide.png";
    public override string CustomBigIconPath => "res://Librarian/images/powers/v0.6.0/big/unretreating_tide.png";

    public override async Task AfterDamageGiven(PlayerChoiceContext context, Creature? dealer,
        DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
    {
        if (Owner.IsDead || (dealer != Owner && dealer?.PetOwner?.Creature != Owner)
            || target.Side == Owner.Side || !result.WasTargetKilled
            || target.Powers.Any(p => !p.ShouldOwnerDeathTriggerFatal())) return;
        Flash();
        await CreatureCmd.Heal(Owner, Amount);
    }
}
