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

/// <summary>V0.4.1: snapshot Tide, clear it, then make two/three random hits before locking Fire and Growth.</summary>
[BaseLib.Utils.Attributes.CustomID("LIBRARIAN-SEA_BURIAL")]
public sealed class SeaBurial() : OrbAdvancedCard(2, CardType.Attack, CardRarity.Uncommon, TargetType.RandomEnemy)
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [.. CalculatedDamage((card, _) => PreviewForeground(card, OrbKind.Tide)), new RepeatVar(2)];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int actual = await LoseAll(choiceContext, Session, OrbKind.Tide);
        var combat = CombatState ?? throw new InvalidOperationException("Attack requires combat.");
        for (int i = 0; i < Amount("Repeat"); i++)
        {
            if (MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsOverOrEnding || Owner.Creature.IsDead
                || combat.HittableEnemies.Count == 0) break;
            await DamageCmd.Attack(actual).FromCard(this, cardPlay)
                .TargetingRandomOpponents(combat).WithHitFx("vfx/vfx_attack_slash").Execute(choiceContext);
        }
        await LibrarianRuntime.Dispatch(Session, choiceContext,
            Session.Orbs.Lock(OrbKind.Fire, 1, Origin));
        await LibrarianRuntime.Dispatch(Session, choiceContext,
            Session.Orbs.Lock(OrbKind.Growth, 1, Origin));
    }
    protected override void OnUpgrade() => DynamicVars.Repeat.UpgradeValueBy(1m);
}
