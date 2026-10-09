using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.LibrarianCode.Cards.NativeBatch;

/// <summary>Catalog 14, source row 15: four separately randomized hits of 4, upgraded to 6.</summary>
public sealed class FlyingPages() : LibrarianCard(2, CardType.Attack, CardRarity.Common, TargetType.RandomEnemy)
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(5m, ValueProp.Move), new RepeatVar(3)];

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
        => DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .WithHitCount(DynamicVars.Repeat.IntValue)
            .FromCard(this, cardPlay)
            .TargetingRandomOpponents(CombatState ?? throw new InvalidOperationException("Card play requires combat."))
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(choiceContext);

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(2m);
}
