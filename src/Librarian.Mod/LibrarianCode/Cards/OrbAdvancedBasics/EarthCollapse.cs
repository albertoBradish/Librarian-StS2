using Librarian.Core;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.LibrarianCode.Cards.OrbAdvancedBasics;

/// <summary>V1.3.11 approved 20-card revision. Published model identity is retained.</summary>
public sealed class EarthCollapse() : OrbAdvancedCard(3, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(24m, ValueProp.Move), new DamageVar("SplashDamage", 12m, ValueProp.Move)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        ArgumentNullException.ThrowIfNull(play.Target);
        // One native attack context: all damage precedes attack-completion hooks.
#if LIBRARIAN_API_0111
        await using AttackContext attack = await AttackCommand.CreateContextAsync(CombatState!, context, play);
#else
        await using AttackContext attack = await AttackCommand.CreateContextAsync(CombatState!, context, this);
#endif
        var others = CombatState!.GetTeammatesOf(play.Target).Where(e => e != play.Target && e.IsHittable).ToArray();
#if LIBRARIAN_API_0111
        attack.AddHit(await CreatureCmd.Damage(context, new[] { play.Target }, DynamicVars.Damage.BaseValue, ValueProp.Move, Owner.Creature, this, play));
#else
        attack.AddHit(await CreatureCmd.Damage(context, new[] { play.Target }, DynamicVars.Damage.BaseValue, ValueProp.Move, Owner.Creature, this));
#endif
        var remaining = others.Where(e => e.IsHittable).ToArray();
        if (remaining.Length > 0)
#if LIBRARIAN_API_0111
            attack.AddHit(await CreatureCmd.Damage(context, remaining, DynamicVars["SplashDamage"].BaseValue,
                ValueProp.Move, Owner.Creature, this, play));
#else
            attack.AddHit(await CreatureCmd.Damage(context, remaining, DynamicVars["SplashDamage"].BaseValue,
                ValueProp.Move, Owner.Creature, this));
#endif
    }
    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(4m);
        DynamicVars["SplashDamage"].UpgradeValueBy(2m);
    }
}
