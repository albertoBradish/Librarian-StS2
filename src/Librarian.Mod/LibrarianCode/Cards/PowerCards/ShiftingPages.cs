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

/// <summary>Source 60/r61. Stack additive Block per real foreground switch.</summary>
public sealed class ShiftingPages() : ImplementedPowerCard(1, CardRarity.Uncommon)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<ShiftingPagesPower>(3m)];
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
        => Apply<ShiftingPagesPower>(choiceContext, DynamicVars["ShiftingPagesPower"].BaseValue);
    protected override void OnUpgrade() => DynamicVars["ShiftingPagesPower"].UpgradeValueBy(1m);
}
