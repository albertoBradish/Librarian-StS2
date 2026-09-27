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

/// <summary>Source 82/r83. v0.3.4: each extinguish gains Fire, with no draw effect.</summary>
public sealed class EmberBookmark() : ImplementedPowerCard(1, CardRarity.Uncommon)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<EmberBookmarkPower>(1m)];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await Apply<EmberBookmarkPower>(choiceContext, DynamicVars["EmberBookmarkPower"].BaseValue);
    }
    protected override void OnUpgrade() => DynamicVars["EmberBookmarkPower"].UpgradeValueBy(1m);
}
