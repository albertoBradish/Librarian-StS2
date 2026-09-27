using Librarian.Core;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.LibrarianCode.Cards.OrbAdvancedBasics;

/// <summary>Source 28/r29. User override: lose Tide in either position, snapshot remaining Tide, attack once or twice.</summary>
public sealed class SeverCurrent() : OrbAdvancedCard(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DynamicVar("Loss", 6m), new DynamicVar("Repeat", 1m), .. CalculatedDamage((card, _) =>
            Math.Max(0, (PreviewSession(card)?.Orbs.Value(OrbKind.Tide) ?? 0) - card.DynamicVars["Loss"].BaseValue))];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var session = Session;
        await LibrarianRuntime.Dispatch(session, choiceContext,
            session.Orbs.Lose(OrbKind.Tide, Amount("Loss"), OrbScope.All, Origin));
        await DamageCmd.Attack(session.Orbs.Value(OrbKind.Tide)).FromCard(this, cardPlay)
            .Targeting(cardPlay.Target!).WithHitCount(Amount("Repeat"))
            .WithHitFx("vfx/vfx_attack_slash").Execute(choiceContext);
    }
    protected override void OnUpgrade() => DynamicVars["Repeat"].UpgradeValueBy(1m);
}
