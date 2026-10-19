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
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<AfterRainPower>(3m)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<AfterRainPower>()];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var session = LibrarianRuntime.Get(Owner);
        await LibrarianRuntime.Dispatch(session, choiceContext,
            session.Orbs.Lock(OrbKind.Tide, 2, new(Id.ToString())));
        await Apply<AfterRainPower>(choiceContext, DynamicVars["AfterRainPower"].BaseValue);
    }
    protected override void OnUpgrade() => DynamicVars["AfterRainPower"].UpgradeValueBy(2m);
}
