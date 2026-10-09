using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Godot;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using STS2RitsuLib.Saves.RawProgress;
using STS2RitsuLib.Settings;

namespace Librarian.Mechanics;

/// <summary>Explicit, confirmed reset of this mod's current-profile records and local preferences.</summary>
internal static class LibrarianDebugReset
{
    private static bool _requestOpen, _applying;
    private static string _statusKey = "";
    internal static bool IsBusy => _requestOpen || _applying;
    internal static bool Applying => _applying;
    internal static bool CanReset => NGame.Instance?.MainMenu is { } menu && menu.IsVisibleInTree()
        && !RunManager.Instance.IsInProgress && !LibrarianPracticeSession.Active;
    internal static int LastDeletedHistories { get; private set; }
    internal static string LastCommitOutcome { get; private set; } = "";
    internal static bool LastSucceeded { get; private set; }
    internal static bool LastCanceled { get; private set; }
    internal static bool ConfirmationArmed { get; private set; }
    internal static string Status => _statusKey.Length == 0 ? "" : Text(_statusKey);
    internal static string[] LocalRecordPaths =>
        [LibrarianPreferences050.FilePath, LibrarianLanguage.FilePath, LibrarianNoticeHistory051.FilePath, LibrarianArchitectReviewHistory102.FilePath];
    private static string Text(string key) => LibrarianLanguage.Text("main_menu_ui", "LIBRARIAN_SETTINGS." + key);
    private static LocString Loc(string key) => new("main_menu_ui", "LIBRARIAN_SETTINGS." + key);

