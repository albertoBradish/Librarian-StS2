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

/// <summary>Source 6/r7. D24 Rare; upgraded card gains Innate while the rule remains Single.</summary>
public sealed class EternalGrimoire() : ImplementedPowerCard(1, CardRarity.Rare)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<EternalGrimoirePower>(1m)];
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
        => Apply<EternalGrimoirePower>(choiceContext, DynamicVars["EternalGrimoirePower"].BaseValue);
    protected override void OnUpgrade() => AddKeyword(CardKeyword.Innate);
}
