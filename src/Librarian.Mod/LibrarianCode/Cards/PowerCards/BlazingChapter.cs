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

/// <summary>V1.2.0-beta2: immediately add one extra Fire settlement; lock the other Orbs after two/three turns.</summary>
public sealed class BlazingChapter() : ImplementedPowerCard(2, CardRarity.Rare)
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new PowerVar<BlazingChapterPower>(1m), new DynamicVar("Turns", 2m)];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await Apply<BlazingChapterPower>(choiceContext, DynamicVars["BlazingChapterPower"].BaseValue);
        Owner.Creature.GetPower<BlazingChapterPower>()?.RegisterDelay(DynamicVars["Turns"].IntValue);
    }
    protected override void OnUpgrade() => DynamicVars["Turns"].UpgradeValueBy(1m);
}
