using Godot;
using MegaCrit.Sts2.Core.Nodes;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.NativeBatch;
using Librarian.LibrarianCode.Cards.OrbBasics;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Cards.PowerCards;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.Mechanics;

/// <summary>Isolated v036 runtime regression; extra actors exist in combat only, never in saved run.Players.
/// This verifies native model/hook recipient semantics, not separate-client networking.</summary>
internal static class DevelopmentRevision036Audit
{
    internal static async Task Run(Player player, Func<Task> freshFight)
    {
        var ctx = new ThrowingPlayerChoiceContext();
        int checks = 0;
        var extras = new List<Player>();
        LibrarianSession S() => LibrarianRuntime.Get(player);
        Creature Enemy() => player.Creature.CombatState!.HittableEnemies.First();
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("036: " + label);
            checks++; MainFile.Logger.Info("REV036_CHECK_PASS " + label);
        }
        async Task Reset()
        {
            foreach (var extra in extras)
                if (extra.Creature.CombatState is CombatState combat) combat.RemoveCreature(extra.Creature);
            extras.Clear();
            await freshFight();
            // The shared fixture's first-hand readiness can precede the remaining opening draw.
            // Native Play phase begins only after all start/automatic setup completes.
            for (int frame = 0; player.PlayerCombatState?.Phase != PlayerTurnPhase.Play; frame++)
            {
                if (frame >= 900) throw new TimeoutException("036 fixture did not reach native Play phase.");
                await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
        async Task<T> Play<T>(bool upgraded = false) where T : CardModel
        {
            var card = player.Creature.CombatState!.CreateCard<T>(player);
            if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
            await CardCmd.AutoPlay(ctx, card, Enemy(), skipCardPileVisuals: true);
            return card;
        }
        Task Gain(OrbKind kind, int amount) => LibrarianRuntime.Dispatch(S(), ctx, S().Orbs.Gain(kind, amount));
        async Task Settle(OrbKind kind)
        {
            await S().Orbs.SettleImmediatelyAsync(OrbSelector.Named(kind, OrbScope.All), 1,
                r => LibrarianRuntime.Settle(S(), ctx, r), "036-audit");
        }
        Player Extra<T>(ulong id) where T : CharacterModel
        {
            var actor = Player.CreateForNewRun<T>(UnlockState.all, id);
            actor.RunState = player.RunState;
            actor.ResetCombatState();
            ((CombatState)player.Creature.CombatState!).AddPlayer(actor);
            extras.Add(actor);
            return actor;
        }
        async Task CardEvent(Player actor, CardModel card, int index = 0)
        {
            await Hook.AfterCardPlayed(player.Creature.CombatState!, ctx, new CardPlay
            {
                Player = actor, Card = card, Target = Enemy(), ResultPile = PileType.Discard,
                IsAutoPlay = true, PlayIndex = index, PlayCount = 2,
                Resources = new() { EnergySpent = 0, EnergyValue = 0, StarsSpent = 0, StarValue = 0 }
            });
        }
        try
        {
            await Reset();
            await Gain(OrbKind.Fire, 20);
            await PowerCmd.Apply<StrengthPower>(ctx, player.Creature, 7, player.Creature, null);
            await PowerCmd.Apply<WeakPower>(ctx, player.Creature, 2, player.Creature, null);
            await PowerCmd.Apply<VulnerablePower>(ctx, Enemy(), 2, player.Creature, null);
            await PowerCmd.Apply<ThornsPower>(ctx, Enemy(), 10, Enemy(), null);
            int hp = Enemy().CurrentHp, ownHp = player.Creature.CurrentHp;
            await CreatureCmd.GainBlock(Enemy(), 7, ValueProp.Unpowered, null);
            await Settle(OrbKind.Fire);
            Check(Enemy().CurrentHp == hp - 13 && Enemy().Block == 0, "Fire ignores Strength Weak Vulnerable but respects seven Block");
            Check(player.Creature.CurrentHp == ownHp, "Fire does not trigger Thorns");
            await PowerCmd.Apply<IntangiblePower>(ctx, Enemy(), 1, Enemy(), null);
            hp = Enemy().CurrentHp;
            await Settle(OrbKind.Fire);
            Check(Enemy().CurrentHp == hp - 1, "Fire retains native Intangible cap");

            await Reset();
            await PowerCmd.Apply<FrailPower>(ctx, player.Creature, 2, player.Creature, null);
            await PowerCmd.Apply<DexterityPower>(ctx, player.Creature, 7, player.Creature, null);
            await Gain(OrbKind.Tide, 11); await Settle(OrbKind.Tide);
            Check(player.Creature.Block == 11 && S().Waves.Amount == 11, "Tide ignores Frail and Dexterity, keeps eleven Waves");

            foreach (bool upgrade in new[] { false, true })
            {
                await Reset();
                var sprout = await Play<SproutingSeed>(upgrade);
                Check(S().Orbs.LockedTurns(OrbKind.Fire) == (upgrade ? 1 : 2), "Sprout Fire lock " + upgrade);
                Check(player.Creature.Block == (upgrade ? 10 : 7) && S().Orbs.Value(OrbKind.Growth) == (upgrade ? 8 : 6), "Sprout Block and Growth " + upgrade);
                await Reset();
                // Ensure space and a nonempty draw pile, independent of the opening hand.
                foreach (var handCard in player.PlayerCombatState!.Hand.Cards.ToArray()) await CardPileCmd.Add(handCard, PileType.Discard);
                var drawFixtures = new List<CardModel>();
                for (int i = 0; i < 3; i++)
                {
                    var next = player.Creature.CombatState!.CreateCard<WearyIncantation>(player);
                    drawFixtures.Add(next);
                    await CardPileCmd.Add(next, PileType.Draw, CardPilePosition.Top);
                }
                int beforeDraw = player.PlayerCombatState.DrawPile.Cards.Count;
                int historyBefore = CombatManager.Instance.History.Entries.OfType<MegaCrit.Sts2.Core.Combat.History.Entries.CardDrawnEntry>().Count();
                var evaporation = await Play<Evaporation>(upgrade);
                int drawDelta = beforeDraw - player.PlayerCombatState.DrawPile.Cards.Count;
                int fixtureCardsInHand = drawFixtures.Count(c => c.Pile?.Type == PileType.Hand);
                int historyDraws = CombatManager.Instance.History.Entries.OfType<MegaCrit.Sts2.Core.Combat.History.Entries.CardDrawnEntry>().Count() - historyBefore;
                MainFile.Logger.Info($"REV036_DRAW_DETAILS upgrade={upgrade} phase={player.PlayerCombatState.Phase} requested={evaporation.DynamicVars.Cards.IntValue} drawDelta={drawDelta} fixturesInHand={fixtureCardsInHand} hand={player.PlayerCombatState.Hand.Cards.Count} historyDraws={historyDraws} inProgress={CombatManager.Instance.IsInProgress} ending={CombatManager.Instance.IsOverOrEnding} deferred={S().ResolvingEndTurn}");
                Check(drawDelta == (upgrade ? 2 : 1) && fixtureCardsInHand == (upgrade ? 2 : 1), $"Evaporation exact draw pile transfer upgrade={upgrade} drawDelta={drawDelta} fixturesInHand={fixtureCardsInHand} historyDraws={historyDraws}");
                Check(S().Orbs.LockedTurns(OrbKind.Tide) == 1 && S().Orbs.Value(OrbKind.Fire) == (upgrade ? 6 : 4), "Evaporation lock and Fire " + upgrade);
                await Reset();
                var read = await Play<ReadWidely>(upgrade);
                Check(S().Orbs.Value(OrbKind.Fire) == 2 && S().Orbs.Value(OrbKind.Tide) == 3 && S().Orbs.Value(OrbKind.Growth) == 4, "Read Widely fixed gains " + upgrade);
                Check(read.Keywords.Contains(CardKeyword.Exhaust) && read.Keywords.Contains(CardKeyword.Innate) == upgrade, "Read Widely Exhaust Innate " + upgrade);
                await Reset();
                await Gain(OrbKind.Fire, 2);
                var over = await Play<Overfishing>(upgrade);
                var power = player.Creature.GetPower<OverfishingPower>()!;
                Check(over.Rarity == CardRarity.Rare && power.DisplayAmount == (upgrade ? 4 : 3) && S().Orbs.Value(OrbKind.Fire) == 2, "Overfishing future-only Rare " + upgrade);
                int turns = upgrade ? 4 : 3;
                for (int turn = 1; turn <= turns + 1; turn++)
                {
                    player.PlayerCombatState!.IncrementTurnNumber();
                    await power.BeforeHandDraw(player, ctx, player.Creature.CombatState!);
                    Check(S().Orbs.Value(OrbKind.Fire) == 2 * (1 << Math.Min(turn, turns)), "Overfishing doubling limit " + upgrade + ":" + turn);
                }
                Check(power.DisplayAmount == 0, "Overfishing remaining zero " + upgrade);
            }
            var pool = ModelDb.CardPool<LibrarianCardPool>().AllCards.ToArray();
            Check(!pool.Any(c => c.Id.Entry is "LIBRARIAN-FLAME_BURST" or "LIBRARIAN-SEAL_AWAY"), "Removed cards absent from current pool");

            await Reset();
            var ally = Extra<LibrarianCharacter>(903601);
            var nonLibrarian = Extra<Ironclad>(903602);
            Check(!player.RunState.Players.Contains(ally) && player.Creature.CombatState!.Players.Contains(ally), "Extra actors only in combat fixture");
            await Play<MultiplayerPlaceholderA>();
            var attack = player.Creature.CombatState!.CreateCard<WearyIncantation>(ally);
            await CardPileCmd.Add(attack, PileType.Hand);
            await CardEvent(ally, attack);
            Check(S().Orbs.Value(OrbKind.Fire) == 1 && !S().Orbs.IsActivated(OrbKind.Fire), "Crowd teammate Attack strengthens without activation");
            await CardEvent(ally, attack, 1);
            Check(S().Orbs.Value(OrbKind.Fire) == 2, "Crowd replay counts separately");
            var skill = player.Creature.CombatState.CreateCard<ReadWidely>(ally);
            await CardEvent(ally, skill);
            Check(S().Orbs.Value(OrbKind.Fire) == 2, "Crowd ignores skills");
            await Play<MultiplayerPlaceholderA>(true);
            await CardEvent(ally, attack);
            Check(S().Orbs.Value(OrbKind.Fire) == 5, "Crowd base and upgrade stack to three");
            await PowerCmd.Remove(player.Creature.GetPower<CrowdKindlingPower>()!);
            // Borrowed Fire works despite recipient locks and does not copy orb state.
            await CreatureCmd.GainBlock(nonLibrarian.Creature, 7, ValueProp.Unpowered, null);
            await Gain(OrbKind.Fire, 7);
            var allySession = LibrarianRuntime.Get(ally);
            allySession.Orbs.Strengthen(OrbKind.Fire, 31, OrbScope.All);
            allySession.Orbs.Lock(OrbKind.Fire);
            hp = Enemy().CurrentHp;
            int fireValue = S().Orbs.Value(OrbKind.Fire);
            var playedBind = await Play<MultiplayerPlaceholderB>();
            Check(playedBind.Pile?.Type == PileType.Exhaust, "Binding actual card play exhausts");
            Check(Enemy().CurrentHp == hp - fireValue * 3, "Bind Fire settles once per three living players");
            Check(allySession.Orbs.Value(OrbKind.Fire) == 31 && allySession.Orbs.IsLocked(OrbKind.Fire), "Bind Fire preserves recipient locked orb value");
            int blockAlly = ally.Creature.Block, blockOther = nonLibrarian.Creature.Block;
            await Gain(OrbKind.Tide, 20);
            playedBind = await Play<MultiplayerPlaceholderB>(true);
            Check(playedBind.Pile?.Type == PileType.Exhaust, "Binding upgraded actual play exhausts");
            Check(ally.Creature.Block == blockAlly + 20 && nonLibrarian.Creature.Block == blockOther + 20, "Bind Tide grants twenty Block preserving ordinary Block across character types");
            Check(LibrarianRuntime.Get(nonLibrarian).Waves.Amount == 20, "Bind Tide non-Librarian Waves ledger");
            await Gain(OrbKind.Growth, 6);
            int allyLow = allySession.Orbs.Value(OrbKind.Tide);
            await LibrarianRuntime.BindSharedAsync(ctx, player, ModelDb.Card<MultiplayerPlaceholderB>());
            Check(allySession.Orbs.Value(OrbKind.Tide) == allyLow + 6, "Bind Growth strengthens recipient other orb from source snapshot");
            Check(LibrarianRuntime.Get(nonLibrarian).Orbs.Snapshot().Orbs.All(o => o.Value == 0), "Bind Growth skips non-Librarian orb state");
            Check(S().Orbs.SettlementsThisCombat == 3, "Bind source settles once per cast with no forwarded recursion");
            Check(allySession.Orbs.SettlementsThisCombat == 3 && LibrarianRuntime.Get(nonLibrarian).Orbs.SettlementsThisCombat == 2, "Bind recipient counters record effects once and skip incompatible Growth");
            Check(!allySession.Orbs.IsActivated(OrbKind.Fire) && !allySession.Orbs.IsActivated(OrbKind.Tide) && !allySession.Orbs.IsActivated(OrbKind.Growth), "Bind never activates recipient orbs");
            int beforeShared = allySession.Orbs.SettlementsThisCombat;
            S().Orbs.Lock(S().Orbs.Foreground);
            await LibrarianRuntime.BindSharedAsync(ctx, player, ModelDb.Card<MultiplayerPlaceholderB>());
            Check(S().Orbs.SettlementsThisCombat == 3 && allySession.Orbs.SettlementsThisCombat == beforeShared, "Bind locked source prevents own and copied settlement");
            await ModelDb.Singleton<LibrarianCombatHooks>().BeforeSideTurnEnd(ctx, CombatSide.Player, new[] { nonLibrarian.Creature });
            Check(LibrarianRuntime.Get(nonLibrarian).Waves.Amount == 10 && nonLibrarian.Creature.Block == blockOther + 20, "Borrowed Tide first end decays twenty to ten with no double payout");
            var bind = (MultiplayerPlaceholderB)ModelDb.Card<MultiplayerPlaceholderB>().ToMutable();
            Check(bind.EnergyCost.Canonical == 1 && bind.Keywords.Contains(CardKeyword.Exhaust), "Binding costs one and Exhausts");
            bind.UpgradeInternal(); bind.FinalizeUpgradeInternal();
            Check(bind.EnergyCost.GetWithModifiers(CostModifiers.None) == 0 && bind.Keywords.Contains(CardKeyword.Exhaust), "Binding upgrade costs zero and retains Exhaust");
            MainFile.Logger.Info($"REV036_RUNTIME_AUDIT_PASS checks={checks} multiplayer=single-process-model-hooks-not-multiclient");
        }
        finally { await Reset(); }
    }
}



