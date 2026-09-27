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

/// <summary>v0.3.6: rare; fixes the foreground and grants three/four future turn-start doublings.</summary>
public sealed class Overfishing() : ImplementedPowerCard(2, CardRarity.Rare)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Turns", 3m)];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await Apply<OverfishingPower>(choiceContext, 1m);
        Owner.Creature.GetPower<OverfishingPower>()?.RegisterTurns(DynamicVars["Turns"].IntValue);
    }
    protected override void OnUpgrade() => DynamicVars["Turns"].UpgradeValueBy(1m);
}
