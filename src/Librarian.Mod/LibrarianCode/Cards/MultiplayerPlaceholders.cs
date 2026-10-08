using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace Librarian.LibrarianCode.Cards;

[BaseLib.Utils.Attributes.CustomID("LIBRARIAN-MULTIPLAYER_PLACEHOLDER_B")]
public sealed class MultiplayerPlaceholderB() : LibrarianCard(1, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    public override string PortraitPath => "res://Librarian/images/card_portraits/binding.png";
    public override string CustomPortraitPath => "res://Librarian/images/card_portraits/binding.png";
    public override CardMultiplayerConstraint MultiplayerConstraint => CardMultiplayerConstraint.MultiplayerOnly;
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override Task OnPlay(PlayerChoiceContext context, CardPlay play)
        => Librarian.Mechanics.LibrarianRuntime.BindSharedAsync(context, Owner, this);
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}
