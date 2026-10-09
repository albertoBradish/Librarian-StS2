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

/// <summary>v0.4.0: clear Growth even at X0; distribute its actual loss once across random hits.</summary>
public sealed class EarthCollapse() : OrbAdvancedCard(0, CardType.Attack, CardRarity.Uncommon, TargetType.RandomEnemy)
{
    protected override bool HasEnergyCostX => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(8m, ValueProp.Move)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        int count = ResolveEnergyXValue();
        if (count <= 0) return;
        int lost = await LoseAll(context, Session, OrbKind.Growth);
        var shares = DamagePartition.Split(lost, count, new LibrarianRuntime.GameOrbRandom(Owner));
        foreach (int share in shares)
        {
            if (MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsOverOrEnding || Owner.Creature.IsDead) break;
            await DamageCmd.Attack(DynamicVars.Damage.BaseValue + share).FromCard(this, play)
                .TargetingRandomOpponents(CombatState ?? throw new InvalidOperationException("Attack requires combat."))
                .WithHitFx("vfx/vfx_attack_slash").Execute(context);
        }
    }
    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(3m);
}
