using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace Librarian.LibrarianCode.Cards;

/// <summary>A small repeatable, directed draw exchange; native branching context handles teammate choices.</summary>
public sealed class CirculationNotes() : LibrarianCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.AnyAlly)
{
    public override CardMultiplayerConstraint MultiplayerConstraint => CardMultiplayerConstraint.MultiplayerOnly;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(1), new IntVar("AllyCards", 3m)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var target = play.Target;
        if (target is null || target == Owner.Creature || !target.IsAlive || target.Player is not { } ally
            || target.Side != Owner.Creature.Side || target.CombatState != Owner.Creature.CombatState) return;
        await CardPileCmd.Draw(context, DynamicVars.Cards.IntValue, Owner);
        if (target.IsAlive && target.CombatState == Owner.Creature.CombatState)
            await CardPileCmd.DrawWithoutBlockingOnOtherPlayers(context, DynamicVars["AllyCards"].IntValue, ally, this);
    }
    protected override void OnUpgrade() => DynamicVars["AllyCards"].UpgradeValueBy(1m);
}
