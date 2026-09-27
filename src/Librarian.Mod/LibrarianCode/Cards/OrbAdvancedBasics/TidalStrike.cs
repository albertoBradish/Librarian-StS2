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

/// <summary>Source 31/r32. Any positive Wave stacks grant the additional hit.</summary>
public sealed class TidalStrike() : OrbAdvancedCard(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    protected override HashSet<CardTag> CanonicalTags => [CardTag.Strike];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new CalculationBaseVar(8m), new ExtraDamageVar(1m),
            new CalculatedDamageVar(ValueProp.Move).WithMultiplier((card, _) => PreviewSession(card)?.Waves.Amount ?? 0)];
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
        => Hit(choiceContext, cardPlay, DynamicVars.CalculationBase.BaseValue + Session.Waves.Amount);
    protected override void AfterDowngraded()
    {
        DynamicVars.RecalculateForUpgradeOrEnchant();
        DynamicVars.FinalizeUpgrade();
    }
    protected override void OnUpgrade() => DynamicVars.CalculationBase.UpgradeValueBy(3m);
}
