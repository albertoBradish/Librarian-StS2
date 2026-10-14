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

/// <summary>v0.3.0 approved revision, catalog 57. Stable model ID retained.</summary>
public sealed class Ignite() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(1, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CalculationBaseVar(7m), new CalculationExtraVar(4m), new CalculatedBlockVar(ValueProp.Move).WithMultiplier((card, _) => PreviewLockedCount(card))];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await Block(play, Amount("CalculationBase") + LockedCount * Amount("CalculationExtra"));
    }
    protected override void OnUpgrade()
    {
        DynamicVars.CalculationBase.UpgradeValueBy(3m);
    }
    protected override void AfterDowngraded() { DynamicVars.RecalculateForUpgradeOrEnchant(); DynamicVars.FinalizeUpgrade(); }
}
