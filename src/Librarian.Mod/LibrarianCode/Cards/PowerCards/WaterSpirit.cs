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

/// <summary>v0.4.0: install the single passive before the immediate owner-only Tide gain.</summary>
public sealed class WaterSpirit() : ImplementedPowerCard(2, CardRarity.Rare)
{
    public override CardMultiplayerConstraint MultiplayerConstraint => CardMultiplayerConstraint.MultiplayerOnly;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Tide", 8m)];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var session = Session;
        await Apply<WaterSpiritPower>(choiceContext, 1m);
        await LibrarianRuntime.Dispatch(session, choiceContext, session.Orbs.Gain(OrbKind.Tide,
            checked((int)DynamicVars["Tide"].BaseValue), new(Id.ToString())));
    }
    protected override void OnUpgrade() => DynamicVars["Tide"].UpgradeValueBy(5m);
}
