using Librarian.LibrarianCode.Integration;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace Librarian.LibrarianCode.Cards;

public sealed class Spark() : LibrarianCard(1, CardType.Skill, CardRarity.Basic, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Fire", 5m)];
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
        => LibrarianMechanicsBridge.Current.GainAsync(choiceContext, Owner, LibrarianElement.Fire,
            DynamicVars["Fire"].BaseValue, this, cardPlay);
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}
