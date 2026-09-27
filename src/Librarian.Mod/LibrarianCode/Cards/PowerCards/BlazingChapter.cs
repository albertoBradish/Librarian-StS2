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

/// <summary>Source 85/r86. Add 1/2 extra Fire settlements; conversion is one rule run during the pre-snapshot phase.</summary>
public sealed class BlazingChapter() : ImplementedPowerCard(2, CardRarity.Rare)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<BlazingChapterPower>(1m)];
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
        => Apply<BlazingChapterPower>(choiceContext, DynamicVars["BlazingChapterPower"].BaseValue);
    protected override void OnUpgrade() => DynamicVars["BlazingChapterPower"].UpgradeValueBy(1m);
}
