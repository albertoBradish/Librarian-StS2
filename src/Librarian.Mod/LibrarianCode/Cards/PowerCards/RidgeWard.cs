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

/// <summary>V1.2.0-beta2: Ancient with Retain at both stages; its existing effects and cost upgrade remain.</summary>
[BaseLib.Utils.Attributes.CustomID("LIBRARIAN-RIDGE_WARD")]
public sealed class RidgeWard() : ImplementedPowerCard(2, CardRarity.Ancient)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Retain];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<RidgeWardPower>(1m)];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await Session.Orbs.SettleImmediatelyAsync(OrbSelector.Named(OrbKind.Tide, OrbScope.All), 1,
            request => LibrarianRuntime.Settle(Session, choiceContext, request), Id.ToString(),
            canSettle: () => !MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsOverOrEnding && !Owner.Creature.IsDead);
        await Apply<RidgeWardPower>(choiceContext, DynamicVars["RidgeWardPower"].BaseValue);
        await LibrarianRuntime.Dispatch(Session, choiceContext, Session.Orbs.Lock(OrbKind.Tide, origin: new(Id.ToString())));
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}
