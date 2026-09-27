using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards.NativeBatch;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Relics;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace Librarian.Mechanics;

/// <summary>Opt-in real-engine effect fixtures; called only by the isolated runtime audit.</summary>
internal static class DevelopmentRevision035RelicAudit
{
    internal static async Task Run(Player player, Func<Task> freshFight)
    {
        var context = new ThrowingPlayerChoiceContext();
        var added = new List<RelicModel>();
        int checks = 0;
        LibrarianSession Session() => LibrarianRuntime.Get(player);
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("035 relic: " + label);
            checks++;
            MainFile.Logger.Info("RELIC035_CHECK_PASS " + label);
        }
        async Task Reset()
        {
            foreach (var relic in added.ToArray()) await RelicCmd.Remove(relic);
            added.Clear();
            await freshFight();
        }
        async Task<T> Add<T>() where T : RelicModel
        {
            var relic = await RelicCmd.Obtain<T>(player);
            added.Add(relic);
            return relic;
        }
        Task Gain(OrbKind kind, int amount) => LibrarianRuntime.Dispatch(Session(), context,
            Session().Orbs.Gain(kind, amount, new("035-relic-audit")));
        Task PutOut(OrbKind kind) => LibrarianRuntime.Dispatch(Session(), context,
            Session().Orbs.Extinguish(kind, OrbScope.All));
        async Task Settle(OrbKind kind, bool half = false)
        {
            var s = Session();
            await LibrarianRuntime.Settle(s, context, new(s.Orbs.OwnerId, s.Orbs.OwnerTurn, kind,
                s.Orbs.Value(kind), SettlementReason.Immediate, "035-relic-audit", 1, 1, half));
        }
        Task Start(RelicModel relic) => relic.BeforeSideTurnStart(context, CombatSide.Player,
            new[] { player.Creature }, player.Creature.CombatState!);
        async Task Natural()
        {
            var s = Session();
            await s.Orbs.ResolveEndTurnAsync(new(), r => LibrarianRuntime.Settle(s, context, r),
                op => new ValueTask(LibrarianRuntime.Dispatch(s, context, op)));
        }
        try
        {
            await Reset();
            var fire = await Add<LibrarianCommonPlaceholder>();
            await fire.BeforeCombatStart();
            await Start(fire);
            Check(Session().Orbs.Value(OrbKind.Fire) == 4 && Session().Orbs.IsActivated(OrbKind.Fire), "Fire Guide grants four and activates");
            await Start(fire);
            Check(Session().Orbs.Value(OrbKind.Fire) == 4, "Fire Guide once per combat");
            await fire.BeforeCombatStart();
            await Start(fire);
            Check(Session().Orbs.Value(OrbKind.Fire) == 8, "Fire Guide combat reset");

            await Reset();
            await Add<LibrarianUncommonPlaceholderOne>();
            await Gain(OrbKind.Fire, 5);
            var enemy = player.Creature.CombatState!.HittableEnemies.First();
            int hp = enemy.CurrentHp;
            await Settle(OrbKind.Fire);
            Check(enemy.CurrentHp == hp - 5 && Session().Orbs.Value(OrbKind.Fire) == 7, "Ember strengthens after damage");
            await Settle(OrbKind.Fire);
            Check(enemy.CurrentHp == hp - 12 && Session().Orbs.Value(OrbKind.Fire) == 9, "Ember each immediate settlement");
            await Settle(OrbKind.Tide);
            Check(Session().Orbs.Value(OrbKind.Fire) == 9, "Ember ignores other orbs");
            await Natural();
            Check(Session().Orbs.Value(OrbKind.Fire) == 11, "Ember natural settlement");

            await Reset();
            var dust = await Add<LibrarianUncommonPlaceholderTwo>();
            await dust.BeforeCombatStart();
            var card = player.Creature.CombatState!.CreateCard<WearyIncantation>(player);
            card.AddKeyword(CardKeyword.Exhaust);
            var ordinary = player.Creature.CombatState.CreateCard<WearyIncantation>(player);
            await CardPileCmd.Add(card, PileType.Hand);
            await CardPileCmd.Add(ordinary, PileType.Hand);
            Check(card.CombatState is not null && card.Pile?.Type == PileType.Hand, "Dust fixture uses actual combat hand");
            Check(card.EnergyCost.GetWithModifiers(CostModifiers.All) == 0, "Dust temporary Exhaust free preview");
            Check(card.EnergyCost.GetWithModifiers(CostModifiers.All) == 0 && ordinary.EnergyCost.GetWithModifiers(CostModifiers.All) == 2, "Dust repeated preview no consumption, ordinary unchanged");
            int energy = player.PlayerCombatState!.Energy;
            var spent = await card.SpendResources();
            Check(spent.Item1 == 0 && player.PlayerCombatState.Energy == energy, "Dust actual resource spending zero");
            Check(!dust.Qualifies(card), "Dust consumed by resource commit");
            await CardCmd.AutoPlay(context, card, player.Creature.CombatState.HittableEnemies.First(), skipCardPileVisuals: true);
            var second = player.Creature.CombatState.CreateCard<WearyIncantation>(player);
            second.AddKeyword(CardKeyword.Exhaust);
            await CardPileCmd.Add(second, PileType.Hand);
            Check(second.EnergyCost.GetWithModifiers(CostModifiers.All) == 2, "Dust second Exhaust normal cost");
            await dust.BeforeCombatStart();
            Check(second.EnergyCost.GetWithModifiers(CostModifiers.All) == 0, "Dust reset next combat");
            await CardCmd.AutoPlay(context, second, player.Creature.CombatState.HittableEnemies.First(), skipCardPileVisuals: true);
            Check(!dust.Qualifies(second), "Dust automatic first Exhaust also consumes");

            await Reset();
            dust = await Add<LibrarianUncommonPlaceholderTwo>();
            await dust.BeforeCombatStart();
            await Gain(OrbKind.Fire, 3);
            var x = player.Creature.CombatState!.CreateCard<MultipleEruption>(player);
            await CardPileCmd.Add(x, PileType.Hand);
            energy = player.PlayerCombatState!.Energy;
            spent = await x.SpendResources();
            Check(spent.Item1 == 0 && player.PlayerCombatState.Energy == energy && x.EnergyCost.CapturedXValue == energy, "Dust X spends zero but retains captured X");
            hp = player.Creature.CombatState.HittableEnemies.First().CurrentHp;
            await CardCmd.AutoPlay(context, x, player.Creature.CombatState.HittableEnemies.First(), skipXCapture: true, skipCardPileVisuals: true);
            Check(player.Creature.CombatState.HittableEnemies.First().CurrentHp == hp - 3 * energy, "Dust X still resolves full repetitions");
            var xHistory = CombatManager.Instance.History.CardPlaysFinished.Last(e => e.CardPlay.Card == x).CardPlay.Resources;
            Check(xHistory.EnergySpent == 0 && xHistory.EnergyValue == energy, "Dust X native history zero spent/full value");

            await Reset();
            var unstable = await Add<LibrarianRarePlaceholderOne>();
            await Gain(OrbKind.Tide, 2); await Gain(OrbKind.Growth, 3);
            await unstable.BeforeOrbSettlements(Session(), context);
            Check(Session().Orbs.IsActivated(OrbKind.Fire) && Session().Orbs.Foreground == OrbKind.Fire, "Unstable only extinguished orb before natural");
            await Natural();
            Check(!Session().Orbs.IsActivated(OrbKind.Fire), "Unstable activated orb participates in natural settlement");
            Session().Orbs.Lock(OrbKind.Fire);
            await unstable.BeforeOrbSettlements(Session(), context);
            Check(!Session().Orbs.IsActivated(OrbKind.Fire), "Unstable excludes locked candidates");

            await Reset();
            await Add<LibrarianRarePlaceholderTwo>();
            await Gain(OrbKind.Fire, 4); await Gain(OrbKind.Tide, 8); await Gain(OrbKind.Growth, 11);
            Check(Session().Orbs.Value(OrbKind.Tide) == 8, "Vines ignores card Gain Growth");
            await Settle(OrbKind.Growth);
            Check(Session().Orbs.Value(OrbKind.Fire) == 15 && Session().Orbs.Value(OrbKind.Tide) == 13, "Vines captures pre-growth high target and floors half");
            await Settle(OrbKind.Growth, true);
            Check(Session().Orbs.Value(OrbKind.Tide) == 18 && Session().Orbs.Value(OrbKind.Fire) == 17, "Vines halves effective background amount");
            Session().Orbs.Lock(OrbKind.Tide);
            await Settle(OrbKind.Growth);
            Check(Session().Orbs.Value(OrbKind.Tide) == 23 && Session().Orbs.Value(OrbKind.Fire) == 28, "Vines strengthens locked high orb");

            await Reset();
            var memory = await Add<LibrarianRarePlaceholderThree>();
            await Gain(OrbKind.Tide, 2); await Settle(OrbKind.Tide); await PutOut(OrbKind.Tide);
            await Start(memory);
            Check(!Session().Orbs.IsActivated(OrbKind.Tide), "Memory excludes current turn");
            player.PlayerCombatState!.IncrementTurnNumber();
            await Settle(OrbKind.Fire);
            await Start(memory);
            Check(Session().Orbs.IsActivated(OrbKind.Tide), "Memory remembers preceding turn despite earlier start effect settlement");
            await Settle(OrbKind.Tide);
            await PutOut(OrbKind.Tide);
            player.PlayerCombatState.IncrementTurnNumber();
            await Start(memory);
            Check(Session().Orbs.IsActivated(OrbKind.Tide), "Memory retains earlier turns without new settlements");
            await PutOut(OrbKind.Tide); Session().Orbs.Lock(OrbKind.Tide);
            player.PlayerCombatState.IncrementTurnNumber();
            await Start(memory);
            Check(!Session().Orbs.IsActivated(OrbKind.Tide) && !Session().Orbs.IsActivated(OrbKind.Fire), "Memory locked target no fallback");
            await freshFight();
            await Start(memory);
            Check(Session().PreviousTurnsLastSettledOrb is null && Session().LastSettledOrb is null, "Memory history cleared in next fight");

            await Reset();
            var badge = await Add<LibrarianShopPlaceholder>();
            await Gain(OrbKind.Fire, 5); await Gain(OrbKind.Tide, 3);
            await badge.BeforeOrbSettlements(Session(), context);
            Check(Session().Orbs.SettlementsThisCombat == 0, "Badge requires all active");
            await Gain(OrbKind.Growth, 2);
            await badge.BeforeOrbSettlements(Session(), context);
            Check(Session().Orbs.SettlementsThisCombat == 3 && Session().Orbs.Snapshot().Orbs.All(o => o.IsActivated), "Badge extra all before natural preserves activation");
            await Natural();
            Check(Session().Orbs.SettlementsThisCombat == 4 && !Session().Orbs.IsActivated(OrbKind.Growth), "Badge then ordinary natural once");

            await Reset();
            unstable = await Add<LibrarianRarePlaceholderOne>();
            await Gain(OrbKind.Tide, 2); await Gain(OrbKind.Growth, 3);
            await ModelDb.Singleton<LibrarianCombatHooks>().BeforeSideTurnEnd(context, CombatSide.Player, new[] { player.Creature });
            Check(Session().Orbs.SettlementsThisCombat == 3 && Session().Orbs.Snapshot().Orbs.All(o => !o.IsActivated), "Native turn hook invokes Unstable before all three natural settlements");

            await Reset();
            badge = await Add<LibrarianShopPlaceholder>();
            await Gain(OrbKind.Fire, 4); await Gain(OrbKind.Tide, 3); await Gain(OrbKind.Growth, 2);
            await ModelDb.Singleton<LibrarianCombatHooks>().BeforeSideTurnEnd(context, CombatSide.Player, new[] { player.Creature });
            Check(Session().Orbs.SettlementsThisCombat == 6 && Session().Orbs.Snapshot().Orbs.All(o => !o.IsActivated), "Native turn hook invokes Badge extra three before natural three");

            await Reset();
            await Add<LibrarianRarePlaceholderOne>(); await Add<LibrarianShopPlaceholder>();
            await Gain(OrbKind.Tide, 3); await Gain(OrbKind.Growth, 2);
            await ModelDb.Singleton<LibrarianCombatHooks>().BeforeSideTurnEnd(context, CombatSide.Player, new[] { player.Creature });
            Check(Session().Orbs.SettlementsThisCombat == 6, "Native relic inventory order Unstable can enable Badge");

            await Reset();
            var restored = await Add<RestoredSpellScroll>();
            Session().Orbs.Lock(OrbKind.Tide);
            await Start(restored);
            Check(Session().Orbs.Snapshot().Orbs.All(o => o.Value == 1), "Restored strengthens all including locked");
            Check(Session().Orbs.IsActivated(Session().Orbs.Foreground), "Restored activates foreground");
            Check(Session().Orbs.LockedTurns(OrbKind.Tide) == OrbView.PermanentLock, "Restored preserves locks");
            Check(ModelDb.Relic<TatteredSpellScroll>().HoverTips.OfType<MegaCrit.Sts2.Core.HoverTips.HoverTip>().Any(t => t.Id.Contains("LIBRARIAN_STRENGTHEN")), "Starter Strengthen hover tip available");
            MainFile.Logger.Info($"RELIC035_RUNTIME_AUDIT_PASS checks={checks}");
        }
        finally { await Reset(); }
    }
}



