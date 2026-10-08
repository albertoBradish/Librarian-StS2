using Librarian.LibrarianCode.Integration;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace Librarian.LibrarianCode.Cards;

[BaseLib.Utils.Attributes.CustomID("LIBRARIAN-TRICKLE")]
public sealed class Trickle() : LibrarianCard(1, CardType.Skill, CardRarity.Basic, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Water", 4m)];
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
        => LibrarianMechanicsBridge.Current.GainAsync(choiceContext, Owner, LibrarianElement.Water,
            DynamicVars["Water"].BaseValue, this, cardPlay);
    protected override void OnUpgrade() => DynamicVars["Water"].UpgradeValueBy(2m);
}
