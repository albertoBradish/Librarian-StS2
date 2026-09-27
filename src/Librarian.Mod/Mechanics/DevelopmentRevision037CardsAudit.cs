using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards.OrbAdvancedBasics;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Cards.Stateful;
using Librarian.LibrarianCode.Relics;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Random;

namespace Librarian.Mechanics;

/// <summary>Opt-in isolated card regression. Coordinator supplies fresh fights; no production hook.</summary>
internal static class DevelopmentRevision037CardsAudit
{
    internal static async Task Run(Player player, Func<Task> freshFight)
    {
        var ctx = new ThrowingPlayerChoiceContext();
        var added = new List<RelicModel>();
        int checks = 0;
        LibrarianSession S() => LibrarianRuntime.Get(player);
        Creature Enemy() => player.Creature.CombatState!.HittableEnemies.First();
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("037 cards: " + label);
            checks++; MainFile.Logger.Info("REV037_CARD_CHECK_PASS " + label);
        }
        async Task Reset()
        {
            foreach (var relic in added.ToArray()) await RelicCmd.Remove(relic);
            added.Clear();
            await freshFight();
            for (int frame = 0; player.PlayerCombatState?.Phase != PlayerTurnPhase.Play; frame++)
            {
                if (frame >= 900) throw new TimeoutException("037 fixture did not reach native Play phase.");
                await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
        CardModel Create<T>(bool upgraded) where T : CardModel
        {
            var card = player.Creature.CombatState!.CreateCard<T>(player);
            if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
            return card;
        }
        Task Play(CardModel card) => CardCmd.AutoPlay(ctx, card, Enemy(), skipCardPileVisuals: true);
        Task Gain(OrbKind kind, int amount) => LibrarianRuntime.Dispatch(S(), ctx, S().Orbs.Gain(kind, amount));
        Task Extinguish(OrbKind kind) => LibrarianRuntime.Dispatch(S(), ctx, S().Orbs.Extinguish(kind, OrbScope.All));
        void SelectNext(OrbKind kind)
        {
            int index = S().Orbs.Positions.ToList().IndexOf(kind);
            for (ulong seed = 0; seed < 1000; seed++)
            {
                var rng = new Rng(seed);
                var saved = rng.ToSerializable();
                if (rng.NextInt(3) != index) continue;
                player.RunState.Rng.CombatTargets.LoadFromSerializable(saved);
                return;
            }
            throw new InvalidOperationException("Could not arrange deterministic audit orb selection.");
        }
        try
        {
            foreach (bool upgraded in new[] { false, true })
            {
                foreach (int x in new[] { 0, 1, 3 })
                {
                    await Reset();
                    var second = await CreatureCmd.Add<Tunneler>(player.Creature.CombatState!);
                    await CreatureCmd.SetMaxAndCurrentHp(second, 10000);
                    foreach (var power in second.Powers.ToArray()) await PowerCmd.Remove(power);
                    second.LoseBlockInternal(second.Block);
                    var enemies = player.Creature.CombatState!.HittableEnemies.ToArray();
                    var hp = enemies.Select(c => c.CurrentHp).ToArray();
                    var card = Create<EarthCollapse>(upgraded);
                    card.EnergyCost.CapturedXValue = x;
                    await CardCmd.AutoPlay(ctx, card, null, skipXCapture: true, skipCardPileVisuals: true);
                    Check(card.EnergyCost.CostsX && card.TargetType == TargetType.AllEnemies, "Earth Collapse native X/all targets");
                    Check(S().Orbs.LockedTurns(OrbKind.Growth) == 2, $"Earth Collapse lock2 X={x} upgrade={upgraded}");
                    Check(enemies.Length >= 2 && enemies.Select((c,i) => hp[i] - c.CurrentHp).All(n => n == x * (upgraded ? 11 : 8)),
                        $"Earth Collapse hits every enemy X={x} upgrade={upgraded}");
                    Check(enemies.All(c => c.GetPower<VulnerablePower>() is null), "Earth Collapse no legacy Vulnerable");
                }
                await Reset();
                var values = new (CardModel Card, string Var, int Expected)[] {
                    (Create<OutOfContext>(upgraded), "Cards", upgraded ? 4 : 3),
                    (Create<Afforestation>(upgraded), "Tide", upgraded ? 7 : 5),
                    (Create<Afforestation>(upgraded), "Growth", upgraded ? 6 : 4),
                    (Create<SeedburialStrike>(upgraded), "Growth", upgraded ? 15 : 11),
                    (Create<SeedburialStrike>(upgraded), "Damage", upgraded ? 20 : 15),
                    (Create<SteamBlast>(upgraded), "Damage", upgraded ? 23 : 18),
                    (Create<BurnRoots>(upgraded), "Fire", upgraded ? 5 : 3),
                    (Create<ResidualWarmth>(upgraded), "Fire", upgraded ? 11 : 7),
                    (Create<ResidualWarmth>(upgraded), "Block", upgraded ? 11 : 8)
                };
                foreach (var v in values) Check(v.Card.DynamicVars[v.Var].IntValue == v.Expected, $"{v.Card.Id} {v.Var}={v.Expected}");

                await Reset();
                await Gain(OrbKind.Growth, 9); await Gain(OrbKind.Tide, 2); await Gain(OrbKind.Fire, 10);
                await Extinguish(OrbKind.Growth); await Extinguish(OrbKind.Tide);
                var order = S().Orbs.Positions.ToArray();
                await Play(Create<CropRotation>(upgraded));
                int amount = upgraded ? 8 : 5;
                Check(S().Orbs.Value(OrbKind.Fire) == 7 && S().Orbs.Value(OrbKind.Growth) == 9 + amount
                    && S().Orbs.Value(OrbKind.Tide) == 2 + amount, "Crop Rotation strengthens both unequal backgrounds");
                Check(order.SequenceEqual(S().Orbs.Positions) && S().Orbs.IsActivated(OrbKind.Tide) && S().Orbs.IsActivated(OrbKind.Growth),
                    "Crop Rotation activates both without moving");

                foreach (bool locked in new[] { false, true })
                {
                    await Reset();
                    await Gain(OrbKind.Fire, 7); await Extinguish(OrbKind.Fire);
                    if (locked) await LibrarianRuntime.Dispatch(S(), ctx, S().Orbs.Lock(OrbKind.Fire));
                    SelectNext(OrbKind.Fire);
                    var card = Create<NourishingLife>(upgraded);
                    int hp = Enemy().CurrentHp;
                    await Play(card);
                    int initial = upgraded ? 5 : 3;
                    Check(hp - Enemy().CurrentHp == initial * 2 + (locked ? 0 : 7), "Nourishing Life initial two hits then Fire settlement");
                    Check(S().Orbs.SettlementsThisTurn == (locked ? 0 : 1) && S().Orbs.Value(OrbKind.Fire) == 0,
                        "Nourishing Life locked skips settlement but still drains; extinguished can settle");
                    Check(card.DynamicVars.Damage.IntValue == initial + 7, "Nourishing Life absorbs once into future per-hit damage");
                    if (upgraded)
                    {
                        card.DowngradeInternal();
                        Check(card.DynamicVars.Damage.IntValue == 10, "Nourishing Life downgrade preserves absorbed value");
                    }
                }
            }
            await Reset();
            await Gain(OrbKind.Growth, 9); await Gain(OrbKind.Tide, 2); await Gain(OrbKind.Fire, 10);
            await LibrarianRuntime.Dispatch(S(), ctx, S().Orbs.Lock(OrbKind.Growth));
            var beforeOrder = S().Orbs.Positions.ToArray();
            await Play(Create<CropRotation>(false));
            Check(S().Orbs.Value(OrbKind.Growth) == 14 && !S().Orbs.IsActivated(OrbKind.Growth)
                && S().Orbs.IsActivated(OrbKind.Tide) && beforeOrder.SequenceEqual(S().Orbs.Positions), "Crop Rotation locked background strengthens but cannot activate");

            await Reset();
            added.Add(await RelicCmd.Obtain<LibrarianUncommonPlaceholderOne>(player));
            await Gain(OrbKind.Fire, 7); SelectNext(OrbKind.Fire);
            var absorbing = Create<NourishingLife>(false);
            int enemyHp = Enemy().CurrentHp;
            await Play(absorbing);
            Check(enemyHp - Enemy().CurrentHp == 13 && S().Orbs.Value(OrbKind.Fire) == 0 && absorbing.DynamicVars.Damage.IntValue == 12,
                "Nourishing Life settles7, relic strengthens2, then absorbs9");
            await Reset();
            MainFile.Logger.Info($"REV037_CARDS_AUDIT_PASS checks={checks} native_autoplay=True multienemy=True");
        }
        finally
        {
            foreach (var relic in added.ToArray()) await RelicCmd.Remove(relic);
        }
    }
}
