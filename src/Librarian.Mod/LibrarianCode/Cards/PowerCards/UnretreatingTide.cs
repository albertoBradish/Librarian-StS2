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

/// <summary>v0.3.0 approved power rule.</summary>
public sealed class UnretreatingTide() : ImplementedPowerCard(1, CardRarity.Uncommon)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<UnretreatingTidePower>(8m)];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int target = DynamicVars["UnretreatingTidePower"].IntValue;
        int existing = Owner.Creature.GetPower<UnretreatingTidePower>()?.Amount ?? 0;
        if (target > existing) await Apply<UnretreatingTidePower>(choiceContext, target - existing);
    }
    protected override void OnUpgrade() => DynamicVars["UnretreatingTidePower"].UpgradeValueBy(4m);
}
