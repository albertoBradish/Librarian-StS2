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

/// <summary>v0.3.0 approved power rule.</summary>
[BaseLib.Utils.Attributes.CustomID("LIBRARIAN-SEDIMENTATION")]
public sealed class Sedimentation() : ImplementedPowerCard(1, CardRarity.Uncommon)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<SedimentationPower>(5m)];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await Apply<SedimentationPower>(choiceContext, DynamicVars["SedimentationPower"].BaseValue);
    }
    protected override void OnUpgrade() => DynamicVars["SedimentationPower"].UpgradeValueBy(2m);
}
