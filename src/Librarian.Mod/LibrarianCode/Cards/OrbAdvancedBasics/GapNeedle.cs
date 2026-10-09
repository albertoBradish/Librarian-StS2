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

/// <summary>Source 27/r28. D02 explicitly reads both background values and uses their absolute difference.</summary>
public sealed class GapNeedle() : OrbAdvancedCard(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    private static int Difference(LibrarianSession session)
    {
        var background = session.Orbs.Select(OrbScope.Background);
        return Math.Abs(session.Orbs.Value(background[0]) - session.Orbs.Value(background[1]));
    }
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new CalculationBaseVar(9m), new ExtraDamageVar(1m),
            new CalculatedDamageVar(ValueProp.Move).WithMultiplier((card, _) => PreviewSession(card) is { } session ? Difference(session) : 0)];
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
        => Hit(choiceContext, cardPlay, DynamicVars.CalculationBase.BaseValue + Difference(Session));
    protected override void AfterDowngraded()
    {
        DynamicVars.RecalculateForUpgradeOrEnchant();
        DynamicVars.FinalizeUpgrade();
    }
    protected override void OnUpgrade() => DynamicVars.CalculationBase.UpgradeValueBy(4m);
}
