using Librarian.Core;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.LibrarianCode.Cards.OrbAdvancedBasics;

/// <summary>Source 33/r34. Independent activation condition; the two Weak applications retain text order.</summary>
[BaseLib.Utils.Attributes.CustomID("LIBRARIAN-TIDAL_EROSION")]
public sealed class TidalErosion() : OrbAdvancedCard(1, CardType.Attack, CardRarity.Uncommon, TargetType.AllEnemies)
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(8m, ValueProp.Move), new PowerVar<WeakPower>(1m)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<WeakPower>()];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var session = Session;
        await HitAll(choiceContext, cardPlay, DynamicVars.Damage.BaseValue);
        var enemies = Owner.Creature.CombatState!.GetOpponentsOf(Owner.Creature).Where(c => c.IsHittable).ToArray();
        foreach (var enemy in enemies)
        {
            await PowerCmd.Apply<WeakPower>(choiceContext, enemy, DynamicVars.Weak.BaseValue, Owner.Creature, this);
            if (session.Orbs.IsActivated(OrbKind.Tide) && enemy.IsHittable)
                await PowerCmd.Apply<WeakPower>(choiceContext, enemy, DynamicVars.Weak.BaseValue, Owner.Creature, this);
        }
    }
    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(4m);
}
