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

/// <summary>v0.4.0: fixed one energy, no Retain, upgraded Innate; existing power timing retained.</summary>
[BaseLib.Utils.Attributes.CustomID("LIBRARIAN-LIFELINE")]
public sealed class Lifeline() : ImplementedPowerCard(1, CardRarity.Rare)
{

    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<LifelinePower>(1m)];
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
        => Apply<LifelinePower>(choiceContext, DynamicVars["LifelinePower"].BaseValue);
    protected override void OnUpgrade() => AddKeyword(CardKeyword.Innate);
}
