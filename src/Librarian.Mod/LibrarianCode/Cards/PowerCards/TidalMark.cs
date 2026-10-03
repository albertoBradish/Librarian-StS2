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

/// <summary>V1.2.0-beta2: one energy at both stages; each applied Orb lock grants three/five Waves.</summary>
public sealed class TidalMark() : ImplementedPowerCard(1, CardRarity.Rare)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<TidalMarkPower>(3m)];
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
        => Apply<TidalMarkPower>(choiceContext, DynamicVars["TidalMarkPower"].BaseValue);
    protected override void OnUpgrade() => DynamicVars["TidalMarkPower"].UpgradeValueBy(2m);
}
