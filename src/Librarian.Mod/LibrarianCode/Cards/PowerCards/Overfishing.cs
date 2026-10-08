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

/// <summary>V1.2.0-beta2: permanently lock the current back Orbs, then double the current foreground for three/four turn starts.</summary>
[BaseLib.Utils.Attributes.CustomID("LIBRARIAN-OVERFISHING")]
public sealed class Overfishing() : ImplementedPowerCard(2, CardRarity.Rare)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Turns", 3m)];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var session = Session;
        var background = session.Orbs.Select(OrbScope.Background).ToArray();
        foreach (var kind in background)
            await LibrarianRuntime.Dispatch(session, choiceContext,
                session.Orbs.Lock(kind, origin: new(Id.ToString())));
        await Apply<OverfishingPower>(choiceContext, 1m);
        Owner.Creature.GetPower<OverfishingPower>()?.RegisterTurns(DynamicVars["Turns"].IntValue);
    }
    protected override void OnUpgrade() => DynamicVars["Turns"].UpgradeValueBy(1m);
}
