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

/// <summary>Source 41/r42. Both versions are Innate and Exhaust; Tide5 upgrades to7.</summary>
public sealed class OpeningTide() : OrbAdvancedCard(0, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust, CardKeyword.Innate];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Tide", 5m)];
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
        => Gain(choiceContext, Session, OrbKind.Tide, Amount("Tide"));
    protected override void OnUpgrade() => DynamicVars["Tide"].UpgradeValueBy(3m);
}
