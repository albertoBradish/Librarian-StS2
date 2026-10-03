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

/// <summary>v0.4.0: one energy; owner-turn Growth gain 4/6.</summary>
public sealed class RingCurriculum() : ImplementedPowerCard(1, CardRarity.Uncommon)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<RingCurriculumPower>(3m)];
    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
        => Apply<RingCurriculumPower>(choiceContext, DynamicVars["RingCurriculumPower"].BaseValue);
    protected override void OnUpgrade() => DynamicVars["RingCurriculumPower"].UpgradeValueBy(2m);
}
