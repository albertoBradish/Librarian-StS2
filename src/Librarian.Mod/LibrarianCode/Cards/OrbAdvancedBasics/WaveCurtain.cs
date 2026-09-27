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

/// <summary>Source 43/r44. Reading retained Tide does not mutate it; D05 creates ordinary block.</summary>
public sealed class WaveCurtain() : OrbAdvancedCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    public override bool GainsBlock => true;
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new CalculationBaseVar(0m), new CalculationExtraVar(1m),
            new CalculatedBlockVar(ValueProp.Move).WithMultiplier((card, _) => PreviewSession(card)?.Orbs.Value(OrbKind.Tide) ?? 0)];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        decimal amount = Session.Orbs.Value(OrbKind.Tide) * DynamicVars.CalculationExtra.BaseValue;
        await CreatureCmd.GainBlock(Owner.Creature, amount, ValueProp.Move, cardPlay);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}
