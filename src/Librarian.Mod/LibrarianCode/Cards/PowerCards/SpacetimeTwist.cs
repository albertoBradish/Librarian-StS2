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

/// <summary>V1.2.0-beta2: its Power locks the selected Orb before strengthening and activating the others.</summary>
public sealed class SpacetimeTwist() : ImplementedPowerCard(2, CardRarity.Rare)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<SpacetimeTwistPower>(4m)];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await Apply<SpacetimeTwistPower>(choiceContext, DynamicVars["SpacetimeTwistPower"].BaseValue);
    }
    protected override void OnUpgrade() => DynamicVars["SpacetimeTwistPower"].UpgradeValueBy(2m);
}
