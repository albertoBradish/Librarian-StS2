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

/// <summary>Source 88/r89. Extra counts add; each common snapshot checks all three active states once.</summary>
public sealed class OverlimitForm() : ImplementedPowerCard(3, CardRarity.Rare)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Ethereal];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<OverlimitFormPower>(1m)];
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
        => Apply<OverlimitFormPower>(choiceContext, DynamicVars["OverlimitFormPower"].BaseValue);
    protected override void OnUpgrade() => RemoveKeyword(CardKeyword.Ethereal);
}
