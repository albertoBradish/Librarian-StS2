using Librarian.LibrarianCode.Cards.PowerCards;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace Librarian.LibrarianCode.Cards;

// Keep the v0.3.5 model ID so existing copies and saves become the defined multiplayer card.
public sealed class MultiplayerPlaceholderA() : ImplementedPowerCard(1, CardRarity.Rare)
{
    public override string PortraitPath => "res://Librarian/images/card_portraits/crowd_kindling.png";
    public override string CustomPortraitPath => "res://Librarian/images/card_portraits/crowd_kindling.png";
    public override CardMultiplayerConstraint MultiplayerConstraint => CardMultiplayerConstraint.MultiplayerOnly;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<CrowdKindlingPower>(3m)];

    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
        => await Apply<CrowdKindlingPower>(context, DynamicVars["CrowdKindlingPower"].BaseValue);

    protected override void OnUpgrade() => DynamicVars["CrowdKindlingPower"].UpgradeValueBy(1m);
}
