using System.IO;
using System.Text.Json;
using Godot;
using HarmonyLib;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Powers.Implemented;
using Librarian.LibrarianCode.Relics;
using Librarian.LibrarianCode.Cards.OrbBasics;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.Mechanics;

/// <summary>Opt-in regression at the actual end of all native turn-end hooks, before enemy actions.</summary>
internal static class DevelopmentEndTurnBlockAudit
{
    private static Player? _watch;
    private static int? _actual;
    [HarmonyPatch(typeof(Hook), "AfterTurnEnd")]
    private static class ObserveEnd
    {
        private static bool Prepare() => System.Environment.GetEnvironmentVariable("LIBRARIAN_041_FOCUS") == "block-preview";
        private static void Postfix(CombatSide side, ref Task __result)
        {
            if (side == CombatSide.Player && _watch is { } player) __result = Observe(__result, player);
        }
        private static async Task Observe(Task original, Player player)
        {
            await original;
            if (_watch == player) _actual = player.Creature.Block;
        }
    }
    private static IEnumerable<Node> Desc(Node n)
    {
        foreach (var c in n.GetChildren()) { yield return c; foreach (var d in Desc(c)) yield return d; }
    }
    internal static async Task Run(Player player, Func<Task> freshFight)
    {
        int checks = 0, turns = 0;
        var evidence = new List<object>();
        var context = new ThrowingPlayerChoiceContext();
        string output = System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT")!;
        Directory.CreateDirectory(output);
        string language = LibrarianLanguage.Selected;
        bool bar = LibrarianPreferences050.Current.WaveBar, reduced = LibrarianPreferences050.Current.ReducedMotion;
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("Block preview: " + label);
            checks++; MainFile.Logger.Info("BLOCK_PREVIEW_CHECK_PASS " + label);
        }
        async Task Frame() => await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
        async Task Wait(Func<bool> condition, string label)
        {
            for (int i = 0; !condition(); i++) { if (i > 3600) throw new TimeoutException(label); await Frame(); }
        }
        LibrarianSession S() => LibrarianRuntime.Get(player);
        LibrarianWaveBar Band() => Desc(NCombatRoom.Instance!).OfType<LibrarianWaveBar>().Single();
        async Task Reset(int shield = 0, int waves = 0, int tide = 0, int plating = 0)
        {
            await freshFight();
            await Wait(() => player.PlayerCombatState!.Phase == PlayerTurnPhase.Play, "Play phase");
            foreach (var relic in player.Relics.ToArray()) await RelicCmd.Remove(relic);
            S().Waves.Add(waves);
            if (tide > 0) await LibrarianRuntime.Dispatch(S(), context, S().Orbs.Gain(OrbKind.Tide, tide));
            if (plating > 0) await PowerCmd.Apply<PlatingPower>(context, player.Creature, plating, player.Creature, null);
            if (shield > 0) await CreatureCmd.GainBlock(player.Creature, shield, ValueProp.Unpowered, null);
        }
        async Task Shot(string label)
        {
            await Wait(() => !Desc(NGame.Instance!).OfType<Control>().Any(c =>
                c is MegaCrit.Sts2.Core.Nodes.Combat.NCombatStartBanner or MegaCrit.Sts2.Core.Nodes.Combat.NPlayerTurnBanner
                && c.IsVisibleInTree()), "banner before " + label);
            for (int i = 0; i < 30; i++) await Frame();
            await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var img = NGame.Instance.GetViewport().GetTexture().GetImage();
            Check(img.SavePng(Path.Combine(output, label + ".png")) == Error.Ok, "screenshot " + label);
        }
        async Task Turn(int expectedFinal, string label)
        {
            int current = player.Creature.Block;
            var preview = LibrarianEndTurnPreview.Read(S());
            Check(preview.Exact && preview.FinalMinimum == expectedFinal && current + preview.Minimum == expectedFinal,
                label + " predicted=" + JsonSerializer.Serialize(preview));
            string state = JsonSerializer.Serialize(S().Orbs.Snapshot());
            string rng = JsonSerializer.Serialize(player.RunState.Rng.ToSerializable());
            int wave = S().Waves.Amount;
            for (int i = 0; i < 30; i++) LibrarianEndTurnPreview.Read(S());
            Check(state == JsonSerializer.Serialize(S().Orbs.Snapshot()) && wave == S().Waves.Amount
                && rng == JsonSerializer.Serialize(player.RunState.Rng.ToSerializable()) && current == player.Creature.Block,
                label + " preview does not mutate live state, Block or RNG");
            await Wait(() => preview.Minimum == 0 && preview.ExpiringBlock == 0 || Band().Visible
                && Band().GetNode<Label>("WaveAmount").Text == preview.AmountText, "blue label " + label);
            Check(preview.Minimum == 0 && preview.ExpiringBlock == 0 || Band().Visible && Band().GetNode<Label>("WaveAmount").Text == preview.AmountText,
                label + " blue label matches net change");
            if (preview.Minimum < 0 || preview.ExpiringBlock > 0 || label.Contains("native cap")) await Shot("boundary-" + turns);
            _watch = player; _actual = null;
            int round = player.Creature.CombatState!.RoundNumber;
            CombatManager.Instance.SetReadyToEndTurn(player, false);
            await Wait(() => player.Creature.CombatState!.RoundNumber != round && player.PlayerCombatState!.Phase == PlayerTurnPhase.Play,
                "native turn " + label);
            _watch = null;
            Check(_actual == expectedFinal, label + " actual=" + _actual);
            evidence.Add(new { label, current, expectedFinal, preview, actual = _actual }); turns++;
        }
        try
        {
            LibrarianPreferences050.Current.WaveBar = true; LibrarianPreferences050.Current.ReducedMotion = true;
            await Reset(plating: 8); await Turn(8, "Plating without Waves");
            await Reset(11, plating: 8); await Turn(19, "existing shield plus Plating");
            await Reset(11, 14, 20, 8); await Turn(39, "foreground Tide, Waves and Plating");
            await Reset(11, 14, 20, 8); await LibrarianRuntime.Dispatch(S(), context, S().Orbs.Gain(OrbKind.Fire, 0));
            await Turn(33, "background Tide is halved");
            await Reset(11, 14, 20, 8); await LibrarianRuntime.Dispatch(S(), context, S().Orbs.Extinguish(OrbKind.Tide, OrbScope.All));
            await Turn(33, "inactive Tide uses Waves");
            await Reset(11, 14, 20, 8); await LibrarianRuntime.Dispatch(S(), context, S().Orbs.Lock(OrbKind.Tide));
            await Turn(33, "locked Tide uses Waves");
            await Reset(0, 14, 20, 8); await RelicCmd.Obtain<Orichalcum>(player);
            await Turn(34, "Orichalcum snapshots zero before Plating");
            await Reset(1, 14, 20, 8); await RelicCmd.Obtain<Orichalcum>(player);
            await Turn(29, "Orichalcum blocked by existing shield");
            await Reset(0, 14, 20, 8); await RelicCmd.Obtain<CloakClasp>(player);
            await Turn(28 + player.PlayerCombatState!.Hand.Cards.Count, "Cloak Clasp counts current hand");
            await Reset(0, 14, 20, 8); await RelicCmd.Obtain<RippleBasin>(player); await Turn(32, "Ripple Basin without Attack");
            await Reset(plating: 8); await RelicCmd.Obtain<RippleBasin>(player);
            var attack = player.Creature.CombatState!.CreateCard<FlameStrike>(player);
            await CardCmd.AutoPlay(context, attack, player.Creature.CombatState.HittableEnemies.First(), skipCardPileVisuals: true);
            await Turn(8, "Ripple Basin excluded after real Attack");
            await Reset(7, 10, 5, 4); await PowerCmd.Apply<ShadowmeldPower>(context, player.Creature, 1, player.Creature, null);
            await Turn(35, "Shadowmeld modifies each gain but not Waves comparison");
            await Reset(11, 14, 20, 8); await PowerCmd.Apply<DexterityPower>(context, player.Creature, 9, player.Creature, null);
            await PowerCmd.Apply<FrailPower>(context, player.Creature, 2, player.Creature, null); await Turn(39, "unpowered gains ignore Dexterity and Frail");
            await Reset(11, plating: 8); await PowerCmd.Apply<NoBlockPower>(context, player.Creature, 2, player.Creature, null);
            await Turn(19, "No Block does not suppress unpowered Plating");
            await Reset(999999994, tide: 20, plating: 8); await Turn(999999999, "native cap changes requested gain to net plus five");
            await Reset(12, tide: 20); S().Orbs.BlockLedger.Consume(12);
            S().Orbs.BlockLedger.RecordTideGain(7, S().Orbs.OwnerTurn - 1); S().Orbs.BlockLedger.RecordOrdinaryGain(5, S().Orbs.OwnerTurn);
            await Turn(25, "expiry is subtracted from net change");
            await Reset(12); S().Orbs.BlockLedger.Consume(12);
            S().Orbs.BlockLedger.RecordTideGain(7, S().Orbs.OwnerTurn - 1); S().Orbs.BlockLedger.RecordOrdinaryGain(5, S().Orbs.OwnerTurn);
            await Turn(5, "expiry only shows minus seven");
            await Reset(7, tide: 7); S().Orbs.BlockLedger.Consume(7); S().Orbs.BlockLedger.RecordTideGain(7, S().Orbs.OwnerTurn - 1);
            await Turn(7, "equal gain and expiry shows zero");
            await Reset(11, plating: 8); await CardPileCmd.Add(player.Creature.CombatState!.CreateCard<Burn>(player), PileType.Hand);
            await Turn(17, "Burn damage consumes Block after Plating");
            await Reset(11, plating: 8); await PowerCmd.Apply<ConstrictPower>(context, player.Creature, 5, player.Creature, null);
            await Turn(14, "Constrict consumes Block in after-turn hooks");
            await Reset(11, plating: 8); await PowerCmd.Apply<IntangiblePower>(context, player.Creature, 1, player.Creature, null);
            await CardPileCmd.Add(player.Creature.CombatState!.CreateCard<Burn>(player), PileType.Hand);
            await PowerCmd.Apply<ConstrictPower>(context, player.Creature, 5, player.Creature, null);
            await Turn(17, "Intangible caps each turn-end damage separately");
            await Reset(11, plating: 8); await PowerCmd.Apply<FeelNoPainPower>(context, player.Creature, 3, player.Creature, null);
            await CardPileCmd.Add(player.Creature.CombatState!.CreateCard<Dazed>(player), PileType.Hand);
            await Turn(22, "Ethereal Exhaust triggers Feel No Pain");
            await Reset(11, plating: 8); await OrbCmd.AddSlots(player, 2);
            await MegaCrit.Sts2.Core.Commands.OrbCmd.Channel<FrostOrb>(context, player);
            await PowerCmd.Apply<FocusPower>(context, player.Creature, 3, player.Creature, null);
            await RelicCmd.Obtain<GoldPlatedCables>(player); await Turn(29, "Frost, Focus and Gold Plated Cables");
            await Reset(plating: 8); await LibrarianRuntime.Dispatch(S(), context, S().Orbs.Gain(OrbKind.Fire, 12));
            await LibrarianRuntime.Dispatch(S(), context, S().Orbs.Gain(OrbKind.Tide, 5));
            await LibrarianRuntime.Dispatch(S(), context, S().Orbs.Gain(OrbKind.Growth, 20));
            await RelicCmd.Obtain<LibrarianRarePlaceholderTwo>(player); await Turn(20, "channel-specific Growth settlement relic");
            await Reset(waves: 9, plating: 8);
            var delayed = await PowerCmd.Apply<DeepSeaPendingPower>(context, player.Creature, 30, player.Creature, null);
            delayed!.Schedule(S(), 30);
            await Turn(System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_CHANNEL") == "stable" ? 38 : 17,
                "channel-specific delayed Deep Sea effect");
            await Reset(11, plating: 8); S().Orbs.Strengthen(OrbKind.Tide, 20, OrbScope.All);
            await RelicCmd.Obtain<LibrarianRarePlaceholderOne>(player);
            var range = LibrarianEndTurnPreview.Read(S());
            Check(!range.Exact && range.Minimum == 8 && range.Maximum == 28 && range.FinalMinimum == 19 && range.FinalMaximum == 39,
                "random activation range preserves total identity " + JsonSerializer.Serialize(range));
            await Shot("random-range");
            _watch = player; _actual = null; int randomRound = player.Creature.CombatState!.RoundNumber;
            CombatManager.Instance.SetReadyToEndTurn(player, false);
            await Wait(() => player.Creature.CombatState!.RoundNumber != randomRound && player.PlayerCombatState!.Phase == PlayerTurnPhase.Play, "random real turn");
            _watch = null; Check(_actual >= range.FinalMinimum && _actual <= range.FinalMaximum, "real random total lies within preview range");
            evidence.Add(new { label = "random activation range", current = range.CurrentBlock, preview = range, actual = _actual }); turns++;
            await Reset(11, 14, 20, 8);
            foreach (string lang in new[] { "zhs", "eng" })
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                LibrarianLanguage.Select(lang); DisplayServer.WindowSetSize(size); await Shot("total-" + lang + "-" + size.X);
                Band().EmitSignal(Control.SignalName.MouseEntered); await Shot("hover-" + lang + "-" + size.X);
                Check(LibrarianEndTurnPreview.Read(S()).Description.Contains((player.Creature.Block + 28).ToString()), "localized final total " + lang);
                await CreatureCmd.GainBlock(player.Creature, 3, ValueProp.Unpowered, null);
                await Shot("hover-refresh-" + lang + "-" + size.X);
                Check(LibrarianEndTurnPreview.Read(S()).Description.Contains((player.Creature.Block + 28).ToString()), "hover refreshed after Block gain " + lang);
                Check((string?)AccessTools.Field(typeof(LibrarianWaveBar), "_hoverText").GetValue(Band())
                    == LibrarianEndTurnPreview.Read(S()).Description, "displayed hover text stays current " + lang);
                Band().EmitSignal(Control.SignalName.MouseExited);
            }
            await Reset(plating: 8); await LibrarianRuntime.Dispatch(S(), context, S().Orbs.Gain(OrbKind.Fire, 3));
            await PowerCmd.Apply<ThornsPower>(context, player.Creature.CombatState!.HittableEnemies.First(), 5, player.Creature, null);
            Check(LibrarianEndTurnPreview.Read(S()).Uncertainty == "modifiers", "unsupported damage reaction cannot promise exact total");
            await Reset(); await PowerCmd.Apply<FuelTheFirePower>(context, player.Creature, 1, player.Creature, null);
            await Shot("uncertain-bottom"); Check(Band().GetNode<Label>("WaveAmount").Text == "?", "unknown is explicit question mark");
            await Reset(plating: 8); await PowerCmd.Apply<JuggernautPower>(context, player.Creature, 5, player.Creature, null);
            await Shot("uncertain-trigger"); Check(LibrarianEndTurnPreview.Read(S()).Uncertainty == "modifiers", "unsupported gain trigger cannot promise exact total");
            LibrarianPreferences050.Current.WaveBar = false; for (int i = 0; i < 15; i++) await Frame(); Check(!Band().Visible, "setting hides preview");
            File.WriteAllText(Path.Combine(output, "cases.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
            MainFile.Logger.Info($"BLOCK_PREVIEW_AUDIT_PASS checks={checks} realTurns={turns} beforeEnemy=True liveMulticlient=False");
        }
        finally { _watch = null; LibrarianLanguage.Select(language); LibrarianPreferences050.Current.WaveBar = bar; LibrarianPreferences050.Current.ReducedMotion = reduced; }
    }
    internal static async Task AfterReload(Player player)
    {
        var context = new ThrowingPlayerChoiceContext();
        await PowerCmd.Apply<PlatingPower>(context, player.Creature, 8, player.Creature, null);
        var preview = LibrarianEndTurnPreview.Read(LibrarianRuntime.Get(player));
        if (!preview.Exact || preview.Minimum != 8 || player.Creature.Block + preview.Minimum != preview.FinalMinimum)
            throw new InvalidOperationException("Block preview after real save/reload: " + JsonSerializer.Serialize(preview));
        MainFile.Logger.Info("BLOCK_PREVIEW_RELOAD_PASS totalIdentity=True Plating=True");
    }
}
