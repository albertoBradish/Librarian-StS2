using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using STS2RitsuLib.Settings;

namespace Librarian.Mechanics;

/// <summary>Explicit dependency integration audit; inactive during ordinary play.</summary>
[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class DevelopmentDependencyUpgradeAudit
{
    private static bool _ran;
    [HarmonyPostfix]
    private static void Postfix()
    {
        if (_ran || System.Environment.GetEnvironmentVariable("LIBRARIAN_DEPENDENCY_AUDIT") != "1") return;
        _ran = true;
        Callable.From((Action)(() => { _ = DevelopmentRitsuCardSweepAudit.RunDependencyUpgrade(); })).CallDeferred();
    }
}

internal static partial class DevelopmentRitsuCardSweepAudit
{
    internal static async Task RunDependencyUpgrade()
    {
        var game = NGame.Instance!;
        string phase = System.Environment.GetEnvironmentVariable("LIBRARIAN_DEPENDENCY_PHASE") ?? "";
        string expected = System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_VERSION") ?? "";
        string required = System.Environment.GetEnvironmentVariable("LIBRARIAN_DEPENDENCY_RITSU") ?? "";
        try
        {
            string boundary = System.Environment.GetEnvironmentVariable("LIBRARIAN_DEPENDENCY_PROFILE") ?? "";
            string user = Path.GetFullPath(OS.GetUserDataDir());
            Check(Path.IsPathFullyQualified(boundary) && user.StartsWith(Path.GetFullPath(boundary) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && user.Contains("dependency-upgrade", StringComparison.OrdinalIgnoreCase), "explicit dependency-only isolated profile");
            Check(phase is "main" or "restart", "explicit dependency phase");
            Directory.CreateDirectory(Output);
            var startup = await DevelopmentCardRemasterStartupFence.WaitForCompletion();
            Check(game.MainMenu!.IsVisibleInTree(), "rendered native main menu");
            var content = typeof(MainFile).Assembly;
            Check(LibrarianUpdateNotice051.CurrentVersion == expected && content.GetName().Version == System.Version.Parse(expected + ".0"), "current native mod and selected content version");
#if LIBRARIAN_API_01131
            const string host = "0.113.1";
#elif LIBRARIAN_API_0113
            const string host = "0.113.0";
#elif LIBRARIAN_API_01111
            const string host = "0.111.1";
#elif LIBRARIAN_API_0107
            const string host = "0.107.1";
#else
            const string host = "0.111.0";
#endif
            Check(LibrarianGameApi.GameVersion.TrimStart('v', 'V') == host
                && content.Location.Replace('\\', '/').EndsWith("/compat/" + host + "/Librarian.dll", StringComparison.OrdinalIgnoreCase), "fixed host selects matching content");
            Check(AppDomain.CurrentDomain.GetAssemblies().Count(a => a.GetName().Name == "Librarian") == 1, "one selected content assembly");
            var dependency = ModManager.GetLoadedMods().Single(m => m.manifest?.id == "STS2-RitsuLib");
            Check(dependency.manifest!.version == required, "native RitsuLib package version");
            var ritsuRuntime = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "STS2-RitsuLib.Runtime");
            Check(ritsuRuntime.GetName().Version == System.Version.Parse(required + ".0"), "actual Ritsu runtime assembly version");
            Check(!content.GetReferencedAssemblies().Any(a => a.Name == "BaseLib") && !AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "BaseLib"), "Ritsu-only runtime");
            foreach (string page in new[] { LibrarianSettings041.PageId, LibrarianSettings041.DisplayPageId, LibrarianSettings041.EffectsPageId, LibrarianSettings041.ToolsPageId })
                Check(ModSettingsRegistry.TryGetPage(MainFile.ModId, page, out _), "registered settings page " + page);
            var cards = RemasterCards();
            Check(cards.Length == 92 && cards.Select(c => c.Id).Distinct().Count() == 92, "91 active cards plus StudyNotes registered once");
            foreach (var card in cards)
                Check(ResourceLoader.Exists(card.PortraitPath), "registered portrait " + card.Id);
            int formatted = 0;
            foreach (string language in new[] { "zhs", "eng", "kor", "jpn", "spa", "fra" })
            {
                LibrarianLanguage.Select(language);
                foreach (var model in cards) foreach (bool upgraded in new[] { false, true })
                {
                    var mutable = model.ToMutable();
                    if (upgraded) { mutable.UpgradeInternal(); mutable.FinalizeUpgradeInternal(); }
                    Check(!mutable.GetDescriptionForPile(PileType.None).Contains('{'), "formatted description " + language + " " + model.Id);
                    formatted++;
                }
            }
            LibrarianLanguage.Select("zhs");
            MainFile.Logger.Info($"DEPENDENCY_UPGRADE_MENU_PASS version={expected} ritsu={required} game={host} cards={cards.Length} formats={formatted} settings=4");
            if (phase == "main")
            {
                string? baseline = System.Environment.GetEnvironmentVariable("LIBRARIAN_DEPENDENCY_BASELINE_DECK");
                if (!string.IsNullOrEmpty(baseline))
                {
                    var oldSave = SaveManager.Instance.LoadRunSave();
                    Check(oldSave.Success && oldSave.SaveData is not null, "previous released dependency save is readable");
                    var oldRun = RunState.FromSerializable(oldSave.SaveData!);
                    await RunManager.Instance.SetUpSavedSingleplayer(oldRun, oldSave.SaveData!);
                    await game.LoadRun(oldRun, oldSave.SaveData!.PreFinishedRoom); await game.Transition.FadeIn();
                    _player = oldRun.Players.Single();
                    string actual = JsonSerializer.Serialize(PileType.Deck.GetPile(_player).Cards.Select(Card), Json);
                    Check(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(baseline)), JsonNode.Parse(actual)), "baseline save keeps every card identity and value");
                    MainFile.Logger.Info($"DEPENDENCY_UPGRADE_LEGACY_SAVE_PASS version={expected} ritsu={required} cards={PileType.Deck.GetPile(_player).Cards.Count}");
                    await Settle(2.5); await game.ReturnToMainMenu();
                }
                SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
                LibrarianUnlocks040.ApplyChoice(SaveManager.Instance.Progress, true);
                var run = await game.StartNewSingleplayerRun(ModelDb.Character<LibrarianCharacter>(), true, ActModel.GetDefaultList(), [], "RITSU0612", GameMode.Standard);
                _player = run.Players.Single();
                await RemasterFreshFight();
                foreach (var relic in _player.Relics.ToArray()) await RelicCmd.Remove(relic);
                foreach (string name in new[] { "LibrarianStrike", "LibrarianDefend", "Spark", "ToBeContinued" })
                {
                    var model = cards.Single(c => c.GetType().Name == name);
                    await Reset(model); await Play(Create(model));
                }
                await Turn();
#if LIBRARIAN_API_0113
                var endless = cards.Single(c => c.GetType().Name == "EndlessTide");
                await Reset(endless);
                await Play(Create(endless));
                // The standard fixture seeds Growth, which would strengthen Tide at end of turn.
                // Isolate the exact +1 from the native turn-start callback.
                await LibrarianRuntime.Dispatch(Session, Context, Session.Orbs.Extinguish(Librarian.Core.OrbKind.Growth, Librarian.Core.OrbScope.All));
                var tideBefore = Session.Orbs.Value(Librarian.Core.OrbKind.Tide);
                await Turn();
                Check(Session.Orbs.Value(Librarian.Core.OrbKind.Tide) == tideBefore + 1
                    && Session.Orbs.IsActivated(Librarian.Core.OrbKind.Tide), "Endless Tide uses the native turn-start hook");
                var still = cards.Single(c => c.GetType().Name == "RidgeWard");
                await Reset(still);
                await Play(Create(still));
                await Turn();
                Check(_player.PlayerCombatState!.Hand.Cards.Count > 0, "Still Tide does not interrupt the next draw");
                await Reset(cards.Single(c => c.GetType().Name == "LibrarianStrike"));
                await Play(Create(cards.Single(c => c.GetType().Name == "LibrarianStrike")));
                MainFile.Logger.Info("V147_TURN_ISSUES_PASS");
#endif
                await SaveReload(cards);
                LibrarianPreferences050.Current.ReducedMotion = true;
                LibrarianPreferences050.Save();
                await SaveManager.Instance.SaveRun(null);
            }
            else
            {
                LibrarianPreferences050.Load();
                Check(LibrarianPreferences050.Current.ReducedMotion, "mod preference survives an independent process");
                var saved = SaveManager.Instance.LoadRunSave();
                Check(saved.Success && saved.SaveData is not null, "independent native saved run read");
                var run = RunState.FromSerializable(saved.SaveData!);
                await RunManager.Instance.SetUpSavedSingleplayer(run, saved.SaveData!);
                await game.LoadRun(run, saved.SaveData!.PreFinishedRoom); await game.Transition.FadeIn();
                _player = run.Players.Single();
                Check(PileType.Deck.GetPile(_player).Cards.Count == cards.Length * 2, "all base/upgraded cards survive an independent process");
                string restoredDeck = JsonSerializer.Serialize(PileType.Deck.GetPile(_player).Cards.Select(Card), Json);
                Check(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(Path.Combine(Output, "reloaded-deck.json"))), JsonNode.Parse(restoredDeck)), "independent process preserves all saved deck values");
                foreach (var model in cards) foreach (bool upgraded in new[] { false, true })
                    Check(PileType.Deck.GetPile(_player).Cards.Any(c => c.Id == model.Id && c.IsUpgraded == upgraded), "independent card identity " + model.Id);
                // Debug encounters are not map visits; their save has no PreFinishedRoom.
                // Validate the actual restored run first, then use its native next combat.
                File.WriteAllText(Path.Combine(Output, "restart-load-state.json"), JsonSerializer.Serialize(new {
                    pre_finished_room_present = saved.SaveData.PreFinishedRoom is not null,
                    native_combat_was_active = CombatManager.Instance.IsInProgress,
                    restored_deck = PileType.Deck.GetPile(_player).Cards.Count }, Json));
                await RemasterFreshFight();
                await Reset(cards.Single(c => c.GetType().Name == "LibrarianStrike"));
                await Play(Create(cards.Single(c => c.GetType().Name == "LibrarianStrike")));
                await SaveManager.Instance.SaveRun(null);
            }
            var summary = new { passed = true, phase, version = expected, ritsu = required, game = host,
                checks = _checks, cards = cards.Length, formatted, settings_pages = 4, pid = System.Environment.ProcessId,
                startup, saved_cards = cards.Length * 2, new_run_combat_save_reload = phase == "main",
                independent_restart = phase == "restart", restored_run_started_new_encounter = phase == "restart",
                mid_combat_snapshot_restored = false, real_multiclient = false, screenshots = 0 };
            File.WriteAllText(Path.Combine(Output, "dependency-" + phase + ".json"), JsonSerializer.Serialize(summary, Json));
            MainFile.Logger.Info($"DEPENDENCY_UPGRADE_{phase.ToUpperInvariant()}_PASS version={expected} ritsu={required} game={host} checks={_checks}");
        }
        catch (Exception error)
        {
            MainFile.Logger.Error("DEPENDENCY_UPGRADE_AUDIT_FAIL " + phase + " " + error);
            File.WriteAllText(Path.Combine(Output, "dependency-" + phase + "-failure.txt"), error.ToString());
        }
        finally { await Settle(1); game.Quit(); }
    }
}
