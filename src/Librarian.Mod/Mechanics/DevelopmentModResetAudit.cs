using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.OrbBasics;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using STS2RitsuLib.Saves.RawProgress;
using STS2RitsuLib.Settings;

namespace Librarian.Mechanics;

/// <summary>Opt-in UI lifecycle regression against a dedicated isolated profile.</summary>
internal static class DevelopmentModResetAudit
{
    private static NGame Game => NGame.Instance!;
    private static string _output = "";
    private static int _checks, _shots, _resetUiCount;
    private static bool _keyboardMouseFallback;
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("Mod reset audit: " + label);
        _checks++; MainFile.Logger.Info("MOD_RESET_CHECK_PASS " + label);
    }
    private static async Task Until(Func<bool> condition, string label, double seconds = 30)
    {
        ulong end = Time.GetTicksMsec() + (ulong)(seconds * 1000);
        while (!condition()) { if (Time.GetTicksMsec() > end) throw new TimeoutException(label); await Wait(.1); }
    }
    private static async Task Wait(double seconds = .2) => await Game.ToSignal(Game.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private static IEnumerable<Node> Descendants(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static async Task Capture(string name)
    {
        await Game.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = Game.GetViewport().GetTexture().GetImage();
        if (image.SavePng(Path.Combine(_output, name + ".png")) != Error.Ok) throw new IOException("Screenshot failed");
        _shots++;
    }
    private static string Hash(string path) => File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : "absent";
    private static Dictionary<string, string> RecordHashes()
    {
        string native = ProjectSettings.GlobalizePath(SaveManager.Instance.GetProfileScopedPath("saves/progress.save"));
        string mirror = LibrarianDebugReset.MirrorPath;
        return new[] { native, native + ".backup", mirror, mirror + ".backup" }
            .Concat(LibrarianDebugReset.LocalRecordPaths).ToDictionary(path => path, Hash);
    }
    private static Dictionary<string, string> RunHashes() => new[] { "current_run.save", "current_run.save.backup", "current_run_mp.save", "current_run_mp.save.backup" }
        .ToDictionary(name => name, name => Hash(ProjectSettings.GlobalizePath(SaveManager.Instance.GetProfileScopedPath("saves/" + name))));
    private static void CheckHashes(Dictionary<string, string> expected, Dictionary<string, string> actual, string label)
    {
        foreach (var pair in expected) Check(actual[pair.Key] == pair.Value, label + " " + Path.GetFileName(pair.Key));
    }
    private static void SuspendNotices()
    {
        foreach (var node in Descendants(Game).Where(n => n is LibrarianUpdateNotice051 or LibrarianArchitectReview102)) node.SetProcess(false);
    }
    private static async Task<RawProgressSnapshot> Snapshot()
    {
        var result = await RawProgressBridge.Instance.CaptureAsync();
        Check(result.Outcome == RawProgressReadOutcome.Succeeded && result.Snapshot is not null, "public raw capture succeeds");
        return result.Snapshot!;
    }
    private static async Task Commit(RawProgressSnapshot before, string proposed)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(proposed);
        var bridge = RawProgressBridge.Instance;
        var result = await bridge.CommitAsync(new RawProgressCommitRequest
        {
            ProtocolVersion = bridge.Describe().ProtocolVersion, SchemaVersion = before.SchemaVersion,
            OwnerId = "Librarian-debug-reset-audit", TransactionId = Guid.NewGuid(), ExpectedGeneration = before.Generation,
            ProposedRawJson = proposed, ProposedSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), ProposedUtf8Length = bytes.Length
        });
        MainFile.Logger.Info($"MOD_RESET_FIXTURE_COMMIT outcome={result.Outcome} continuation={result.UnknownJsonContinuationInstalled} cloud={result.CloudStatus} live={result.LiveStateDisposition} journal={result.RecoveryJournalRetained}");
        Check(result.Outcome == RawProgressCommitOutcome.CommittedVerified && result.UnknownJsonContinuationInstalled, "fixture raw commit synchronizes known and unknown records");
        Check(result.CloudStatus == CloudReadBackStatus.NotRequired, "isolated fixture has no cloud backend");
        SaveManager.Instance.SaveProgressFile();
    }
    private static string _foreignProjection = "";
    private static async Task CheckPreservationPreflight()
    {
        var snapshot = await Snapshot();
        var before = RecordHashes();
        var live = SaveManager.Instance.Progress;
        string valid = LibrarianDebugReset.SanitizeProgressJson(snapshot.RawJson);
        RawProgressCommitRequest Request(string json) => new()
        {
            ProtocolVersion = RawProgressBridge.Instance.Describe().ProtocolVersion, SchemaVersion = snapshot.SchemaVersion,
            OwnerId = "Librarian-debug-reset-audit", TransactionId = Guid.NewGuid(), ExpectedGeneration = snapshot.Generation,
            ProposedRawJson = json, ProposedSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant(),
            ProposedUtf8Length = Encoding.UTF8.GetByteCount(json)
        };
        LibrarianDebugReset.VerifyRawPreservation(Request(valid));
        Check(true, "valid sanitized proposal passes preservation preflight");
        var bad = (JsonObject)JsonNode.Parse(valid)!;
        var encounters = bad["encounter_stats"] as JsonArray ?? (JsonArray)(bad["encounter_stats"] = new JsonArray())!;
        string encounter = ModelDb.Encounter<TunnelerWeak>().Id.ToString();
        foreach (var old in encounters.Where(n => n?["encounter_id"]?.GetValue<string>() == encounter).ToArray()) encounters.Remove(old);
        encounters.Add(new JsonObject
        {
            ["encounter_id"] = encounter,
            ["fight_stats"] = new JsonArray(new JsonObject
            {
                ["character"] = ModelDb.Character<Ironclad>().Id.ToString(), ["wins"] = 3, ["losses"] = 1,
                ["foreign_reset_nested_extension"] = "must-survive"
            })
        });
        bool rejected = false;
        try { LibrarianDebugReset.VerifyRawPreservation(Request(bad.ToJsonString())); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected, "unsupported foreign nested extension is rejected before commit");
        Check(ReferenceEquals(live, SaveManager.Instance.Progress), "preflight leaves actual live progress reference unchanged");
        CheckHashes(before, RecordHashes(), "preflight leaves native mirror and local settings bytes unchanged");
    }
    private static async Task SeedRecords()
    {
        var snapshot = await Snapshot();
        var data = (JsonObject)JsonNode.Parse(snapshot.RawJson)!;
        string character = ModelDb.Character<LibrarianCharacter>().Id.ToString(), vanilla = ModelDb.Character<Ironclad>().Id.ToString();
        string card = ModelDb.Card<Spark>().Id.ToString();
        JsonArray Array(string key) => data[key] as JsonArray ?? (JsonArray)(data[key] = new JsonArray())!;
        void Add(string key, JsonNode row) => Array(key).Add(row);
        var chars = Array("character_stats");
        foreach (var old in chars.Where(n => n?["id"]?.GetValue<string>() == character).ToArray()) chars.Remove(old);
        Add("character_stats", new JsonObject { ["id"] = character, ["total_wins"] = 5, ["total_losses"] = 4, ["playtime"] = 999, ["max_ascension"] = 20, ["preferred_ascension"] = 17 });
        foreach (string id in new[] { card, "CARD.LIBRARIAN-RETIRED_DEBUG_CARD", "CARD.FOREIGN_DEBUG_CARD" })
        {
            var cards = Array("card_stats");
            foreach (var old in cards.Where(n => n?["id"]?.GetValue<string>() == id).ToArray()) cards.Remove(old);
            Add("card_stats", new JsonObject { ["id"] = id, ["times_picked"] = 7, ["times_won"] = 2 });
            if (!Array("discovered_cards").Any(n => n?.GetValue<string>() == id)) Add("discovered_cards", JsonValue.Create(id)!);
        }
        foreach (string key in new[] { "discovered_relics", "discovered_potions", "discovered_events", "discovered_acts" })
            Add(key, JsonValue.Create(key switch { "discovered_relics" => "RELIC.LIBRARIAN-RETIRED_DEBUG_RELIC", "discovered_potions" => "POTION.LIBRARIAN-RETIRED_DEBUG_POTION", "discovered_events" => "EVENT.LIBRARIAN-RETIRED_DEBUG_EVENT", _ => "ACT.LIBRARIAN-RETIRED_DEBUG_ACT" })!);
        foreach (var (section, idKey, rows) in new[] { ("encounter_stats", "encounter_id", "fight_stats"), ("enemy_stats", "enemy_id", "fight_stats"), ("ancient_stats", "ancient_id", "character_stats") })
        {
            var array = Array(section);
            var row = array.FirstOrDefault() as JsonObject;
            string category = section == "encounter_stats" ? "ENCOUNTER" : section == "enemy_stats" ? "MONSTER" : "EVENT";
            if (row is null) { row = new JsonObject { [idKey] = category + ".FOREIGN_DEBUG_" + section.ToUpperInvariant() }; array.Add(row); }
            row[rows] = new JsonArray(new JsonObject { ["character"] = character, ["wins"] = 4, ["losses"] = 2 }, new JsonObject { ["character"] = vanilla, ["wins"] = 8, ["losses"] = 3 });
            Add(section, new JsonObject { [idKey] = category + ".LIBRARIAN-RETIRED_" + section.ToUpperInvariant(), [rows] = new JsonArray() });
        }
        foreach (var old in Array("epochs").Where(n => Enumerable.Range(1, 7).Any(i => n?["id"]?.GetValue<string>() == LibrarianUnlocks040.Id(i))).ToArray()) Array("epochs").Remove(old);
        foreach (int number in Enumerable.Range(1, 7)) Add("epochs", new JsonObject { ["id"] = LibrarianUnlocks040.Id(number), ["state"] = (int)EpochState.Revealed, ["obtain_date"] = 1000 });
        foreach (string marker in new[] { LibrarianOnboarding.Decision, LibrarianOnboarding.Accepted, LibrarianOnboarding.Completed, LibrarianOnboarding.CompactDecision, LibrarianUnlocks040.ChoiceMarker, LibrarianUnlocks040.AllMarker, "foreign_reset_fixture_receipt" })
            if (!Array("ftue_completed").Any(n => n?.GetValue<string>() == marker)) Add("ftue_completed", JsonValue.Create(marker)!);
        data["pending_character_unlock"] = character;
        data["foreign_reset_fixture_extension"] = new JsonObject { ["keep"] = "unchanged" };
        await Commit(snapshot, data.ToJsonString());
        var stored = await Snapshot();
        _foreignProjection = LibrarianDebugReset.SanitizeProgressJson(stored.RawJson);
        File.WriteAllText(Path.Combine(_output, "non-owned-projection-before.sha256"), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_foreignProjection))));
        LibrarianPreferences050.Current.CompactHoverTips = true;
        LibrarianPreferences050.Current.CardEffects = false; LibrarianPreferences050.Current.SoundVolume = 20;
        LibrarianPreferences050.Save(); LibrarianLanguage.Select("eng");
        LibrarianNoticeHistory051.Acknowledge("reset-audit-old-version");
        LibrarianArchitectReviewHistory102.MarkShown();
        Check(!JsonNode.DeepEquals(JsonNode.Parse(stored.RawJson), JsonNode.Parse(_foreignProjection)), "fixture actually contains owned records to reset");
    }
    private static async Task CheckResetState(bool compareForeign)
    {
        LibrarianDebugReset.VerifyProgressFiles(); _checks += 4;
        var snapshot = await Snapshot();
        var actual = JsonNode.Parse(snapshot.RawJson);
        Check(LibrarianDebugReset.IsInitialProgressJson(snapshot.RawJson), "owned records restore exact first-use progressive baseline including retired/unavailable ID removal");
        if (compareForeign) Check(JsonNode.DeepEquals(JsonNode.Parse(LibrarianDebugReset.SanitizeProgressJson(snapshot.RawJson)), JsonNode.Parse(_foreignProjection)), "all non-owned and unknown JSON remains semantically identical");
        var stats = SaveManager.Instance.Progress.GetStatsForCharacter(ModelDb.Character<LibrarianCharacter>().Id);
        Check(stats is null || stats.TotalWins == 0 && stats.TotalLosses == 0 && stats.Playtime == 0 && stats.MaxAscension == 0, "native Librarian statistics reset");
        Check(!LibrarianOnboarding.Has(LibrarianOnboarding.Decision) && !LibrarianOnboarding.Has(LibrarianOnboarding.Accepted)
            && !LibrarianOnboarding.Has(LibrarianOnboarding.Completed) && !LibrarianOnboarding.Has(LibrarianOnboarding.CompactDecision), "all four onboarding receipts reset");
        Check(JsonSerializer.Serialize(LibrarianPreferences050.Current) == JsonSerializer.Serialize(LibrarianPreferences050.CreateDefaults()), "all local preferences restored to defaults");
        Check(LibrarianLanguage.Selected == LibrarianSettings041.DefaultLanguage, "mod language returns to native initial language");
        Check(!File.Exists(LibrarianNoticeHistory051.FilePath) && !File.Exists(LibrarianArchitectReviewHistory102.FilePath), "notice and review history files removed");
        Check(!LibrarianNoticeHistory051.IsAcknowledged("reset-audit-old-version"), "old update session receipt cleared");
        Check(LibrarianOnboarding.NeedsTutorial(stats?.TotalWins ?? 0, stats?.TotalLosses ?? 0, LibrarianOnboarding.Has(LibrarianOnboarding.Decision), stats?.Playtime ?? 0), "first-use tutorial becomes eligible again");
    }
    private static async Task ResetThroughUi(bool confirm, string language, bool keyboard = false)
    {
        string shotSuffix = language + (keyboard ? "-keyboard" : "") + "-" + (++_resetUiCount);
        LibrarianLanguage.Select(language); SuspendNotices();
        var button = await OpenEntry("diagnostics", "reset_mod");
        var before = RecordHashes();
        NGenericPopup? competing = null;
        var settings = Descendants(Game).OfType<RitsuModSettingsSubmenu>().Single(n => n.IsVisibleInTree());
        var settingsStack = (NSubmenuStack)settings.GetParent();
        var onSettingsClosed = Callable.From(() =>
        {
            if (_resetUiCount != 1 || settingsStack.SubmenusOpen || competing is not null || NModalContainer.Instance!.OpenModal is not null) return;
            competing = NGenericPopup.Create()!;
            competing.Name = "LibrarianResetAuditCompetingNotice";
            NModalContainer.Instance.Add(competing);
            var competingPanel = competing.GetNode<NVerticalPopup>("VerticalPopup");
            competingPanel.SetText("菜单弹窗竞争检查", "教学重置请求应等待这个菜单提示关闭。");
            competingPanel.HideNoButton(); competingPanel.YesButton.SetText("继续检查"); competingPanel.YesButton.Enable();
            competingPanel.YesButton.Connect("Released", Callable.From<GodotObject>(_ => NModalContainer.Instance!.Clear()));
        });
        if (_resetUiCount == 1) settingsStack.Connect(NSubmenuStack.SignalName.StackModified, onSettingsClosed);
        if (keyboard)
        {
            button.GrabFocus();
            await Wait();
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = true });
            await Wait(.2);
            if (!LibrarianDebugReset.IsBusy)
            {
                _keyboardMouseFallback = true;
                await Click(button);
            }
            await Until(() => NModalContainer.Instance?.OpenModal is Node held && held.Name == "LibrarianDebugResetConfirmation", "held Enter request opens confirmation", 5);
            Check(Input.IsKeyPressed(Key.Enter) && !LibrarianDebugReset.ConfirmationArmed
                && !((Node)NModalContainer.Instance!.OpenModal!).GetNode<NVerticalPopup>("VerticalPopup").YesButton.IsEnabled, "held raw Enter cannot arm or confirm reset");
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = false });
            await Wait(.3);
        }
        else await Click(button);
        if (_resetUiCount == 1)
        {
            settingsStack.Disconnect(NSubmenuStack.SignalName.StackModified, onSettingsClosed);
            await Until(() => competing is not null && ReferenceEquals(NModalContainer.Instance!.OpenModal, competing), "another real native modal occupies the closing settings slot", 5);
            await Wait(.4);
            Check(LibrarianDebugReset.IsBusy && !LibrarianDebugReset.ConfirmationArmed, "reset queues behind competing native modal without orphan Ready wait");
            CheckHashes(before, RecordHashes(), "queued reset writes no progress or local settings");
            await Capture("reset-queued-behind-native-notice");
            await Click(competing!.GetNode<NVerticalPopup>("VerticalPopup").YesButton);
        }
        await Until(() => NModalContainer.Instance?.OpenModal is Node node && node.Name == "LibrarianDebugResetConfirmation", "native reset confirmation remains after trigger input", 5);
        CheckHashes(before, RecordHashes(), "request alone changes no saved data");
        Check(LibrarianDebugReset.IsBusy, "reset awaits explicit confirmation");
        var panel = ((Node)NModalContainer.Instance!.OpenModal!).GetNode<NVerticalPopup>("VerticalPopup");
        await Capture("reset-confirm-" + shotSuffix);
        await Until(() => panel.YesButton.IsEnabled, "confirm enables after trigger release", 5);
        await Click(confirm ? panel.YesButton : panel.NoButton);
        await Until(() => !LibrarianDebugReset.IsBusy, "reset choice completes", 30);
        if (confirm)
        {
            Check(LibrarianDebugReset.LastSucceeded && LibrarianDebugReset.LastCommitOutcome == "CommittedVerified", "confirmed reset uses verified public conditional commit");
            await Until(() => NModalContainer.Instance?.OpenModal is Node result && result.Name == "LibrarianDebugResetResult", "reset reports visible native result", 5);
            await Until(() => ((Node)NModalContainer.Instance!.OpenModal!).GetNode<NVerticalPopup>("VerticalPopup").YesButton.IsEnabled, "result requires a separate released input", 5);
            await Capture("reset-result-" + shotSuffix);
            var resultPopup = (Node)NModalContainer.Instance!.OpenModal!;
            await Click(resultPopup.GetNode<NVerticalPopup>("VerticalPopup").YesButton);
            await Until(() => !ReferenceEquals(NModalContainer.Instance!.OpenModal, resultPopup), "reset result closes after explicit click", 5);
            Check(!GodotObject.IsInstanceValid(resultPopup) || !resultPopup.IsInsideTree(), "native result popup actually leaves the tree");
            // Reset deliberately re-enables the first-use update notice. It may
            // claim the now-free slot; only suppress that unrelated UI in the audit.
            SuspendNotices(); NModalContainer.Instance!.Clear(); await Wait(.4);
        }
        else CheckHashes(before, RecordHashes(), "cancel preserves all data bytes");
    }
    private static async Task<LibrarianPracticeSession> PracticeThroughUi(string suffix, Dictionary<string, string> saved)
    {
        SuspendNotices(); NModalContainer.Instance!.Clear();
        var button = await OpenEntry("display", "character_tutorial");
        await Capture("settings-before-entry-" + suffix); await Click(button);
        await Until(() => LibrarianPracticeSession.Active && CombatManager.Instance.IsInProgress
            && NModalContainer.Instance?.OpenModal is LibrarianPracticeOverlay, "actual Ritsu settings enters native teaching battle", 35);
        var session = LibrarianPracticeSession.Instance!;
        Check(Game.MainMenu is null && session.Player.Character is LibrarianCharacter && !RunManager.Instance.ShouldSave, "settings entry reaches unsaved native Librarian run");
        Check(!Descendants(Game).OfType<RitsuModSettingsSubmenu>().Any(n => n.IsVisibleInTree()), "actual Ritsu overlay closes before teaching interaction");
        await Capture("settings-entry-combat-" + suffix);
        var lesson = (LibrarianPracticeOverlay)NModalContainer.Instance!.OpenModal!;
        await Click(lesson.ContinueButton);
        await Until(() => session.AwaitingAction && NModalContainer.Instance!.OpenModal is null, "lesson opens action phase");
        var hint = Descendants(session).OfType<LibrarianPracticeTaskHint>().Single();
        await Until(() => hint.NativeGoldGuidanceActive, "native golden card guidance appears", 5);
        var cardNode = NPlayerHand.Instance!.GetCardHolder(session.CurrentCard!)!.CardNode;
        Check(session.CurrentCard!.CanPlay(out _, out _) && !cardNode.GetNode<TextureRect>("%UnplayableEnergyIcon").Visible,
            "guided target is natively playable and has no stale prohibited cost icon");
        Check(ReferenceEquals(hint.GuidedCardNode, cardNode) && ReferenceEquals(hint.GuidedNativeHighlight, cardNode.CardHighlight)
            && cardNode.CardHighlight.Modulate.IsEqualApprox(NCardHighlight.gold) && hint.NativeGoldGuidanceWidth > 0, "guidance reuses target card original native gold shader");
        // Ritsu's update toast is an eight-second Always-process notification.
        // Keep the cursor off it and let its native lifecycle finish for evidence.
        Input.WarpMouse(new Vector2(900, 900));
        await Wait(9);
        await Capture("native-gold-guidance-" + suffix);
        var action = new PlayCardAction(session.CurrentCard!, null);
        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
        await Until(() => action.CompletionTask.IsCompleted && session.Step == 1 && NModalContainer.Instance!.OpenModal is LibrarianPracticeOverlay, "guided card truly plays");
        Check(action.Exception is null && hint.GuidedNativeHighlight is null, "submitted card leaves no tutorial glow in play pile");
        await Click(((LibrarianPracticeOverlay)NModalContainer.Instance!.OpenModal!).ExitButton);
        await Until(() => !LibrarianPracticeSession.Active && Game.MainMenu is { } m && m.IsVisibleInTree(), "real teaching exit restores visible native menu");
        await Until(() => Game.Transition.Material is ShaderMaterial transition
            && transition.GetShaderParameter("threshold").AsSingle() <= .000001f
            && Game.Transition.GetNode<Control>("SimpleTransition").Modulate.A <= .000001f
            && Game.Transition.GetNode<Control>("GradientTransition").Modulate.A <= .000001f
            && (!NModalContainer.Instance!.GetNode<ColorRect>("Backstop").Visible
                || NModalContainer.Instance.GetNode<ColorRect>("Backstop").Color.A <= .000001f), "native menu fade and modal backstop become fully transparent", 8);
        Check(true, "teaching exit leaves no black transition or modal dim layer");
        Check(!RunManager.Instance.IsInProgress, "teaching exit clears native run");
        CheckHashes(saved, RunHashes(), "ordinary run files protected through real settings lesson");
        await Capture("settings-entry-return-menu-" + suffix);
        return session;
    }
    private static async Task Click(Control button)
    {
        var point = button.GetViewport().GetStretchTransform() * button.GetGlobalTransformWithCanvas() * (button.Size * .5f);
        Input.WarpMouse(point);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = point, GlobalPosition = point, Relative = new Vector2(10, 0) });
        await Wait();
        foreach (bool pressed in new[] { true, false })
            Input.ParseInputEvent(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = pressed });
        await Wait();
    }
    private static void CloseRitsuSettings()
    {
        foreach (var submenu in Descendants(Game).OfType<RitsuModSettingsSubmenu>().Where(n => n.IsVisibleInTree()).ToArray())
            if (submenu.GetParent() is NSubmenuStack stack)
                while (stack.Peek() is not null) stack.Pop();
    }
    internal static async Task<Control> OpenEntry(string section, string entry)
    {
        var opened = await ModSettingsNavigator.OpenByIdsAsync("Librarian", LibrarianSettings041.PageId,
            sectionId: section, entryId: entry, options: new ModSettingsOpenOptions { Highlight = false, Focus = true });
        if (!opened.Success) throw new InvalidOperationException("Could not open Ritsu entry: " + entry);
        await Wait(.6);
        var submenu = Descendants(Game).OfType<RitsuModSettingsSubmenu>().Last(n => n.IsVisibleInTree());
        var resolve = submenu.GetType().GetMethod("TryFindEntryAnchorOnSelectedPage", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("Ritsu entry anchors unavailable");
        object?[] args = [section, entry, null];
        if (resolve.Invoke(submenu, args) is not true || args[2] is not Control anchor)
            throw new InvalidOperationException("Ritsu entry anchor unavailable: " + entry);
        var button = Descendants(anchor).OfType<Control>().Single(n => n.GetType().Name == "ModSettingsTextButton");
        if (!button.IsVisibleInTree()) throw new InvalidOperationException("Ritsu button is hidden");
        MainFile.Logger.Info("MOD_UI_REAL_BUTTON entry=" + entry + " type=" + button.GetType().FullName);
        return button;
    }
    internal static async Task Run()
    {
        if (!OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase)
            || System.Environment.GetEnvironmentVariable("LIBRARIAN_RUNTIME_AUDIT") != "1")
            throw new InvalidOperationException("Reset audit needs isolated profile");
        _output = System.Environment.GetEnvironmentVariable("LIBRARIAN_MOD_RESET_OUTPUT") ?? throw new InvalidOperationException("Output required");
        if (!Path.IsPathFullyQualified(_output)) throw new InvalidOperationException("Absolute output required");
        Directory.CreateDirectory(_output);
        try
        {
            await Wait(3);
            SuspendNotices();
            NModalContainer.Instance!.Clear(); await Wait();
            string mode = System.Environment.GetEnvironmentVariable("LIBRARIAN_MOD_RESET_AUDIT")!;
            if (mode == "status")
            {
                await Game.StartNewSingleplayerRun(ModelDb.Character<LibrarianCharacter>(), true, ActModel.GetDefaultList(), [], "LIBRARIAN_STATUS_PROBE", GameMode.Standard);
                await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<TunnelerWeak>().ToMutable());
                await Wait(2);
                var probeButton = await OpenEntry("diagnostics", "reset_mod");
                await Click(probeButton); await Wait(2);
                await Capture("reset-rejection-status-probe");
                var text = Descendants(Game).OfType<RichTextLabel>().Where(label => label.IsVisibleInTree()).Select(label => label.GetParsedText())
                    .Concat(Descendants(Game).OfType<Label>().Where(label => label.IsVisibleInTree()).Select(label => label.Text))
                    .Where(value => value.Contains("初始化") || value.Contains("主菜单") || value.Contains("reset", StringComparison.OrdinalIgnoreCase)).Distinct().ToArray();
                MainFile.Logger.Info("MOD_RESET_VISIBLE_STATUS " + JsonSerializer.Serialize(new { expected = LibrarianDebugReset.Status, visible = text }));
                Check(!string.IsNullOrEmpty(LibrarianDebugReset.Status) && text.Contains(LibrarianDebugReset.Status), "actual settings label renders current reset rejection status");
                MainFile.Logger.Info("MOD_RESET_STATUS_PROBE_PASS");
                return;
            }
            if (mode == "read")
            {
                await CheckResetState(false);
                Check(JsonNode.Parse((await Snapshot()).RawJson)?["foreign_reset_fixture_extension"]?["keep"]?.GetValue<string>() == "unchanged", "foreign JSON survives independent process restart");
                await Capture("reset-persistent-menu");
                MainFile.Logger.Info($"MOD_RESET_RESTART_AUDIT_PASS checks={_checks} firstUse=True initialProgressiveBaseline=True");
                return;
            }
            if (mode != "baseline")
            {
                var originalRuns = RunHashes();
                Check(originalRuns["current_run.save"] != "absent" && originalRuns["current_run.save.backup"] != "absent", "ordinary singleplayer run main and backup protection inputs exist");
                Check(RecordHashes().Take(4).All(row => row.Value != "absent"), "all four native and Ritsu progress protection inputs exist");
                await PracticeThroughUi("cold", originalRuns);
                await SeedRecords();
                await CheckPreservationPreflight();
                var history = SeedHistories();
                await ResetThroughUi(false, "zhs");
                await ResetThroughUi(false, "eng", keyboard: true);
                foreach (var row in history) Check(Hash(row.Key) == row.Value.hash, "cancel retains exclusive and mixed history");
                await ResetThroughUi(true, "zhs");
                Check(LibrarianDebugReset.LastSucceeded && !LibrarianDebugReset.LastCanceled, "explicit UI confirm completes reset");
                await CheckResetState(true);
                foreach (var row in history) Check(row.Value.owned ? !File.Exists(row.Key) : Hash(row.Key) == row.Value.hash, "reset removes only exclusive Librarian history and backup");
                Check(LibrarianDebugReset.LastDeletedHistories >= 2, "exclusive history main and backup deletion exercised");
                CheckHashes(originalRuns, RunHashes(), "reset preserves ordinary single and multiplayer saves");
                await OpenEntry("progression", "progressive");
                Check(LibrarianSettings041.ProgressText().Contains("1/7") && Descendants(Game).OfType<RichTextLabel>()
                    .Any(label => label.IsVisibleInTree() && label.GetParsedText() == LibrarianSettings041.ProgressText()), "cached settings page renders restored initial chapter progress");
                await Capture("reset-default-settings");
                CloseRitsuSettings(); await Wait();
                await PracticeThroughUi("repeat", originalRuns);
                await OrdinarySaveReloadAndGate();
                SuspendNotices(); NModalContainer.Instance!.Clear();
                await ResetThroughUi(true, "eng");
                await CheckResetState(false);
                SaveManager.Instance.SaveProgressFile(); LibrarianDebugReset.VerifyProgressFiles();
                File.WriteAllText(Path.Combine(_output, "reset-audit.json"), JsonSerializer.Serialize(new
                {
                    checks = _checks, screenshots = _shots, realSettingsMouseEntries = 2, canceledConfirmationChoices = 2,
                    rawEnterHeldAtRequest = true, keyboardOnlySettingsEntry = !_keyboardMouseFallback, confirmedResets = 3, nativeGoldShader = true,
                    exclusiveHistoryDeletion = true, mixedHistoryPreserved = true, foreignUnknownJsonPreserved = true,
                    progressMirrorMainAndBackupReadback = true, ordinarySaveReload = true, inRunResetRejected = true
                }, new JsonSerializerOptions { WriteIndented = true }));
                MainFile.Logger.Info($"MOD_UI_ENTRY_AUDIT_PASS checks={_checks} realButtons=2 nativeGold=True normalSaveReload=True");
                MainFile.Logger.Info("MOD_RESET_AUDIT_PASS cancelBytes=True recordsReset=True fourProgressFiles=True foreignJson=True normalRunPreserved=True");
                MainFile.Logger.Info("MOD_RESET_RESTART_WRITE_PASS firstUse=True isolatedProfile=True");
                return;
            }
            var button = await OpenEntry("display", "character_tutorial");
            await Capture("settings-before-entry");
            await Click(button);
            ulong deadline = Time.GetTicksMsec() + 35000;
            while (Time.GetTicksMsec() < deadline && !(LibrarianPracticeSession.Active
                && CombatManager.Instance.IsInProgress && NModalContainer.Instance?.OpenModal is LibrarianPracticeOverlay)) await Wait(.1);
            bool entered = LibrarianPracticeSession.Active && CombatManager.Instance.IsInProgress
                && NModalContainer.Instance?.OpenModal is LibrarianPracticeOverlay;
            await Capture(entered ? "settings-entry-combat" : "settings-entry-black-screen");
            var evidence = new { entered, active = LibrarianPracticeSession.Active,
                combat = CombatManager.Instance.IsInProgress, mainMenu = Game.MainMenu is not null,
                run = RunManager.Instance.IsInProgress, modal = NModalContainer.Instance?.OpenModal?.GetType().Name,
                actualRitsuMouseClick = true };
            File.WriteAllText(Path.Combine(_output, "entry-result.json"), JsonSerializer.Serialize(evidence));
            MainFile.Logger.Info("MOD_UI_ENTRY_RESULT " + JsonSerializer.Serialize(evidence));
            if (System.Environment.GetEnvironmentVariable("LIBRARIAN_MOD_RESET_AUDIT") == "baseline")
                MainFile.Logger.Info(entered ? "MOD_UI_ENTRY_BASELINE_ENTERED" : "MOD_UI_ENTRY_BLACK_SCREEN_REPRODUCED");
            else if (!entered) throw new TimeoutException("Actual Ritsu settings button cannot enter lesson");
            if (entered) await LibrarianPracticeSession.Instance!.ExitAsync();
        }
        finally { await Wait(3); Game.Quit(); }
    }

    private static Dictionary<string, (string hash, bool owned)> SeedHistories()
    {
        var manager = typeof(SaveManager).GetField("_runHistorySaveManager", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(SaveManager.Instance)!;
        var store = (ISaveStore)typeof(RunHistorySaveManager).GetField("_saveStore", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        string folder = ProjectSettings.GlobalizePath(store.GetFullPath(RunHistorySaveManager.GetHistoryPath(SaveManager.Instance.CurrentProfileId)));
        Check(Path.GetFullPath(folder).StartsWith(Path.GetFullPath(OS.GetUserDataDir()) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "native history fixtures resolve inside isolated user data");
        Directory.CreateDirectory(folder);
        string character = ModelDb.Character<LibrarianCharacter>().Id.ToString(), vanilla = ModelDb.Character<Ironclad>().Id.ToString();
        var result = new Dictionary<string, (string, bool)>();
        foreach (var (name, chars, owned) in new[]
        {
            ("audit_reset_owned.run", new[] { character }, true), ("audit_reset_owned.run.backup", new[] { character }, true),
            ("audit_reset_mixed.run", new[] { character, vanilla }, false), ("audit_reset_vanilla.run", new[] { vanilla }, false)
        })
        {
            string path = Path.Combine(folder, name);
            Check(!File.Exists(path), "exclusive test history fixture name is unused");
            File.WriteAllText(path, new JsonObject { ["players"] = new JsonArray(chars.Select(id => (JsonNode)new JsonObject { ["character"] = id }).ToArray()) }.ToJsonString());
            result[path] = (Hash(path), owned);
        }
        return result;
    }
    private static async Task OrdinarySaveReloadAndGate()
    {
        SuspendNotices(); NModalContainer.Instance!.Clear();
        var run = await Game.StartNewSingleplayerRun(ModelDb.Character<LibrarianCharacter>(), true,
            ActModel.GetDefaultList(), [], "LIBRARIAN_RESET_ORDINARY", GameMode.Standard);
        var player = run.Players.Single();
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<TunnelerWeak>().ToMutable());
        await Until(() => CombatManager.Instance.IsInProgress && player.PlayerCombatState?.Hand.Cards.Count > 0, "ordinary fixture battle ready");
        await SaveManager.Instance.SaveRun(null);
        var saved = SaveManager.Instance.LoadRunSave();
        Check(saved.Success, "ordinary native run save readable");
        var restored = RunState.FromSerializable(saved.SaveData!);
        var button = await OpenEntry("diagnostics", "reset_mod");
        var records = RecordHashes(); var runs = RunHashes();
        await Click(button); await Wait(.3);
        await Until(() => LibrarianDebugReset.Status == LibrarianLanguage.Text("main_menu_ui", "LIBRARIAN_SETTINGS.reset_all_menu_required"), "actual in-run settings request reports menu requirement", 5);
        await Until(() => Descendants(Game).OfType<RichTextLabel>().Any(label => label.IsVisibleInTree() && label.GetParsedText() == LibrarianDebugReset.Status)
            || Descendants(Game).OfType<Label>().Any(label => label.IsVisibleInTree() && label.Text == LibrarianDebugReset.Status), "Ritsu status row actually renders in-run rejection reason", 5);
        Check(!LibrarianDebugReset.IsBusy && NModalContainer.Instance!.OpenModal is null && RunManager.Instance.IsInProgress, "reset button refuses during an active ordinary run");
        CheckHashes(records, RecordHashes(), "in-run reset rejection preserves records");
        CheckHashes(runs, RunHashes(), "in-run reset rejection preserves run save");
        await Capture("reset-rejected-during-run");
        CloseRitsuSettings(); await Wait();
        await Game.ReturnToMainMenu(); await Wait(.6); SuspendNotices(); NModalContainer.Instance!.Clear();
        await ResetThroughUi(true, "zhs");
        CheckHashes(runs, RunHashes(), "confirmed reset keeps readable existing run bytes");
        // Native FTUE remains enabled by user preference; only suppress it in this
        // controlled save reload so unrelated vanilla teaching does not block the probe.
        bool ftues = SaveManager.Instance.Progress.EnableFtues;
        SaveManager.Instance.Progress.EnableFtues = false;
        CloseRitsuSettings(); await Wait();
        await RunManager.Instance.SetUpSavedSingleplayer(restored, saved.SaveData!);
        await Game.LoadRun(restored, saved.SaveData!.PreFinishedRoom); await Game.Transition.FadeIn();
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<TunnelerWeak>().ToMutable());
        await Until(() => CombatManager.Instance.IsInProgress && restored.Players.Single().PlayerCombatState?.Hand.Cards.Count > 0, "ordinary saved run reloads actual combat");
        Check(restored.Players.Single().Character is LibrarianCharacter && restored.Rng.StringSeed == "LIBRARIAN_RESET_ORDINARY", "reset leaves ordinary run character and seed intact");
        await Capture("ordinary-combat-after-reset-reload");
        await Game.ReturnToMainMenu(); await Wait(.5);
        SaveManager.Instance.Progress.EnableFtues = ftues;
    }
}
