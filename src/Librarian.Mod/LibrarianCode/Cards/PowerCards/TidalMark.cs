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

/// <summary>Source 83/r84; D15. Threshold is at least four actual Tide lost once, with fixed 3/5 Block per trigger.</summary>
public sealed class TidalMark() : ImplementedPowerCard(3, CardRarity.Ancient)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<TidalMarkPower>(1m)];
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
        => Apply<TidalMarkPower>(choiceContext, DynamicVars["TidalMarkPower"].BaseValue);
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}
