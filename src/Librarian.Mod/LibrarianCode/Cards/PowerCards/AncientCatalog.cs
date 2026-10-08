using Librarian.Core;
using Librarian.Mechanics;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace Librarian.LibrarianCode.Cards.PowerCards;

/// <summary>Source 86/r87. Strengthen the selected lowest background 3/5 on each genuine Growth extinguish.</summary>
[BaseLib.Utils.Attributes.CustomID("LIBRARIAN-ANCIENT_CATALOG")]
public sealed class AncientCatalog() : ImplementedPowerCard(2, CardRarity.Rare)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<AncientCatalogPower>(3m)];
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
        => Apply<AncientCatalogPower>(choiceContext, DynamicVars["AncientCatalogPower"].BaseValue);
    protected override void OnUpgrade() => DynamicVars["AncientCatalogPower"].UpgradeValueBy(2m);
}