    internal static async Task RequestAsync(IModSettingsUiActionHost host)
    {
        if (IsBusy) return;
        if (!CanReset) { _statusKey = "reset_all_menu_required"; host.RequestRefreshAfterDataModelBatchChange(); return; }
        var modals = NModalContainer.Instance;
        if (modals is null || modals.OpenModal is not null) return;
        _requestOpen = true;
        LastSucceeded = LastCanceled = false;
        bool settingsClosed = false;
        try
        {
            int profile = SaveManager.Instance.CurrentProfileId;
            var progress = SaveManager.Instance.Progress;
            // Let the settings entry's pressed handler finish before hiding its owner.
            var tree = NGame.Instance!.GetTree();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            settingsClosed = CloseSettings(tree.Root);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (!CanReset || SaveManager.Instance.CurrentProfileId != profile
                || !ReferenceEquals(SaveManager.Instance.Progress, progress)
                || Descendants(tree.Root).OfType<RitsuModSettingsSubmenu>().Any(screen => screen.IsVisibleInTree()))
                throw new InvalidOperationException("The settings overlay did not return to the idle main menu.");
            while (modals.OpenModal is not null)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                if (!CanReset || SaveManager.Instance.CurrentProfileId != profile
                    || !ReferenceEquals(SaveManager.Instance.Progress, progress)
                    || !ReferenceEquals(NModalContainer.Instance, modals))
                    throw new InvalidOperationException("The active profile or game state changed while waiting for confirmation.");
            }
            if (!await ConfirmAsync(modals)) { LastCanceled = true; _statusKey = "reset_all_canceled"; return; }
            if (!CanReset || SaveManager.Instance.CurrentProfileId != profile
                || !ReferenceEquals(SaveManager.Instance.Progress, progress))
                throw new InvalidOperationException("The active profile or game state changed during confirmation.");
            _statusKey = "reset_all_working";
            if (!settingsClosed) host.RequestRefreshAfterDataModelBatchChange();
            await ApplyAsync();
            _statusKey = "reset_all_success";
            ShowResult();
        }
        catch (Exception error)
        {
            _statusKey = "reset_all_failed";
            MainFile.Logger.Warn("Librarian reset did not complete: " + error.Message);
            ShowResult();
        }
        finally { _requestOpen = false; if (!settingsClosed) host.RequestRefreshAfterDataModelBatchChange(); }
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    internal static bool CloseSettings(Node root)
    {
        bool closed = false;
        foreach (var screen in Descendants(root).OfType<RitsuModSettingsSubmenu>().Where(screen => screen.IsVisibleInTree()).ToArray())
        {
            Node? parent = screen.GetParent();
            while (parent is not null && parent is not NSubmenuStack) parent = parent.GetParent();
            if (parent is not NSubmenuStack stack) throw new InvalidOperationException("The native settings stack is unavailable.");
            while (GodotObject.IsInstanceValid(stack) && stack.SubmenusOpen) { stack.Pop(); closed = true; }
        }
        return closed;
    }
    private static void ShowResult()
    {
        if (NModalContainer.Instance is not { } modals || !CanReset) return;
        string status = _statusKey;
        int profile = SaveManager.Instance.CurrentProfileId;
        var progress = SaveManager.Instance.Progress;
        var menu = NGame.Instance!.MainMenu;
        var tree = NGame.Instance.GetTree();
        MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(ConfigureResult());
        async Task ConfigureResult()
        {
            bool Current() => CanReset && SaveManager.Instance.CurrentProfileId == profile
                && ReferenceEquals(SaveManager.Instance.Progress, progress)
                && ReferenceEquals(NGame.Instance?.MainMenu, menu)
                && ReferenceEquals(NModalContainer.Instance, modals);
            while (modals.OpenModal is not null)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                if (!Current()) return;
            }
            if (!Current()) return;
            var popup = NGenericPopup.Create();
            if (popup is null) return;
            popup.Name = "LibrarianDebugResetResult";
            modals.Add(popup);
            if (!ReferenceEquals(modals.OpenModal, popup)) { popup.QueueFree(); return; }
            if (!popup.IsNodeReady()) await popup.ToSignal(popup, Node.SignalName.Ready);
            if (!GodotObject.IsInstanceValid(popup) || !popup.IsInsideTree()) return;
            var panel = popup.GetNode<NVerticalPopup>("VerticalPopup");
            panel.SetText(Text("reset_all"), Text(status));
            panel.YesButton.SetText(LibrarianOnboarding.Text("done"));
            panel.YesButton.Disable(); panel.HideNoButton();
            do
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                if (!GodotObject.IsInstanceValid(popup) || !popup.IsInsideTree()) return;
            } while (TriggerHeld());
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (!GodotObject.IsInstanceValid(popup) || !popup.IsInsideTree()) return;
            _ = popup.WaitForConfirmation(Loc(status), Loc("reset_all"), null,
                new LocString("main_menu_ui", "LIBRARIAN_TUTORIAL.done"));
            panel.YesButton.Enable();
        }
    }

    private static bool TriggerHeld() => Input.IsMouseButtonPressed(MouseButton.Left)
        || Input.IsKeyPressed(Key.Enter) || Input.IsKeyPressed(Key.KpEnter) || Input.IsKeyPressed(Key.Space)
        || new string[] { MegaInput.select, "ui_accept" }.Any(key => InputMap.HasAction(key) && Input.IsActionPressed(key));

    private static async Task<bool> ConfirmAsync(NModalContainer modals)
    {
        if (modals.OpenModal is not null) throw new InvalidOperationException("The native confirmation slot is occupied.");
        var popup = NGenericPopup.Create() ?? throw new InvalidOperationException("Native confirmation unavailable.");
        popup.Name = "LibrarianDebugResetConfirmation";
        modals.Add(popup);
        if (!ReferenceEquals(modals.OpenModal, popup))
        {
            popup.QueueFree();
            throw new InvalidOperationException("The native confirmation could not be opened.");
        }
        var closed = new TaskCompletionSource<bool>();
        popup.TreeExited += () => closed.TrySetResult(false);
        if (!popup.IsNodeReady()) await popup.ToSignal(popup, Node.SignalName.Ready);
        var panel = popup.GetNode<NVerticalPopup>("VerticalPopup");
        panel.SetText(Text("reset_all_confirm_title"), Text("reset_all_confirm_body"));
        panel.YesButton.SetText(Text("reset_all_confirm"));
        panel.NoButton.SetText(Text("reset_all_cancel"));
        panel.YesButton.IsYes = true; panel.NoButton.IsYes = false;
        panel.YesButton.Show(); panel.NoButton.Show();
        panel.NoButton.Enable();
        panel.YesButton.Disable();
        ConfirmationArmed = false;
        var hotkeys = NHotkeyManager.Instance;
        string[] cancelKeys = [MegaInput.cancel, MegaInput.pauseAndBack, MegaInput.back];
        void IgnorePress() { }
        void Cancel()
        {
            closed.TrySetResult(false);
            if (ReferenceEquals(modals.OpenModal, popup)) modals.Clear();
        }
        void CancelButton(NButton _) => Cancel();
        panel.NoButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(CancelButton));
        foreach (string key in cancelKeys)
        {
            hotkeys?.PushHotkeyPressedBinding(key, IgnorePress);
            hotkeys?.PushHotkeyReleasedBinding(key, Cancel);
        }
        try
        {
            // Ritsu entries activate on press. Do not register the native confirmation
            // callback until that press is released and a subsequent frame has passed.
            // This also covers a settings entry opened by keyboard/controller input.
            var tree = popup.GetTree();
            ulong started = Time.GetTicksMsec();
            while (!closed.Task.IsCompleted)
            {
                int seconds = Math.Max(0, (int)Math.Ceiling(5 - (Time.GetTicksMsec() - started) / 1000d));
                panel.YesButton.SetText(Text("reset_all_confirm") + (seconds > 0 ? " (" + seconds + ")" : ""));
                if (panel.GetNodeOrNull<MegaCrit.Sts2.addons.mega_text.MegaRichTextLabel>("Description") is { } body)
                {
                    body.Text = Text("reset_all_confirm_body") + "\n\n[color=#ff6565]"
                        + LibrarianSettings041.PlainText(Text("reset_all_warning")) + "[/color]";
                }
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                if (closed.Task.IsCompleted) return false;
                if (seconds > 0 || TriggerHeld()) continue;
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                if (closed.Task.IsCompleted) return false;
                if (!TriggerHeld()) break;
            }
            if (closed.Task.IsCompleted) return false;
            panel.NoButton.Disconnect(NClickableControl.SignalName.Released, Callable.From<NButton>(CancelButton));
            var choice = popup.WaitForConfirmation(Loc("reset_all_confirm_body"), Loc("reset_all_confirm_title"),
                Loc("reset_all_cancel"), Loc("reset_all_confirm"));
            if (panel.GetNodeOrNull<MegaCrit.Sts2.addons.mega_text.MegaRichTextLabel>("Description") is { } warningBody)
                warningBody.Text = Text("reset_all_confirm_body") + "\n\n[color=#ff6565]"
                    + LibrarianSettings041.PlainText(Text("reset_all_warning")) + "[/color]";
            panel.YesButton.Enable();
            ConfirmationArmed = true;
            var finished = await Task.WhenAny(choice, closed.Task);
            bool confirmed = ReferenceEquals(finished, choice) && await choice;
            if (GodotObject.IsInstanceValid(popup) && popup.IsInsideTree())
                await closed.Task;
            return confirmed;
        }
        finally
        {
            ConfirmationArmed = false;
            foreach (string key in cancelKeys)
            {
                hotkeys?.RemoveHotkeyPressedBinding(key, IgnorePress);
                hotkeys?.RemoveHotkeyReleasedBinding(key, Cancel);
            }
        }
    }

    /// <summary>Only call after confirmation, or from an explicitly isolated development audit.</summary>
    internal static async Task ApplyAsync()
    {
        if (_applying || !CanReset) throw new InvalidOperationException("Reset requires an idle main menu.");
        _applying = true;
        LastDeletedHistories = 0; LastCommitOutcome = ""; LastSucceeded = false;
        try
        {
            var bridge = RawProgressBridge.Instance;
            var descriptor = bridge.Describe();
            var capture = await bridge.CaptureAsync();
            var snapshot = capture.Snapshot;
            if (capture.Outcome != RawProgressReadOutcome.Succeeded || snapshot is null || !snapshot.Generation.IsModded)
                throw new InvalidOperationException("Modded progress could not be captured: " + capture.Outcome);
            if (!CanReset || SaveManager.Instance.CurrentProfileId != snapshot.Generation.ProfileId)
                throw new InvalidOperationException("The active profile changed before reset.");
            string proposed = SanitizeProgressJson(snapshot.RawJson);
            byte[] bytes = Encoding.UTF8.GetBytes(proposed);
            var history = PlanHistoryDeletions(snapshot.Generation.ProfileId);
            var request = new RawProgressCommitRequest
            {
                ProtocolVersion = descriptor.ProtocolVersion, SchemaVersion = snapshot.SchemaVersion,
                OwnerId = MainFile.ModId, TransactionId = Guid.NewGuid(), ExpectedGeneration = snapshot.Generation,
                ProposedRawJson = proposed, ProposedSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                ProposedUtf8Length = bytes.LongLength
            };
            VerifyRawPreservation(request);
            var result = await bridge.CommitAsync(request);
            LastCommitOutcome = result.Outcome.ToString();
            if (result.Outcome != RawProgressCommitOutcome.CommittedVerified)
                throw new InvalidOperationException("Progress commit was not verified: " + result.Outcome);
            // The public bridge replaces live progress and reinstalls unknown-property
            // preservation. A normal second save rotates the sanitized main files into
            // both local backups without sending backup files to the cloud.
            SaveManager.Instance.SaveProgressFile();
            VerifyProgressFiles(initial: false);
            // Match the same first-use progression that NMainMenu._Ready establishes:
            // one revealed opening chapter and six unopened progressive slots.
            // EnsureProgressiveProfile saves once; save again so backups match it too.
            LibrarianSettings041.EnsureProgressiveProfile();
            SaveManager.Instance.SaveProgressFile();
            VerifyProgressFiles();
            foreach (var item in history)
            {
                item.Store.DeleteFile(item.Path);
                if (item.Store.FileExists(item.Path)) throw new IOException("A Librarian history could not be removed.");
                LastDeletedHistories++;
            }
            foreach (string path in new[] { LibrarianNoticeHistory051.FilePath, LibrarianArchitectReviewHistory102.FilePath })
                if (File.Exists(path)) File.Delete(path);
            LibrarianUpdateNotice051.ResetSessionForAudit();
            LibrarianArchitectReviewHistory102.ForgetSessionForAudit();
            typeof(LibrarianArchitectReview102).GetField("_previewRequested", BindingFlags.Static | BindingFlags.NonPublic)!
                .SetValue(null, false);
            LibrarianSettings041.RestoreDefaults();
            if (!File.Exists(LibrarianPreferences050.FilePath) || !File.Exists(LibrarianLanguage.FilePath))
                throw new IOException("Default local settings could not be persisted.");
            var stored = LibrarianPreferences050.Deserialize(File.ReadAllText(LibrarianPreferences050.FilePath));
            var language = System.Text.Json.JsonSerializer.Deserialize<LibrarianLanguage.Preference>(File.ReadAllText(LibrarianLanguage.FilePath));
            if (System.Text.Json.JsonSerializer.Serialize(stored) != System.Text.Json.JsonSerializer.Serialize(LibrarianPreferences050.CreateDefaults())
                || LibrarianLanguage.Selected != LibrarianSettings041.DefaultLanguage
                || language?.Initialized != true || language.Language != LibrarianSettings041.DefaultLanguage)
                throw new IOException("Default local settings could not be verified.");
            var menu = NGame.Instance!.MainMenu!;
            LibrarianUnlocks040.RefreshMenuEntry(menu);
            LibrarianUpdateNotice051.Schedule(menu);
            LastSucceeded = true;
            MainFile.Logger.Info($"LIBRARIAN_DEBUG_RESET_PASS profile={snapshot.Generation.ProfileId} progressFiles=4 histories={LastDeletedHistories} vanillaAndOtherModsPreserved=True ordinaryRunsPreserved=True");
        }
        finally { _applying = false; }
    }

    /// <summary>Fixed Ritsu SDK read-only preparation; never binds the temporary progress to the active profile.</summary>
    internal static void VerifyRawPreservation(RawProgressCommitRequest request)
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .Single(candidate => candidate.GetType("STS2RitsuLib.Saves.RawProgress.RawProgressCommitBridge") is not null);
        var bridge = assembly.GetType("STS2RitsuLib.Saves.RawProgress.RawProgressCommitBridge", true)!;
        var validation = bridge.GetMethod("TryValidateProposedDocument", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException("Ritsu raw progress preparation is unavailable.");
        object?[] args = [request, null];
        if (validation.Invoke(null, args) is not true || args[1] is not { } prepared)
            throw new InvalidDataException("The proposed progress could not be validated before reset.");
        var type = prepared.GetType();
        var temporary = type.GetProperty("Progress")?.GetValue(prepared) as ProgressState
            ?? throw new InvalidDataException("The temporary progress projection is unavailable.");
        var known = type.GetProperty("KnownProjectionJson")?.GetValue(prepared) as string
            ?? throw new InvalidDataException("The known progress projection is unavailable.");
        var preservation = assembly.GetType("STS2RitsuLib.Saves.RawProgress.RawProgressJsonPreservation", true)!;
        var attach = preservation.GetMethod("TryAttach", BindingFlags.NonPublic | BindingFlags.Static,
            null, [typeof(ProgressState), typeof(string), typeof(string)], null)
            ?? throw new MissingMethodException("Ritsu unknown-property verification is unavailable.");
        bool accepted = attach.Invoke(null, [temporary, request.ProposedRawJson, known]) is true;
        if (!accepted) LibrarianProgressPreservation.Attach(temporary, request.ProposedRawJson, known, ref accepted, resetPreflight: true);
        if (!accepted)
        {
            MainFile.Logger.Warn("Librarian reset preflight rejected unsupported unknown progress fields; nothing was committed.");
            throw new InvalidDataException("Other progress fields cannot be safely preserved by the current Ritsu version.");
        }
    }

    internal static bool OwnsModelId(string? id)
    {
        if (id is null) return false;
        int separator = id.IndexOf('.');
        return separator > 0 && id[(separator + 1)..].StartsWith("LIBRARIAN-", StringComparison.Ordinal);
    }
    private static readonly HashSet<string> Receipts = new(StringComparer.Ordinal)
    {
        LibrarianOnboarding.Decision, LibrarianOnboarding.Accepted, LibrarianOnboarding.Completed,
        LibrarianOnboarding.CompactDecision, LibrarianUnlocks040.ChoiceMarker, LibrarianUnlocks040.AllMarker,
        "librarian_onboarding_audit_clone_only"
    };
    private static string? Value(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    private static void Remove(JsonArray? array, Func<JsonNode?, bool> owned)
    {
        if (array is null) return;
        for (int i = array.Count - 1; i >= 0; i--) if (owned(array[i])) array.RemoveAt(i);
    }
    internal static string SanitizeProgressJson(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("Progress must be a JSON object.");
        foreach (string key in new[] { "character_stats", "card_stats" })
            Remove(root[key] as JsonArray, row => OwnsModelId(Value(row?["id"])));
        foreach (var (key, children) in new[] { ("encounter_stats", "fight_stats"), ("enemy_stats", "fight_stats"), ("ancient_stats", "character_stats") })
        {
            var array = root[key] as JsonArray;
            string idKey = key switch { "encounter_stats" => "encounter_id", "enemy_stats" => "enemy_id", _ => "ancient_id" };
            Remove(array, row => OwnsModelId(Value(row?[idKey])));
            if (array is not null)
                foreach (var row in array) Remove(row?[children] as JsonArray, child => OwnsModelId(Value(child?["character"])));
        }
        foreach (string key in new[] { "discovered_cards", "discovered_relics", "discovered_potions", "discovered_events", "discovered_acts" })
            Remove(root[key] as JsonArray, item => OwnsModelId(Value(item)));
        Remove(root["epochs"] as JsonArray, row => Enumerable.Range(1, 7).Any(number => Value(row?["id"]) == LibrarianUnlocks040.Id(number)));
        Remove(root["ftue_completed"] as JsonArray, item => Value(item) is { } marker && Receipts.Contains(marker));
        if (OwnsModelId(Value(root["pending_character_unlock"]))) root["pending_character_unlock"] = "NONE.NONE";
        // Aggregate native counters, achievements, unknown JSON, other characters and
        // records with shared ownership have no reliable Librarian-only attribution.
        return root.ToJsonString();
    }

    internal static string MirrorPath
    {
        get
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("STS2RitsuLib.Saves.ProgressMirrorStore"))
                .OfType<Type>().Distinct().Single();
            return ProjectSettings.GlobalizePath((string)type.GetMethod("GetMirrorPath", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!);
        }
    }
    internal static bool IsInitialProgressJson(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject;
        if (root is null || root["epochs"] is not JsonArray epochs || root["ftue_completed"] is not JsonArray receipts)
            return false;
        bool OwnedEpoch(JsonNode? row) => Enumerable.Range(1, 7).Any(number => Value(row?["id"]) == LibrarianUnlocks040.Id(number));
        if (epochs.Count(OwnedEpoch) != 7 || receipts.Count(item => Value(item) == LibrarianUnlocks040.ChoiceMarker) != 1)
            return false;
        foreach (int number in Enumerable.Range(1, 7))
        {
            var rows = epochs.Where(row => Value(row?["id"]) == LibrarianUnlocks040.Id(number)).ToArray();
            if (rows.Length != 1 || rows[0] is not JsonObject { Count: 3 } row
                || Value(row["state"]) != (number == 1 ? "revealed" : "not_obtained")
                || row["obtain_date"] is not JsonValue date || !date.TryGetValue<long>(out long obtained)
                || (number == 1 ? obtained <= 0 : obtained != 0)) return false;
        }
        // Remove only the seven verified default slots and the default progressive
        // receipt. Any other owned statistic, discovery, receipt or unlock is rejected.
        Remove(epochs, OwnedEpoch);
        Remove(receipts, item => Value(item) == LibrarianUnlocks040.ChoiceMarker);
        return JsonNode.DeepEquals(root, JsonNode.Parse(SanitizeProgressJson(json)));
    }
    internal static void VerifyProgressFiles(bool initial = true)
    {
        string native = ProjectSettings.GlobalizePath(SaveManager.Instance.GetProfileScopedPath(Path.Combine("saves", "progress.save")));
        string mirror = MirrorPath;
        foreach (string path in new[] { native, native + ".backup", mirror, mirror + ".backup" })
        {
            if (!File.Exists(path)) throw new IOException("Reset progress evidence file is missing: " + Path.GetFileName(path));
            string json = File.ReadAllText(path);
            bool verified = initial ? IsInitialProgressJson(json)
                : JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(SanitizeProgressJson(json)));
            if (!verified) throw new InvalidDataException("Librarian reset progress could not be verified: " + Path.GetFileName(path));
        }
    }
    private sealed record HistoryDeletion(ISaveStore Store, string Path);
    private static List<HistoryDeletion> PlanHistoryDeletions(int profile)
    {
        var manager = typeof(SaveManager).GetField("_runHistorySaveManager", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(SaveManager.Instance)!;
        var store = (ISaveStore)typeof(RunHistorySaveManager).GetField("_saveStore", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(manager)!;
        string directory = RunHistorySaveManager.GetHistoryPath(profile);
        var plan = new List<HistoryDeletion>();
        if (!store.DirectoryExists(directory)) return plan;
        foreach (string name in store.GetFilesInDirectory(directory))
        {
            if (Path.GetFileName(name) != name || (!name.EndsWith(".run", StringComparison.Ordinal) && !name.EndsWith(".run.backup", StringComparison.Ordinal))) continue;
            string path = Path.Combine(directory, name);
            try
            {
                var json = JsonNode.Parse(store.ReadFile(path) ?? "");
                if (json?["players"] is JsonArray { Count: > 0 } players
                    && players.All(player => Value(player?["character"]) == ModelDb.Character<LibrarianCharacter>().Id.ToString()))
                    plan.Add(new(store, path));
            }
            catch (System.Text.Json.JsonException) { /* Unreadable or mixed-owner histories are preserved. */ }
            catch (InvalidOperationException) { /* Preserve malformed, unowned records. */ }
        }
        return plan;
    }
}
