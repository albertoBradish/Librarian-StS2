using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Cards.PowerCards;
using Librarian.LibrarianCode.Cards.Stateful;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace Librarian.Mechanics;

/// <summary>Opt-in beta2 native play, owner-turn, event-order and deck save checks.</summary>
internal static class DevelopmentRevision120Beta2Audit
{
    private static readonly string[] Changed = ["BLAZING_CHAPTER", "OVERFISHING", "SPACETIME_TWIST", "TIDAL_MARK", "RIDGE_WARD"];

    internal static async Task Run(Player player, Func<Task> freshFight)
    {
        int checks = 0, realTurns = 0;
        var context = new ThrowingPlayerChoiceContext();
        string output = System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT")
            ?? throw new InvalidOperationException("LIBRARIAN_AUDIT_OUTPUT is required");
        if (!Path.IsPathFullyQualified(output)) throw new InvalidOperationException("Absolute beta2 audit output required");
        Directory.CreateDirectory(output);
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("120 beta2: " + label);
            checks++; MainFile.Logger.Info("CARD120_BETA2_CHECK_PASS " + label);
        }
        LibrarianSession S() => LibrarianRuntime.Get(player);
        Creature Enemy() => player.Creature.CombatState!.HittableEnemies.First();
        async Task Reset()
        {
            await freshFight();
            for (int i = 0; player.PlayerCombatState?.Phase != PlayerTurnPhase.Play; i++)
            {
                if (i > 1200) throw new TimeoutException("120 beta2 native Play phase");
                await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            foreach (var relic in player.Relics.ToArray()) await RelicCmd.Remove(relic);
        }
        T Card<T>(bool up = false) where T : CardModel
        {
            var card = player.Creature.CombatState!.CreateCard<T>(player);
            if (up) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
            return card;
        }
        Task Play(CardModel card) => CardCmd.AutoPlay(context, card, null, skipCardPileVisuals: true);
        Task Dispatch(OrbOperationResult operation) => LibrarianRuntime.Dispatch(S(), context, operation);
        async Task Turn()
        {
            int round = player.Creature.CombatState!.RoundNumber;
            CombatManager.Instance.SetReadyToEndTurn(player, false);
            for (int i = 0; player.Creature.CombatState!.RoundNumber == round || player.PlayerCombatState!.Phase != PlayerTurnPhase.Play; i++)
            {
                if (i > 3600) throw new TimeoutException("120 beta2 native end turn");
                await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            realTurns++;
        }
        async Task FireFixture()
        {
            await Dispatch(S().Orbs.Gain(OrbKind.Tide, 5));
            await Dispatch(S().Orbs.Gain(OrbKind.Growth, 7));
            await Dispatch(S().Orbs.Extinguish(OrbKind.Tide, OrbScope.All));
            await Dispatch(S().Orbs.Extinguish(OrbKind.Growth, OrbScope.All));
            await Dispatch(S().Orbs.Gain(OrbKind.Fire, 3));
        }
        async Task InactiveFixture()
        {
            await Dispatch(S().Orbs.Gain(OrbKind.Tide, 4));
            await Dispatch(S().Orbs.Gain(OrbKind.Growth, 6));
            await Dispatch(S().Orbs.Gain(OrbKind.Fire, 2));
            foreach (var kind in S().Orbs.Positions.ToArray()) await Dispatch(S().Orbs.Extinguish(kind, OrbScope.All));
        }
        var events = new List<OrbEvent>();
        var eventEvidence = new List<object>();
        var powerDescriptions = new List<object>();
        void BlazingText(BlazingChapterPower power, bool pending, string label)
        {
            string previous = LibrarianLanguage.Selected;
            try
            {
                foreach (string language in new[] { "zhs", "eng" })
                {
                    LibrarianLanguage.Select(language);
                    string description = power.HoverTips.OfType<HoverTip>().First().Description;
                    Check(!description.Contains('{') && !description.Contains('}'), "Blazing current power formatted " + language + " " + label);
                    bool waiting = language == "zhs" ? description.Contains("个回合后") : description.Contains("After");
                    Check(waiting == pending, "Blazing current wait sentence only while pending " + language + " " + label);
                    if (pending)
                        Check(power.PendingLockDelays.All(d => description.Contains(d.ToString())), "Blazing current wait numbers " + language + " " + label);
                    powerDescriptions.Add(new { label, language, description, delays = power.PendingLockDelays });
                }
            }
            finally { LibrarianLanguage.Select(previous); }
        }
        Task Observe(LibrarianSession session, PlayerChoiceContext _, OrbEvent change)
        {
            if (session.Player == player) events.Add(change);
            return Task.CompletedTask;
        }
        LibrarianRuntime.OrbChanged += Observe;
        string originalLanguage = LibrarianLanguage.Selected;
        try
        {
            await Reset();
            var models = ModelDb.CardPool<LibrarianCardPool>().AllCards.ToArray();
            Check(models.Length == 91, "91 active models");
            var formatted = new List<object>();
            foreach (string language in new[] { "zhs", "eng" })
            {
                LibrarianLanguage.Select(language);
                foreach (var model in models)
                {
                    var card = model.ToMutable(); card.Owner = player;
                    foreach (bool up in new[] { false, true })
                    {
                        if (up) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
                        string description = card.GetDescriptionForPile(PileType.None);
                        Check(!description.Contains('{') && !description.Contains('}'), "formatted " + language + " " + card.Id + " " + up);
                        Check(Regex.Matches(description, @"\[gold\]").Count == Regex.Matches(description, @"\[/gold\]").Count,
                            "balanced gold " + language + " " + card.Id + " " + up);
                        var tips = card.HoverTips.ToArray();
                        Check(IHoverTip.RemoveDupes(tips).SequenceEqual(tips), "native hover dedupe " + language + " " + card.Id + " " + up);
                        formatted.Add(new { id = card.Id.Entry, language, upgraded = up, description });
                        if (language == "zhs")
                        {
                            string plain = Regex.Replace(description, @"\[[^\]]*\]", "");
                            if (card is BlazingChapter)
                                Check(plain.Contains($"在{(up ? 3 : 2)}个回合后，锁定波涛与翠叶法球。")
                                    && plain.Contains("你的烈焰法球将会额外结算一次。"), "user Blazing wording " + up);
                            if (card is Overfishing)
                                Check(plain.Contains("锁定后台法球。") && plain.Contains($"接下来{(up ? 4 : 3)}个你的回合开始时，你的前台法球数值翻倍。"), "user Overfishing wording " + up);
                            if (card is SpacetimeTwist)
                                Check(plain.Contains("你的回合开始时，随机锁定一个法球1回合。")
                                    && plain.Contains($"其余法球强化{(up ? 6 : 4)}并注魔。"), "user Spacetime wording " + up);
                            if (card is TidalMark)
                                Check(plain.Contains($"每当你锁定任意法球时，获得{(up ? 5 : 3)}点浪潮。"), "user TidalMark wording " + up);
                        }
                        if (card is TidalMark)
                            Check(card.Rarity == CardRarity.Rare && card.EnergyCost.GetWithModifiers(CostModifiers.None) == 1,
                                "TidalMark rare and one energy " + up);
                        if (card is RidgeWard)
                            Check(card.Rarity == CardRarity.Ancient && card.Keywords.Contains(CardKeyword.Retain), "RidgeWard Ancient and Retain " + up);
                    }
                }
            }
            File.WriteAllText(Path.Combine(output, "formatted-cards.json"), JsonSerializer.Serialize(formatted, new JsonSerializerOptions { WriteIndented = true }));
            LibrarianLanguage.Select("zhs");

            foreach (bool up in new[] { false, true })
            {
                await Reset(); await FireFixture(); events.Clear();
                await Play(Card<BlazingChapter>(up));
                var blazing = player.Creature.GetPower<BlazingChapterPower>()!;
                int delay = up ? 3 : 2;
                Check(blazing.Amount == 1 && blazing.PendingLockDelays.SequenceEqual(new[] { delay }), "Blazing one immediate extra and captured delay " + up);
                BlazingText(blazing, true, "single-applied-" + up);
                Check(!S().Orbs.IsLocked(OrbKind.Tide) && !S().Orbs.IsLocked(OrbKind.Growth)
                    && S().Orbs.Value(OrbKind.Tide) == 5 && S().Orbs.Value(OrbKind.Growth) == 7, "Blazing no immediate lock or old conversion " + up);
                string source = blazing.Id.ToString();
                long beforeSettlements = S().Orbs.SettlementsThisCombat; int enemyHp = Enemy().CurrentHp;
                await Turn();
                Check(S().Orbs.SettlementsThisCombat - beforeSettlements == 2 && enemyHp - Enemy().CurrentHp == 6,
                    "Blazing first real end turn already settles Fire twice " + up);
                Check(S().Orbs.Value(OrbKind.Tide) == 5 && S().Orbs.Value(OrbKind.Growth) == 7
                    && blazing.PendingLockDelays.SequenceEqual(new[] { delay - 1 }), "Blazing first real owner start decrements only " + up);
                for (int turn = 2; turn <= delay; turn++)
                {
                    await Turn();
                    Check(S().Orbs.IsLocked(OrbKind.Tide) == (turn == delay) && S().Orbs.IsLocked(OrbKind.Growth) == (turn == delay),
                        "Blazing locks exactly at own start " + up + " " + turn);
                }
                Check(blazing.PendingLockDelays.Length == 0 && S().Orbs.LockedTurns(OrbKind.Tide) == OrbView.PermanentLock
                    && S().Orbs.LockedTurns(OrbKind.Growth) == OrbView.PermanentLock, "Blazing permanent two-orb lock after wait " + up);
                BlazingText(blazing, false, "single-expired-" + up);
                int locks = events.Count(e => e.Origin.Source == source && e.Kind == OrbEventKind.Locked);
                await Turn();
                Check(locks == 2 && events.Count(e => e.Origin.Source == source && e.Kind == OrbEventKind.Locked) == locks,
                    "Blazing consumed countdown never repeats lock " + up);
                Check(!events.Any(e => e.Origin.Source == source && e.Kind == OrbEventKind.Lost), "Blazing never runs removed conversion " + up);
                eventEvidence.Add(new { label = "Blazing-single-" + up, events = events.Where(e => e.Origin.Source == source).ToArray() });

                await Reset(); await InactiveFixture(); await Play(Card<Overfishing>(up));
                var overfish = player.Creature.GetPower<OverfishingPower>()!;
                int remaining = up ? 4 : 3;
                Check(!S().Orbs.SwitchLocked && overfish.DynamicVars["Remaining"].IntValue == remaining,
                    "Overfishing leaves foreground movable and counts future turns " + up);
                Check(S().Orbs.LockedTurns(OrbKind.Tide) == OrbView.PermanentLock && S().Orbs.LockedTurns(OrbKind.Growth) == OrbView.PermanentLock,
                    "Overfishing locks current backgrounds permanently " + up);
                foreach (var kind in new[] { OrbKind.Tide, OrbKind.Growth, OrbKind.Fire, OrbKind.Tide }.Take(remaining))
                {
                    var swap = S().Orbs.SwapPositions(S().Orbs.Foreground, kind); await Dispatch(swap);
                    Check(swap.Status == OrbOperationStatus.Applied && S().Orbs.Foreground == kind, "Overfishing native dispatch allows changed foreground " + up + " " + kind);
                    int value = S().Orbs.Value(kind); var otherValues = S().Orbs.Positions.Where(k => k != kind).ToDictionary(k => k, S().Orbs.Value);
                    await Turn(); remaining--;
                    Check(S().Orbs.Value(kind) == value * 2 && otherValues.All(kv => S().Orbs.Value(kv.Key) == kv.Value),
                        "Overfishing real start doubles then-current foreground only " + up + " " + remaining);
                    Check(!S().Orbs.SwitchLocked && overfish.DynamicVars["Remaining"].IntValue == remaining, "Overfishing native start consumes once " + up + " " + remaining);
                }
                var exhaustedValues = S().Orbs.Positions.ToDictionary(k => k, S().Orbs.Value); await Turn();
                Check(exhaustedValues.All(kv => S().Orbs.Value(kv.Key) == kv.Value) && S().Orbs.IsLocked(OrbKind.Tide) && S().Orbs.IsLocked(OrbKind.Growth),
                    "Overfishing exhausted duration stops without unlocking " + up);

                await Reset(); await InactiveFixture(); await Play(Card<SpacetimeTwist>(up));
                var twist = player.Creature.GetPower<SpacetimeTwistPower>()!;
                var values = S().Orbs.Positions.ToDictionary(k => k, S().Orbs.Value); events.Clear();
                await Turn();
                var twistEvents = events.Where(e => e.Origin.Source == twist.Id.ToString()).ToArray();
                var lockEvents = twistEvents.Where(e => e.Kind == OrbEventKind.Locked).ToArray();
                Check(lockEvents.Length == 1 && twistEvents[0].Kind == OrbEventKind.Locked, "Spacetime real start dispatches lock first " + up);
                OrbKind locked = lockEvents.Single().Orb; int strength = up ? 6 : 4;
                Check(S().Orbs.LockedTurns(locked) == 1 && !S().Orbs.IsActivated(locked) && S().Orbs.Value(locked) == values[locked], "Spacetime chosen one-turn lock unchanged value " + up);
                Check(S().Orbs.Positions.Where(k => k != locked).All(k => S().Orbs.Value(k) == values[k] + strength && S().Orbs.IsActivated(k)),
                    "Spacetime real start strengthens and imbues only other two " + up);
                Check(twistEvents.Count(e => e.Kind == OrbEventKind.Strengthened) == 2 && twistEvents.Count(e => e.Kind == OrbEventKind.Imbued) == 2
                    && twistEvents.Where(e => e.Kind is OrbEventKind.Strengthened or OrbEventKind.Imbued).All(e => e.Sequence > lockEvents[0].Sequence),
                    "Spacetime lock event precedes both strengthen and imbue callbacks " + up);
                eventEvidence.Add(new { label = "Spacetime-real-start-" + up, events = twistEvents });

                await Reset(); await Play(Card<TidalMark>(up)); var tidal = player.Creature.GetPower<TidalMarkPower>()!; int wave = up ? 5 : 3;
                await Dispatch(S().Orbs.Lock(OrbKind.Fire, 1));
                Check(S().Waves.Amount == wave, "TidalMark one-turn temporary lock once " + up);
                await Dispatch(S().Orbs.Lock(OrbKind.Growth, 7));
                Check(S().Waves.Amount == wave * 2, "TidalMark seven-turn lock still once " + up);
                await Dispatch(S().Orbs.Lock(OrbKind.Fire, 1));
                Check(S().Waves.Amount == wave * 3, "TidalMark temporary extension counts one application " + up);
                await Dispatch(S().Orbs.Gain(OrbKind.Tide, 6)); await Dispatch(S().Orbs.LoseAll(OrbKind.Tide, OrbScope.All));
                Check(S().Waves.Amount == wave * 3 && S().Orbs.Value(OrbKind.Tide) == 0, "TidalMark Tide gain and loss no longer trigger " + up);
                await Dispatch(S().Orbs.Lock(OrbKind.Tide)); await Dispatch(S().Orbs.Lock(OrbKind.Tide));
                Check(S().Waves.Amount == wave * 5, "TidalMark permanent lock and permanent relock once each " + up);
                await Dispatch(S().Orbs.Lock(OrbKind.Fire)); await Dispatch(S().Orbs.Lock(OrbKind.Growth));
                Check(S().Waves.Amount == wave * 7, "TidalMark two orb applications trigger twice " + up);
                var foreignEvent = new OrbCombatState("beta2-foreign-owner").Lock(OrbKind.Fire, 1).Events.First(e => e.Kind == OrbEventKind.Locked);
                await tidal.OnOrbEvent(S(), context, foreignEvent);
                Check(S().Waves.Amount == wave * 7, "TidalMark foreign OwnerId guard (single-process callback probe) " + up);
                await Play(Card<TidalMark>(!up)); await Dispatch(S().Orbs.Lock(OrbKind.Tide));
                Check(tidal.Amount == 8 && S().Waves.Amount == wave * 7 + 8, "TidalMark 3 plus 5 stacks per application " + up);

                await Reset(); var retained = Card<RidgeWard>(up);
                await CardPileCmd.AddGeneratedCardToCombat(retained, PileType.Hand, player); await Turn();
                Check(player.PlayerCombatState!.Hand.Cards.Contains(retained) && retained.Keywords.Contains(CardKeyword.Retain), "RidgeWard native end-turn hand Retain " + up);
            }

            await Reset(); await FireFixture(); await Play(Card<BlazingChapter>()); await Play(Card<BlazingChapter>(true));
            var mixed = player.Creature.GetPower<BlazingChapterPower>()!; events.Clear();
            Check(mixed.Amount == 2 && mixed.PendingLockDelays.SequenceEqual(new[] { 2, 3 }), "Blazing mixed copies keep independent waits and add extras");
            BlazingText(mixed, true, "mixed-applied");
            var savedProperties = SavedProperties.From(mixed) ?? throw new InvalidOperationException("Blazing SavedProperties missing");
            var propertyTypeInfo = JsonSerializationUtility.GetTypeInfo<SavedProperties>();
            string propertyJson = JsonSerializer.Serialize(savedProperties, propertyTypeInfo);
            Check(propertyJson.Contains("int_arrays") && propertyJson.Contains("PendingLockTurns")
                && propertyJson.Contains("PendingLockReferenceTurn"), "Blazing native serializer includes saved countdown fields");
            var roundtripProperties = JsonSerializer.Deserialize(propertyJson, propertyTypeInfo)!;
            var clone = (BlazingChapterPower)ModelDb.Power<BlazingChapterPower>().ToMutable();
            roundtripProperties.Fill(clone);
            Check(clone.PendingLockTurns.SequenceEqual(mixed.PendingLockTurns)
                && clone.PendingLockDelays.SequenceEqual(new[] { 2, 3 }), "Blazing explicit SavedProperty JSON roundtrip (not game combat save)");
            clone.PendingLockTurns = [clone.PendingLockReferenceTurn + 9];
            Check(mixed.PendingLockDelays.SequenceEqual(new[] { 2, 3 }), "Blazing copied countdown arrays are independent");
            long settled = S().Orbs.SettlementsThisCombat; await Turn();
            Check(S().Orbs.SettlementsThisCombat - settled == 3 && mixed.PendingLockDelays.SequenceEqual(new[] { 1, 2 }), "Blazing two immediate extras and first countdowns");
            string mixedSource = mixed.Id.ToString();
            await Turn();
            Check(mixed.PendingLockDelays.SequenceEqual(new[] { 1 }) && events.Count(e => e.Origin.Source == mixedSource && e.Kind == OrbEventKind.Locked) == 2,
                "Blazing basic wait completes while upgraded wait remains");
            await Turn();
            Check(mixed.PendingLockDelays.Length == 0 && events.Count(e => e.Origin.Source == mixedSource && e.Kind == OrbEventKind.Locked) == 4,
                "Blazing upgraded wait repeats both permanent applications exactly once");
            BlazingText(mixed, false, "mixed-expired");
            await Turn();
            Check(events.Count(e => e.Origin.Source == mixedSource && e.Kind == OrbEventKind.Locked) == 4, "Blazing both consumed waits never repeat");
            eventEvidence.Add(new { label = "Blazing-mixed-copies", events = events.Where(e => e.Origin.Source == mixedSource).ToArray() });

            await Reset(); await InactiveFixture(); await Play(Card<Overfishing>()); await Play(Card<Overfishing>(true));
            var stacked = player.Creature.GetPower<OverfishingPower>()!;
            Check(stacked.DynamicVars["Remaining"].IntValue == 7, "Overfishing three plus four future turns stack");
            for (int i = 0; i < 7; i++)
            {
                int value = S().Orbs.Value(S().Orbs.Foreground); await Turn();
                Check(S().Orbs.Value(S().Orbs.Foreground) == value * 2 && stacked.DynamicVars["Remaining"].IntValue == 6 - i,
                    "Overfishing stacked real future start " + i);
            }
            int finished = S().Orbs.Value(S().Orbs.Foreground); await Turn();
            Check(S().Orbs.Value(S().Orbs.Foreground) == finished && !S().Orbs.SwitchLocked, "Overfishing stacked duration expires without anchoring");

            var pool = ModelDb.CardPool<LibrarianCardPool>().GetUnlockedCards(UnlockState.all, CardMultiplayerConstraint.SingleplayerOnly).ToArray();
            Check(pool.Contains(ModelDb.Card<TidalMark>()) && pool.Contains(ModelDb.Card<RidgeWard>())
                && pool.Where(c => c.Rarity == CardRarity.Rare).Contains(ModelDb.Card<TidalMark>())
                && pool.Where(c => c.Rarity == CardRarity.Ancient).Contains(ModelDb.Card<RidgeWard>()), "Native unlocked pool reflects exchanged rarities");
            await Reset(); await Capture(player, models, output, Check);
            var persistent = (ImmortalSpark)player.RunState.CreateCard(ModelDb.Card<ImmortalSpark>(), player);
            persistent.PermanentIncrease = 7; persistent.UpgradeInternal(); persistent.FinalizeUpgradeInternal();
            await CardPileCmd.Add(persistent, PileType.Deck);
            foreach (string id in Changed)
            {
                var canonical = models.Single(c => c.Id.Entry == "LIBRARIAN-" + id);
                foreach (bool up in new[] { false, true })
                {
                    var card = player.RunState.CreateCard(canonical, player);
                    if (up) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
                    await CardPileCmd.Add(card, PileType.Deck);
                    Check(card.Pile?.Type == PileType.Deck, "registered candidate deck card " + id + " " + up);
                }
            }
            File.WriteAllText(Path.Combine(output, "beta2-events.json"), JsonSerializer.Serialize(eventEvidence, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(output, "beta2-power-descriptions.json"), JsonSerializer.Serialize(powerDescriptions, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { LibrarianRuntime.OrbChanged -= Observe; LibrarianLanguage.Select(originalLanguage); }
        MainFile.Logger.Info($"CARD120_BETA2_AUDIT_PASS checks={checks} formatted=364 realTurns={realTurns} batchCards=5 native=True liveMulticlient=False combatPowerGameSave=False ancientRewardUI=False");
    }

    internal static void AfterReload(Player player)
    {
        var deck = PileType.Deck.GetPile(player).Cards;
        var immortal = deck.OfType<ImmortalSpark>().Single(c => c.PermanentIncrease == 7);
        if (!immortal.IsUpgraded || immortal.DynamicVars["Increase"].IntValue != 4)
            throw new InvalidOperationException("120 beta2 accumulated card after actual save/reload");
        foreach (string id in Changed)
        {
            var saved = deck.Where(c => c.Id.Entry == "LIBRARIAN-" + id).ToArray();
            if (saved.Length != 2 || saved.Count(c => c.IsUpgraded) != 1)
                throw new InvalidOperationException("120 beta2 candidate base/upgraded card save/reload " + id);
            foreach (var card in saved)
            {
                if (card is RidgeWard && (card.Rarity != CardRarity.Ancient || !card.Keywords.Contains(CardKeyword.Retain)))
                    throw new InvalidOperationException("120 beta2 RidgeWard rarity/Retain after reload");
                if (card is TidalMark && (card.Rarity != CardRarity.Rare || card.EnergyCost.GetWithModifiers(CostModifiers.None) != 1))
                    throw new InvalidOperationException("120 beta2 TidalMark rarity/cost after reload");
            }
        }
        MainFile.Logger.Info("CARD120_BETA2_SAVE_RELOAD_PASS candidateCards=10 permanentIncrease=7 upgraded=True combatPowerGameSave=False");
    }

    private static async Task Capture(Player player, CardModel[] models, string output, Action<bool, string> check)
    {
        check(DisplayServer.GetName() != "headless", "native card renderer");
        var originalSize = NGame.Instance!.GetWindow().Size;
        var layer = new CanvasLayer { Layer = 120 }; NGame.Instance.AddChild(layer);
        var panel = new Control(); layer.AddChild(panel);
        var background = new ColorRect { Color = new Color("20272e") }; panel.AddChild(background);
        int shots = 0;
        async Task Wait() => await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(.25), SceneTreeTimer.SignalName.Timeout);
        try
        {
            foreach (var resolution in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                DisplayServer.WindowSetSize(resolution); await Wait();
                var size = NGame.Instance.GetViewport().GetVisibleRect().Size; background.Size = size;
                foreach (string language in new[] { "zhs", "eng" })
                {
                    LibrarianLanguage.Select(language);
                    foreach (bool up in new[] { false, true })
                    {
                        var nodes = new List<NCard>();
                        try
                        {
                            foreach (string id in Changed)
                            {
                                var card = models.Single(c => c.Id.Entry == "LIBRARIAN-" + id).ToMutable(); card.Owner = player;
                                if (up) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
                                var node = MegaCrit.Sts2.Core.Assets.PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>();
                                panel.AddChild(node); node.Model = card; node.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
                                int i = nodes.Count; node.Scale = Vector2.One * Math.Min(size.X / 1500, size.Y / 1100);
                                node.Position = new(size.X * (i % 3 + .5f) / 3, size.Y * (i / 3 == 0 ? .27f : .75f));
                                nodes.Add(node);
                            }
                            await Wait(); await NGame.Instance.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                            using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
                            check(image.SavePng(Path.Combine(output, $"powers-{language}-{resolution.X}-{up}.png")) == Error.Ok, "beta2 card screenshot");
                            shots++;
                        }
                        finally { foreach (var node in nodes) node.QueueFree(); }
                        await Wait();
                    }
                }
            }
        }
        finally { layer.QueueFree(); DisplayServer.WindowSetSize(originalSize); await Wait(); }
        MainFile.Logger.Info($"CARD120_BETA2_VISUAL_CAPTURE_PASS screenshots={shots} cardViews=40 cards=5 languages=2 resolutions=2 baseAndUpgrade=True");
    }
}
