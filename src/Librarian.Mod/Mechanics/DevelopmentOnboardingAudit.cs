using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using HarmonyLib;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.PowerCards;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Actions;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Saves.Validation;
using STS2RitsuLib.Settings;

namespace Librarian.Mechanics;

/// <summary>Opt-in real-engine onboarding audit; never runs against an ordinary player profile.</summary>
internal static class DevelopmentOnboardingAudit
{
    private static int _checks, _screenshots;
    private static int _explanationScreenshots, _taskScreenshots, _progressWriterProbes, _guiCardPlays;
    private static readonly List<object> _captureEvidence = new();
    private static readonly List<object> _progressEvidence = new();
    private static readonly List<object> _cancelEvidence = new();
    private static readonly List<object> _protectionEvidence = new();
    private static string _output = "";
    private static NGame Game => NGame.Instance ?? throw new InvalidOperationException("Native game unavailable");
    private static NModalContainer Modals => NModalContainer.Instance ?? throw new InvalidOperationException("Native modal container unavailable");
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("Onboarding audit: " + label);
        _checks++; MainFile.Logger.Info("ONBOARDING_CHECK_PASS " + label);
    }
    private static async Task Wait(double seconds = .18) => await Game.ToSignal(Game.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private static async Task Until(Func<bool> condition, string label, double seconds = 12)
    {
        ulong deadline = Time.GetTicksMsec() + (ulong)(seconds * 1000);
        while (!condition())
        {
            if (Time.GetTicksMsec() > deadline) throw new TimeoutException("Onboarding audit: " + label);
            await Game.ToSignal(Game.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
    private static async Task<LibrarianTutorialModal> Modal(LibrarianTutorialMode mode)
    {
        await Until(() => Modals.OpenModal is LibrarianTutorialModal m && m.Mode == mode, "native " + mode + " modal");
        await Wait();
        var modal = (LibrarianTutorialModal)Modals.OpenModal!;
        Check(modal.Mode == mode && modal.IsVisibleInTree(), "native visible " + mode);
        return modal;
    }
    private static async Task Capture(string name, Vector2I? expectedSize = null)
    {
        await Game.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        // Node text can be complete while its last lines are clipped. Inspect the
        // actual rendered content height after layout, including the new footer.
        IEnumerable<RichTextLabel> labels = Modals.OpenModal switch {
            LibrarianPracticeOverlay lesson => lesson.GetNode<Control>("IllustratedLesson").GetChildren().OfType<RichTextLabel>(),
            LibrarianTutorialModal popup => new[] { popup.Panel.GetNode<RichTextLabel>("Description") },
            _ => Enumerable.Empty<RichTextLabel>()
        };
        if (name.StartsWith("practice-action-", StringComparison.Ordinal))
            labels = Descendants(Game).OfType<LibrarianPracticeTaskHint>().Where(h => h.IsVisibleInTree())
                .Select(h => h.GetNode<RichTextLabel>("TaskLayout/TaskText"));
        var layout = new List<object>();
        foreach (var label in labels)
        {
            int height = label.GetContentHeight();
            Check(height <= label.Size.Y + 1, "rendered tutorial text fits " + name + " " + label.Name);
            layout.Add(new { name = label.Name.ToString(), text = label.GetParsedText(), contentHeight = height,
                boxWidth = label.Size.X, boxHeight = label.Size.Y,
                fontSize = label.GetThemeFontSize("normal_font_size"), fullyVisible = height <= label.Size.Y + 1 });
        }
        if (layout.Count > 0) File.WriteAllText(Path.Combine(_output, name + "-text-layout.json"),
            JsonSerializer.Serialize(layout, new JsonSerializerOptions { WriteIndented = true }));
        using var image = Game.GetViewport().GetTexture().GetImage();
        if (expectedSize is { } requested)
            Check(image.GetWidth() == requested.X && image.GetHeight() == requested.Y, "actual rendered dimensions " + name);
        Check(!image.IsEmpty() && image.SavePng(Path.Combine(_output, name + ".png")) == Error.Ok, "capture " + name);
        _screenshots++;
        _captureEvidence.Add(new { file = name + ".png", actualWidth = image.GetWidth(), actualHeight = image.GetHeight(),
            requestedWidth = expectedSize?.X, requestedHeight = expectedSize?.Y });
        File.WriteAllText(Path.Combine(_output, "screenshot-dimensions.json"), JsonSerializer.Serialize(_captureEvidence, new JsonSerializerOptions { WriteIndented = true }));
        MainFile.Logger.Info($"ONBOARDING_SCREENSHOT path={Path.Combine(_output, name + ".png")} dimensions={image.GetWidth()}x{image.GetHeight()}");
    }
    private static string ButtonText(NPopupYesNoButton button) => button.GetNode<Label>("%Label").Text;
    private static void Receipts(bool tutorial, bool compact, int wins = 0, int losses = 0)
    {
        var data = SaveManager.Instance.Progress.ToSerializable();
        data.FtueCompleted = data.FtueCompleted.Where(x => x != LibrarianOnboarding.Decision
            && x != LibrarianOnboarding.Accepted && x != LibrarianOnboarding.CompactDecision).ToList();
        if (tutorial) data.FtueCompleted.Add(LibrarianOnboarding.Decision);
        if (compact) data.FtueCompleted.Add(LibrarianOnboarding.CompactDecision);
        SaveManager.Instance.Progress = ProgressState.FromSerializable(data, new DeserializationContext());
        var stats = SaveManager.Instance.Progress.GetOrCreateCharacterStats(ModelDb.Character<LibrarianCharacter>().Id);
        stats.TotalWins = wins; stats.TotalLosses = losses; stats.Playtime = 0;
        SaveManager.Instance.SaveProgressFile();
    }
    private static IEnumerable<Node> Descendants(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static async Task<NCharacterSelectScreen> OpenSelection(NMainMenu menu)
    {
        Check(!menu.SubmenuStack.SubmenusOpen && Modals.OpenModal is null, "menu free before character selection");
        var screen = menu.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();
        screen.InitializeSingleplayer(); menu.SubmenuStack.Push(screen);
        await Wait(.75);
        Check(screen.IsVisibleInTree(), "native character selection opened");
        return screen;
    }
    private static NCharacterSelectButton Button(NCharacterSelectScreen screen, bool librarian)
        => Descendants(screen).OfType<NCharacterSelectButton>().Single(b => librarian ? b.Character is LibrarianCharacter : b.Character is Ironclad);
    private static async Task SelectLibrary(NCharacterSelectScreen screen)
    {
        Button(screen, false).Select(); await Wait();
        Check(!Button(screen, true).IsLocked, "Librarian is selectable in isolated fixture");
        Button(screen, true).Select();
    }
    private static async Task NativeAction(string action)
    {
        var events = InputMap.ActionGetEvents(action);
        var template = events.OfType<InputEventKey>().FirstOrDefault() as InputEvent
            ?? (InputEvent?)events.OfType<InputEventJoypadButton>().FirstOrDefault()
            ?? new InputEventAction { Action = action };
        var press = (InputEvent)template.Duplicate();
        var release = (InputEvent)template.Duplicate();
        if (press is InputEventKey pk && release is InputEventKey rk) { pk.Pressed = true; pk.Echo = false; rk.Pressed = false; rk.Echo = false; }
        else if (press is InputEventJoypadButton pj && release is InputEventJoypadButton rj) { pj.Pressed = true; rj.Pressed = false; }
        else if (press is InputEventAction pa && release is InputEventAction ra) { pa.Pressed = true; ra.Pressed = false; }
        MainFile.Logger.Info($"ONBOARDING_NATIVE_INPUT action={action} type={template.GetType().Name}");
        Input.ParseInputEvent(press); Input.ParseInputEvent(release); await Wait();
    }
    private static async Task Keyboard(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        await Wait();
    }
    private static async Task MouseClick(NButton button)
    {
        var point = button.GetViewport().GetStretchTransform() * button.GetGlobalTransformWithCanvas() * (button.Size * .5f);
        Input.WarpMouse(point);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = point, GlobalPosition = point, Relative = new Vector2(12, 0) });
        await Wait();
        foreach (bool pressed in new[] { true, false })
            Input.ParseInputEvent(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = pressed });
        await Wait();
    }
    private static async Task CheckHoverTips()
    {
        var evidence = new List<object>();
        Check(!LibrarianPreferences050.CreateDefaults().CompactHoverTips, "all hover tips are the default");
        foreach (string language in new[] { "zhs", "eng" })
        {
            LibrarianLanguage.Select(language);
            var card = ModelDb.Card<RidgeWard>().ToMutable();
            LibrarianPreferences050.Current.CompactHoverTips = false;
            var full = card.HoverTips.ToArray();
            LibrarianPreferences050.Current.CompactHoverTips = true;
            var compact = card.HoverTips.ToArray();
            bool Has(IEnumerable<IHoverTip> tips, string key) => tips.Any(t => t.Id == LibrarianHoverTips.Tip(key).Id);
            Check(compact.Length < full.Length, "Ridge Ward removes nested explanations " + language);
            foreach (string key in new[] { "EXTRA_SETTLE", "WAVES", "LOCK" })
                Check(Has(full, key) && Has(compact, key), "Ridge Ward core root retained " + language + " " + key);
            foreach (string key in new[] { "SETTLE", "FOREGROUND", "ACTIVATE", "EXTINGUISH" })
                Check(Has(full, key) && !Has(compact, key), "Ridge Ward nested-only tip omitted " + language + " " + key);
            var block = LibrarianLanguage.NativeTip(() => HoverTipFactory.Static(StaticHoverTip.Block));
            // The current beta Still Tide description has no Block requirement.
            // Verify the native tip with the real Defend card, which does require it.
            var nativeCard = ModelDb.Card<LibrarianDefend>().ToMutable();
            LibrarianPreferences050.Current.CompactHoverTips = false;
            var fullNative = nativeCard.HoverTips.ToArray();
            LibrarianPreferences050.Current.CompactHoverTips = true;
            var compactNative = nativeCard.HoverTips.ToArray();
            Check(fullNative.Any(t => t.Id == block.Id) && compactNative.Any(t => t.Id == block.Id), "native Block survives nested filtering " + language);
            foreach (var keyword in card.Keywords)
                Check(compact.Any(t => t.Id == HoverTipFactory.FromKeyword(keyword).Id), "native card keyword retained " + language + " " + keyword);
            Check(IHoverTip.RemoveDupes(compact).SequenceEqual(compact), "compact native dedupe " + language);
            var direct = LibrarianHoverTips.Expand(new IHoverTip[] { LibrarianHoverTips.Tip("SETTLE"), LibrarianHoverTips.Tip("ACTIVATE"), LibrarianHoverTips.Tip("BACKGROUND") }).ToArray();
            foreach (string key in new[] { "SETTLE", "ACTIVATE", "BACKGROUND" }) Check(Has(direct, key), "explicit root retained " + language + " " + key);
            var root = new HoverTip(new LocString("static_hover_tips", "LIBRARIAN_EXTRA_SETTLE.title"), "[gold]" + LibrarianLanguage.Format("KEYWORD_BACKGROUND").Split('|')[0] + "[/gold]");
            Check(!Has(LibrarianHoverTips.Expand(new IHoverTip[] { root }), "BACKGROUND"), "nested Background omitted " + language);
            evidence.Add(new { language, card = card.Id.Entry, full = full.Select(t => t.Id).ToArray(), compact = compact.Select(t => t.Id).ToArray() });
            foreach (bool simplified in new[] { false, true })
            {
                LibrarianPreferences050.Current.CompactHoverTips = simplified;
                await RenderHover(card, "hover-ridge-" + language + "-" + (simplified ? "compact" : "full"));
            }
        }
        File.WriteAllText(Path.Combine(_output, "hover-evidence.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static async Task RenderHover(CardModel card, string name)
    {
        var layer = new CanvasLayer { Layer = 120 }; Game.AddChild(layer);
        var panel = new Control(); layer.AddChild(panel);
        var size = Game.GetViewport().GetVisibleRect().Size;
        var background = new ColorRect { Color = new Color("20272e"), Size = size }; panel.AddChild(background);
        var node = GD.Load<PackedScene>("res://scenes/cards/card.tscn").Instantiate<NCard>(); panel.AddChild(node); node.Model = card;
        node.Position = new Vector2(size.X * .22f, size.Y * .5f); node.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
        var owner = new Control { Position = new(size.X * .42f, 70), Size = new(20, 20) }; panel.AddChild(owner);
        var tips = card.HoverTips.ToArray();
        var set = NHoverTipSet.CreateAndShow(owner, tips, HoverTipAlignment.Right);
        Check(set is not null, "native hover renderer " + name);
        set!.GetParent().RemoveChild(set); panel.AddChild(set);
        try
        {
            await Wait(.4);
            var rows = set.GetNode<Control>("textHoverTipContainer").GetChildren().OfType<Control>().ToArray();
            Check(rows.Length == tips.Length, "native hover row count " + name);
            await Capture(name);
        }
        finally { NHoverTipSet.Remove(owner); layer.QueueFree(); await Wait(); }
    }
    private static async Task RenderedSize(Vector2I requested)
    {
        ulong deadline = Time.GetTicksMsec() + 15000;
        int stable = 0;
        while (stable < 3)
        {
            DisplayServer.WindowSetSize(requested); Game.GetWindow().Size = requested;
            await Wait(.12);
            await Game.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var frame = Game.GetViewport().GetTexture().GetImage();
            stable = frame.GetWidth() == requested.X && frame.GetHeight() == requested.Y ? stable + 1 : 0;
            if (Time.GetTicksMsec() > deadline)
                throw new TimeoutException($"Onboarding audit: viewport did not settle at {requested.X}x{requested.Y}; actual {frame.GetWidth()}x{frame.GetHeight()}");
        }
    }

    private static string ProgressFingerprint(ProgressState progress, bool ignoreCompleted = false)
    {
        var data = progress.ToSerializable();
        if (ignoreCompleted) data.FtueCompleted = data.FtueCompleted.Where(x => x != LibrarianPracticeSession.Completed).ToList();
        return JsonSerializer.Serialize(data);
    }
    private static string? ProfileFileHash(string relative)
    {
        string path = ProjectSettings.GlobalizePath(SaveManager.Instance.GetProfileScopedPath(relative));
        return File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : null;
    }
    private sealed record RitsuProgressApi(MethodInfo SaveMirror, MethodInfo MirrorPath, MethodInfo SaveOrdinaryProgress, FieldInfo MirrorBusy);
    private static RitsuProgressApi? _ritsuProgress;
    private static RitsuProgressApi RitsuProgress()
    {
        if (_ritsuProgress is not null) return _ritsuProgress;
        Type LoadedType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name, throwOnError: false)).OfType<Type>().Distinct().Single();
        var mirror = LoadedType("STS2RitsuLib.Saves.ProgressMirrorStore");
        var bridge = LoadedType("STS2RitsuLib.Saves.RawProgress.RawProgressCommitBridge");
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        var saveMirror = mirror.GetMethod("SaveMirror", flags, null, [typeof(SerializableProgress), typeof(string)], null)
            ?? throw new MissingMethodException(mirror.FullName, "SaveMirror(SerializableProgress,string)");
        var mirrorPath = mirror.GetMethod("GetMirrorPath", flags, null, Type.EmptyTypes, null)
            ?? throw new MissingMethodException(mirror.FullName, "GetMirrorPath()");
        var saveOrdinary = bridge.GetMethod("SaveOrdinaryProgress", flags, null, [typeof(ProgressSaveManager)], null)
            ?? throw new MissingMethodException(bridge.FullName, "SaveOrdinaryProgress(ProgressSaveManager)");
        var mirrorBusy = mirror.GetField("_isSavingMirror", flags)
            ?? throw new MissingFieldException(mirror.FullName, "_isSavingMirror");
        Check(saveMirror.ReturnType == typeof(void) && saveOrdinary.ReturnType == typeof(void)
            && mirrorPath.ReturnType == typeof(string) && mirrorBusy.FieldType == typeof(bool),
            "loaded Ritsu precise progress writer signatures and native mirror busy field");
        var nativeSave = AccessTools.Method(typeof(ProgressSaveManager), nameof(ProgressSaveManager.SaveProgress), Type.EmptyTypes);
        var patches = Harmony.GetPatchInfo(nativeSave);
        Check(patches is not null && patches.Prefixes.Any(patch => patch.owner == MainFile.ModId
            && patch.PatchMethod.DeclaringType == typeof(LibrarianPracticeProgressGuard)
            && patch.PatchMethod.Name == "Prefix"), "native progress save has Librarian practice guard installed");
        File.WriteAllText(Path.Combine(_output, "ritsu-progress-guard.json"), JsonSerializer.Serialize(new
        {
            rawWriter = saveOrdinary.DeclaringType!.FullName + "." + saveOrdinary.Name,
            nativeSaveGuard = nativeSave.DeclaringType!.FullName + "." + nativeSave.Name,
            prefixes = patches!.Prefixes.Select(patch => new { owner = patch.owner,
                type = patch.PatchMethod.DeclaringType?.FullName, method = patch.PatchMethod.Name }).ToArray(),
            mirrorProtection = "session holds existing native busy flag",
            rawWriteProtection = nameof(LibrarianPracticeProgressStore),
            mirrorBusyField = mirrorBusy.DeclaringType!.FullName + "." + mirrorBusy.Name
        }, new JsonSerializerOptions { WriteIndented = true }));
        var evidence = new[] { mirror, bridge }.Select(type => new
        {
            type = type.FullName, assembly = type.Assembly.FullName,
            moduleVersionId = type.Assembly.ManifestModule.ModuleVersionId,
            assemblyPath = type.Assembly.Location,
            sha256 = File.Exists(type.Assembly.Location) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(type.Assembly.Location))) : null
        });
        File.WriteAllText(Path.Combine(_output, "ritsu-progress-api.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        return _ritsuProgress = new(saveMirror, mirrorPath, saveOrdinary, mirrorBusy);
    }
    private static ProgressSaveManager NativeProgressManager()
    {
        var field = typeof(SaveManager).GetField("_progressSaveManager", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(SaveManager).FullName, "_progressSaveManager");
        return field.GetValue(SaveManager.Instance) as ProgressSaveManager
            ?? throw new InvalidOperationException("Native progress manager unavailable");
    }
    private static ISaveStore NativeProgressStore(ProgressSaveManager manager)
    {
        var field = typeof(ProgressSaveManager).GetField("_saveStore", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(ProgressSaveManager).FullName, "_saveStore");
        return field.GetValue(manager) as ISaveStore
            ?? throw new InvalidOperationException("Native progress save store unavailable");
    }
    private sealed record ProgressProtectionState(ISaveStore Store, bool MirrorBusy);
    private static ProgressProtectionState CaptureProgressProtection()
    {
        var store = NativeProgressStore(NativeProgressManager());
        bool busy = (bool)RitsuProgress().MirrorBusy.GetValue(null)!;
        Check(!LibrarianPracticeSession.Active && store is not LibrarianPracticeProgressStore && !busy,
            "ordinary progress storage and mirror are idle before lesson");
        return new(store, busy);
    }
    private static void CheckRestoredProgressProtection(ProgressProtectionState expected, string label)
    {
        var store = NativeProgressStore(NativeProgressManager());
        bool busy = (bool)RitsuProgress().MirrorBusy.GetValue(null)!;
        Check(!LibrarianPracticeSession.Active && ReferenceEquals(store, expected.Store)
            && store is not LibrarianPracticeProgressStore && busy == expected.MirrorBusy,
            "lesson exit restores exact native progress store and mirror busy flag " + label);
        _protectionEvidence.Add(new { label, active = false, store = store.GetType().FullName,
            originalStoreReferenceRestored = true, mirrorBusy = busy });
        File.WriteAllText(Path.Combine(_output, "practice-progress-protection.json"), JsonSerializer.Serialize(_protectionEvidence, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static Dictionary<string, string> ProgressFilePaths()
    {
        string native = ProjectSettings.GlobalizePath(SaveManager.Instance.GetProfileScopedPath(Path.Combine("saves", "progress.save")));
        string mirror = ProjectSettings.GlobalizePath((string)(RitsuProgress().MirrorPath.Invoke(null, null)
            ?? throw new InvalidOperationException("Ritsu mirror path unavailable")));
        return new() { ["native"] = native, ["native_backup"] = native + ".backup",
            ["ritsu_mirror"] = mirror, ["ritsu_mirror_backup"] = mirror + ".backup" };
    }
    private static Dictionary<string, string?> ProgressFileHashes() => ProgressFilePaths().ToDictionary(pair => pair.Key,
        pair => File.Exists(pair.Value) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Value))) : null);
    private static void CheckProgressFiles(Dictionary<string, string?> expected, string label)
    {
        var actual = ProgressFileHashes();
        foreach (var pair in expected) Check(actual[pair.Key] == pair.Value, label + " " + pair.Key);
        _progressEvidence.Add(new { label, active = LibrarianPracticeSession.Active, paths = ProgressFilePaths(), hashes = actual });
        File.WriteAllText(Path.Combine(_output, "practice-progress-hashes.json"), JsonSerializer.Serialize(_progressEvidence, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static string ProgressMemoryFingerprint(ProgressState progress, bool ignoreCompleted = false)
    {
        // ToSerializable has a Ritsu mirror-writing postfix. Read the native stored
        // fields directly for post-exit comparisons so the assertion cannot alter disk.
        var values = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in typeof(ProgressState).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            object? value = field.GetValue(progress);
            if (value is Delegate) continue;
            if (field.Name == "_ftueCompleted" && value is IEnumerable<string> receipts)
                value = receipts.Where(marker => !ignoreCompleted || marker != LibrarianPracticeSession.Completed).OrderBy(marker => marker, StringComparer.Ordinal).ToArray();
            else if (value is System.Collections.IDictionary dictionary)
                value = dictionary.Keys.Cast<object>().OrderBy(key => key.ToString(), StringComparer.Ordinal)
                    .Select(key => new { key = key.ToString(), value = dictionary[key] }).ToArray();
            values[field.Name] = value;
        }
        return JsonSerializer.Serialize(values);
    }
    private static string SerializableFingerprint(SerializableProgress data, bool ignoreAllLessonReceipts = false)
    {
        var copy = JsonSerializer.Deserialize<SerializableProgress>(JsonSerializer.Serialize(data))!;
        copy.SchemaVersion = 0;
        copy.FtueCompleted = copy.FtueCompleted.Where(marker => marker != LibrarianPracticeSession.Completed
            && (!ignoreAllLessonReceipts || marker != LibrarianOnboarding.Decision && marker != LibrarianOnboarding.Accepted
                && marker != LibrarianOnboarding.CompactDecision)).OrderBy(marker => marker, StringComparer.Ordinal).ToList();
        List<T> Ordered<T>(List<T> items) => items.OrderBy(item => JsonSerializer.Serialize(item), StringComparer.Ordinal).ToList();
        copy.CharStats = Ordered(copy.CharStats); copy.CardStats = Ordered(copy.CardStats);
        copy.EncounterStats = Ordered(copy.EncounterStats); copy.EnemyStats = Ordered(copy.EnemyStats); copy.AncientStats = Ordered(copy.AncientStats);
        copy.DiscoveredCards = Ordered(copy.DiscoveredCards); copy.DiscoveredRelics = Ordered(copy.DiscoveredRelics);
        copy.DiscoveredEvents = Ordered(copy.DiscoveredEvents); copy.DiscoveredPotions = Ordered(copy.DiscoveredPotions); copy.DiscoveredActs = Ordered(copy.DiscoveredActs);
        copy.UnlockedAchievements = Ordered(copy.UnlockedAchievements);
        return JsonSerializer.Serialize(copy);
    }
    private static void CheckCompletedProgressFiles(SerializableProgress expected)
    {
        foreach (var pair in ProgressFilePaths())
        {
            if (!File.Exists(pair.Value)) { Check(pair.Key.EndsWith("_backup", StringComparison.Ordinal), "completed main progress file exists " + pair.Key); continue; }
            var read = JsonSerializationUtility.FromJson<SerializableProgress>(File.ReadAllText(pair.Value));
            Check(read.Success && read.SaveData is not null, "native parser reads completed progress file " + pair.Key);
            var actual = read.SaveData!;
            bool backup = pair.Key.EndsWith("_backup", StringComparison.Ordinal);
            Check(SerializableFingerprint(actual, backup) == SerializableFingerprint(expected, backup),
                "completed progress and mirror contain no teaching discoveries or statistics " + pair.Key);
            if (!backup) Check(actual.FtueCompleted.Contains(LibrarianPracticeSession.Completed), "completed receipt persisted " + pair.Key);
        }
        _progressEvidence.Add(new { label = "completed legitimate writes", active = false, paths = ProgressFilePaths(), hashes = ProgressFileHashes() });
        File.WriteAllText(Path.Combine(_output, "practice-progress-hashes.json"), JsonSerializer.Serialize(_progressEvidence, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static async Task ExerciseProgressWriters(Dictionary<string, string?> expected, string label)
    {
        Check(LibrarianPracticeSession.Active, "progress serialization occurs inside active practice " + label);
        Check(RitsuProgress().MirrorBusy.GetValue(null) is true, "active practice holds native Ritsu mirror busy flag " + label);
        // The probe lives only in the disposable clone and detects writes even before
        // this lesson has discovered a card absent from the ordinary profile.
        SaveManager.Instance.MarkFtueAsComplete("librarian_onboarding_audit_clone_only");
        var clone = SaveManager.Instance.Progress.ToSerializable();
        Check(clone.FtueCompleted.Contains("librarian_onboarding_audit_clone_only"), "disposable progress contains write probe " + label);
        RitsuProgress().SaveMirror.Invoke(null, [clone, JsonSerializationUtility.ToJson(clone)]);
        var manager = NativeProgressManager();
        var isolatedStore = NativeProgressStore(manager) as LibrarianPracticeProgressStore;
        Check(isolatedStore is not null, "active practice uses read-through isolated native progress store " + label);
        int writesBefore = isolatedStore!.SuppressedWrites;
        RitsuProgress().SaveOrdinaryProgress.Invoke(null, [manager]);
        Check(isolatedStore.SuppressedWrites >= writesBefore + 1,
            "direct Ritsu raw writer reaches suppressed native progress store " + label);
        _protectionEvidence.Add(new { label, active = true, store = isolatedStore.GetType().FullName,
            mirrorBusy = true, suppressedWritesBefore = writesBefore, suppressedWritesAfter = isolatedStore.SuppressedWrites });
        File.WriteAllText(Path.Combine(_output, "practice-progress-protection.json"), JsonSerializer.Serialize(_protectionEvidence, new JsonSerializerOptions { WriteIndented = true }));
        SaveManager.Instance.SaveProgressFile(); await SaveManager.Instance.SaveRun(null);
        CheckProgressFiles(expected, "clone serialization and explicit native/Ritsu writers do not write " + label);
        _progressWriterProbes++;
    }
    private static async Task<NMainMenu> ReadyMenu(List<(Node node, bool enabled)> suspended)
    {
        await Until(() => Game.MainMenu is { } menu && menu.IsVisibleInTree() && !RunManager.Instance.IsInProgress,
            "native menu restored", 35);
        var menu = Game.MainMenu!;
        foreach (var node in menu.GetChildren().Where(n => n is LibrarianUpdateNotice051 or LibrarianArchitectReview102))
            if (!suspended.Any(e => ReferenceEquals(e.node, node)))
            {
                suspended.Add((node, node.IsProcessing())); node.SetProcess(false);
            }
        Modals.Clear(); await Wait(1);
        return menu;
    }
    private static Dictionary<string, string?> RunFileHashes()
    {
        return new[] { "current_run.save", "current_run.save.backup", "current_run_mp.save", "current_run_mp.save.backup" }
            .ToDictionary(name => name, name =>
            {
                return ProfileFileHash(Path.Combine("saves", name));
            });
    }
    private static void CheckRunFiles(Dictionary<string, string?> expected, string label)
    {
        var actual = RunFileHashes();
        foreach (var pair in expected) Check(actual[pair.Key] == pair.Value, label + " " + pair.Key);
    }
    private static string PracticeFingerprint(LibrarianPracticeSession practice)
    {
        var player = practice.Player;
        var session = LibrarianRuntime.Get(player);
        return JsonSerializer.Serialize(new
        {
            seed = practice.RunState.Rng.StringSeed,
            turn = player.PlayerCombatState!.TurnNumber,
            hand = player.PlayerCombatState.Hand.Cards.Select(c => c.Id.Entry).ToArray(),
            orbs = session.Orbs.Snapshot(), waves = session.Waves.Amount,
            enemies = player.Creature.CombatState!.Enemies.Select(c => new { id = c.ModelId.Entry, c.CurrentHp, c.MaxHp }).ToArray()
        });
    }
    private static object PracticeObservation(LibrarianPracticeSession practice)
    {
        var player = practice.Player;
        var session = LibrarianRuntime.Get(player);
        return new
        {
            practice.Step, practice.AwaitingAction, practice.AllowEndTurn,
            card = practice.CurrentCard?.Id.Entry,
            turn = player.PlayerCombatState!.TurnNumber, energy = player.PlayerCombatState.Energy,
            player.Creature.Block, player.Creature.CurrentHp,
            hand = player.PlayerCombatState.Hand.Cards.Select(c => c.Id.Entry).ToArray(),
            orbs = session.Orbs.Snapshot(), waves = session.Waves.Amount,
            settlements = session.Orbs.SettlementsThisCombat,
            enemies = player.Creature.CombatState!.Enemies.Select(c => new { id = c.ModelId.Entry, c.CurrentHp, c.MaxHp }).ToArray()
        };
    }
    private static async Task<LibrarianPracticeSession> Practice()
    {
        await Until(() => LibrarianPracticeSession.Active && LibrarianPracticeSession.Instance is { Step: 0 }
            && Modals.OpenModal is LibrarianPracticeOverlay, "real practice first combat overlay", 45);
        await Wait(.4);
        var practice = LibrarianPracticeSession.Instance!;
        Check(practice.Player.Character is LibrarianCharacter && practice.RunState.GameMode == GameMode.Custom,
            "practice uses native Librarian Custom run");
        Check(practice.RunState.Rng.StringSeed == "LIBRARIAN_LESSON_V1", "practice uses fixed lesson seed");
        Check(!RunManager.Instance.ShouldSave, "practice native run disables saving");
        Check(CombatManager.Instance.IsInProgress && practice.Player.PlayerCombatState!.Hand.Cards.Count == 5,
            "practice has real combat and fixed five-card initial hand");
        Check(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance?.IsOpen != true,
            "practice battle visible with native map closed");
        return practice;
    }
    private static async Task NativePracticeAction(GameAction action, string label)
    {
        MainFile.Logger.Info("ONBOARDING_NATIVE_ACTION " + action);
        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
        await Until(() => action.CompletionTask.IsCompleted, "native action completes " + label, 30);
        Check(action.Exception is null && action.CompletionTask.IsCompletedSuccessfully, "native action executes successfully " + label);
    }
    private static async Task CancelAcceptedAction(LibrarianPracticeSession practice)
    {
        int step = practice.Step;
        string before = JsonSerializer.Serialize(PracticeObservation(practice));
        GameAction action = practice.AllowEndTurn
            ? new EndPlayerTurnAction(practice.Player, practice.Player.PlayerCombatState!.TurnNumber)
            : new PlayCardAction(practice.CurrentCard!, null);
        // Intentionally exercise the session's acceptance checkpoint before enqueue.
        // This is a controlled recovery test, not a queued-action timing race or a
        // claim that the actual native action executor was interrupted mid-effect.
        Check(practice.AcceptAction(action), "controlled practice acceptance before native Cancel step" + step);
        action.Cancel();
        Check(action.State == GameActionState.Canceled && action.Id is null, "native canceled state before enqueue step" + step);
        await Wait(.4);
        Check(practice.Step == step && practice.AwaitingAction && Modals.OpenModal is null
            && JsonSerializer.Serialize(PracticeObservation(practice)) == before,
            "accepted native Cancel returns to same unmodified task step" + step);
        _cancelEvidence.Add(new { step, scope = "session_accept_then_native_cancel_before_enqueue",
            state = action.State.ToString(), completionFinished = action.CompletionTask.IsCompleted,
            completionCanceled = action.CompletionTask.IsCanceled, actionWasEnqueued = action.Id is not null });
        File.WriteAllText(Path.Combine(_output, "practice-cancel-retry.json"), JsonSerializer.Serialize(_cancelEvidence, new JsonSerializerOptions { WriteIndented = true }));
        MainFile.Logger.Info($"ONBOARDING_CANCEL_RETRY_SCOPE step={step} sessionAcceptance=True nativeCancel=True queued=False completionFinished={action.CompletionTask.IsCompleted}");
    }
    private static async Task CaptureTaskHint(LibrarianPracticeSession practice, string language)
    {
        var size = new Vector2I(1920, 1080);
        LibrarianLanguage.Select(language);
        await RenderedSize(size); await Wait(.25);
        var hint = Descendants(practice).OfType<LibrarianPracticeTaskHint>().Single();
        Check(practice.AwaitingAction && Modals.OpenModal is null && hint.IsVisibleInTree()
            && hint.MouseFilter == Control.MouseFilterEnum.Ignore && NCombatRoom.Instance?.IsVisibleInTree() == true
            && NCombatRoom.Instance.Ui.Hand.IsVisibleInTree()
            && MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance?.IsOpen != true,
            "operation task hint leaves actual combat and hand visible step" + practice.Step);
        string Plain(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\[[^\]]*\]", "").Trim();
        var instruction = Descendants(hint).OfType<RichTextLabel>().Single();
        var exit = hint.GetNode<NPopupYesNoButton>("TaskLayout/LeavePractice");
        Check(Plain(instruction.Text) == Plain(LibrarianOnboarding.Text("practice_task" + practice.Step))
            && ButtonText(exit) == LibrarianOnboarding.Text("practice_exit"),
            "operation task instruction and exit refresh language " + language + " step" + practice.Step);
        await Capture($"practice-action-{LibrarianLanguage.Selected}-1920-step{practice.Step:00}", size);
        _taskScreenshots++;
    }
    private static async Task NativeMouseSpark(LibrarianPracticeSession practice, CardModel card)
    {
        Check(practice.Step == 0 && practice.AwaitingAction && Modals.OpenModal is null
            && !CombatManager.Instance.PlayerActionsDisabled
            && MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance?.IsOpen != true,
            "Spark GUI drag begins in native playable combat");
        var hand = NPlayerHand.Instance ?? throw new InvalidOperationException("Native hand unavailable for Spark GUI drag");
        var holder = hand.GetCardHolder(card) as NHandCardHolder
            ?? throw new InvalidOperationException("Native Spark hand holder unavailable");
        var viewport = holder.GetViewport();
        Vector2 Center() => viewport.GetStretchTransform() * holder.Hitbox.GetGlobalTransformWithCanvas() * (holder.Hitbox.Size * .5f);
        void Move(Vector2 point, Vector2 relative, bool held)
        {
            Input.WarpMouse(point);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = point, GlobalPosition = point, Relative = relative,
                ButtonMask = held ? MouseButtonMask.Left : (MouseButtonMask)0 });
        }
        Vector2 start = Center();
        Move(start, new Vector2(12, 0), false);
        await Until(() => ReferenceEquals(hand.FocusedHolder, holder), "real mouse focuses Spark hand hitbox");
        await Wait(.3); start = Center();
        Move(start, new Vector2(0, -1), false);
        await Wait(.1);
        Check(ReferenceEquals(hand.FocusedHolder, holder), "hover movement retains real Spark holder focus");
        Vector2 targetLocal = viewport.GetVisibleRect().Size * new Vector2(.5f, .5f);
        Vector2 target = viewport.GetStretchTransform() * targetLocal;
        bool released = false;
        try
        {
            Input.ParseInputEvent(new InputEventMouseButton { Position = start, GlobalPosition = start,
                ButtonIndex = MouseButton.Left, Pressed = true });
            await Until(() => hand.InCardPlay, "real mouse press enters native Spark card play");
            Move(target, target - start, true); await Wait(.35);
            Check(hand.InCardPlay && practice.Step == 0 && practice.AwaitingAction,
                "Spark GUI drag enters native play zone without automatic lesson action");
            await Capture("practice-mouse-spark-drag-eng-1920", new Vector2I(1920, 1080));
            Input.ParseInputEvent(new InputEventMouseButton { Position = target, GlobalPosition = target,
                ButtonIndex = MouseButton.Left, Pressed = false });
            released = true;
            _guiCardPlays++;
            File.WriteAllText(Path.Combine(_output, "practice-mouse-spark.json"), JsonSerializer.Serialize(new
            {
                step = 0, card = card.Id.ToString(), source = "native_hand_hitbox_mouse_drag_and_release",
                start = new { x = start.X, y = start.Y }, target = new { x = target.X, y = target.Y },
                viewportTarget = new { x = targetLocal.X, y = targetLocal.Y }, focusedHolder = true,
                nativeCardPlayEntered = true, directActionEnqueuedByAudit = false
            }, new JsonSerializerOptions { WriteIndented = true }));
            MainFile.Logger.Info("ONBOARDING_NATIVE_GUI_CARD step=0 nativeHitbox=True mouseFocus=True nativeDrag=True nativeRelease=True directAuditAction=False");
        }
        finally
        {
            if (!released) Input.ParseInputEvent(new InputEventMouseButton { Position = target, GlobalPosition = target,
                ButtonIndex = MouseButton.Left, Pressed = false });
        }
    }
    private static async Task RejectWrongAction(LibrarianPracticeSession practice, string label)
    {
        int step = practice.Step;
        string before = JsonSerializer.Serialize(PracticeObservation(practice));
        var wrong = practice.Player.PlayerCombatState!.Hand.Cards.FirstOrDefault(c => !ReferenceEquals(c, practice.CurrentCard));
        if (wrong is not null)
        {
            var action = new PlayCardAction(wrong, wrong.TargetType == TargetType.AnyEnemy
                ? practice.Player.Creature.CombatState!.Enemies.First() : null);
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
            await Wait(.3);
            Check(action.Id is null && practice.Step == step && JsonSerializer.Serialize(PracticeObservation(practice)) == before,
                "wrong native card request is rejected " + label);
        }
        if (!practice.AllowEndTurn)
        {
            var action = new EndPlayerTurnAction(practice.Player, practice.Player.PlayerCombatState!.TurnNumber);
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
            await Wait(.3);
            Check(action.Id is null && practice.Step == step && JsonSerializer.Serialize(PracticeObservation(practice)) == before,
                "premature native end-turn request is rejected " + label);
        }
    }
    private static NButton PracticeContinueButton(LibrarianPracticeOverlay overlay)
    {
        return overlay.ContinueButton;
    }
    private static async Task PracticeScreenshots(LibrarianPracticeSession practice)
    {
        int step = practice.Step;
        string before = PracticeFingerprint(practice);
        foreach (var resolution in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            foreach (string language in new[] { "zhs", "eng" })
            {
                LibrarianLanguage.Select(language);
                Modals.Clear(); await Wait(.4);
                await RenderedSize(resolution);
                var overlay = LibrarianPracticeOverlay.Create(practice); await Wait(.35);
                await RenderedSize(resolution);
                Check(ReferenceEquals(Modals.OpenModal, overlay) && overlay.IsVisibleInTree() && overlay.HasIllustration,
                    $"real battle overlay includes orb illustration and native card {language} {resolution.X} step{step}");
                Check(practice.Step == step && !practice.AwaitingAction && PracticeFingerprint(practice) == before,
                    $"explanation does not play or advance {language} {resolution.X} step{step}");
                await Capture($"practice-{language}-{resolution.X}-step{step:00}", resolution);
                _explanationScreenshots++;
            }
    }
    private static async Task<string> CompletePractice(LibrarianPracticeSession practice, ProgressState ordinaryProgress,
        Dictionary<string, string?> saveHashes)
    {
        string initial = PracticeFingerprint(practice);
        var progressHashes = ProgressFileHashes();
        var ordinarySerialized = ordinaryProgress.ToSerializable();
        string progress = ProgressMemoryFingerprint(ordinaryProgress, ignoreCompleted: true);
        Check(!ReferenceEquals(SaveManager.Instance.Progress, ordinaryProgress), "practice uses a separate cloned progress object");
        await ExerciseProgressWriters(progressHashes, "initial lesson");
        CheckRunFiles(saveHashes, "explicit native save request leaves ordinary run bytes unchanged");
        var evidence = new List<object>();
        for (int step = 0; step <= 11; step++)
        {
            await Until(() => practice.Step == step && !practice.AwaitingAction && Modals.OpenModal is LibrarianPracticeOverlay,
                "observed result opens explanation step " + step, 35);
            CheckProgressFiles(progressHashes, "progress files unchanged during explanation step" + step);
            await ExerciseProgressWriters(progressHashes, "explanation step" + step);
            CheckRunFiles(saveHashes, "ordinary save unchanged during explanation step" + step);
            var session = LibrarianRuntime.Get(practice.Player);
            var before = session.Orbs.Snapshot();
            int settlements = session.Orbs.SettlementsThisCombat;
            int turn = practice.Player.PlayerCombatState!.TurnNumber;
            evidence.Add(new { phase = "before", observation = PracticeObservation(practice) });
            await RejectWrongAction(practice, "explanation step" + step);
            await PracticeScreenshots(practice);
            var overlay = (LibrarianPracticeOverlay)Modals.OpenModal!;
            if (step == 0)
            {
                PracticeContinueButton(overlay).GrabFocus();
                await NativeAction(MegaInput.select);
            }
            else await MouseClick(PracticeContinueButton(overlay));
            if (step == 11)
            {
                await Until(() => !LibrarianPracticeSession.Active && Game.MainMenu is not null,
                    "completed practice returns to native menu", 35);
                break;
            }
            await Until(() => Modals.OpenModal is null && practice.AwaitingAction,
                "continue releases native player action step" + step);
            Check(practice.Step == step, "continue alone does not advance lesson step" + step);
            await RejectWrongAction(practice, "action step" + step);
            await CaptureTaskHint(practice, "eng");
            if (step is 0 or 2 or 7)
            {
                await CaptureTaskHint(practice, "zhs");
                LibrarianLanguage.Select("eng"); await Wait(.2);
            }
            if (step is 0 or 2) await CancelAcceptedAction(practice);
            if (step is 2 or 6 or 10)
            {
                Check(practice.AllowEndTurn && practice.CurrentCard is null, "end-turn lesson permits only native end turn step" + step);
                await NativePracticeAction(new EndPlayerTurnAction(practice.Player, turn), "end turn step" + step);
            }
            else
            {
                var card = practice.CurrentCard ?? throw new InvalidOperationException("Practice expected card missing at step " + step);
                string expectedType = step switch { 0 or 5 => "Spark", 1 or 3 or 8 => "Trickle", 4 => "Springwater", 7 => "TidalGravity", 9 => "Renewal", _ => "" };
                Check(card.GetType().Name == expectedType, "lesson uses specified real card " + expectedType + " step" + step);
                Check(!practice.AllowEndTurn && card.Pile?.Type == PileType.Hand && NCard.FindOnTable(card) is not null,
                    "expected card exists in actual native hand step" + step);
                if (step == 0) await NativeMouseSpark(practice, card);
                else await NativePracticeAction(new PlayCardAction(card, card.TargetType == TargetType.AnyEnemy
                    ? practice.Player.Creature.CombatState!.Enemies.First() : null), "card step" + step);
            }
            await Until(() => practice.Step == step + 1 && !practice.AwaitingAction
                && Modals.OpenModal is LibrarianPracticeOverlay, "actual observation advances one step " + step, 35);
            var after = session.Orbs.Snapshot();
            if (step is 0 or 5)
                Check(after[OrbKind.Fire].Value > before[OrbKind.Fire].Value && after[OrbKind.Fire].IsActivated,
                    "actual Spark increases and activates Fire step" + step);
            if (step is 1 or 3)
                Check(after[OrbKind.Tide].Value > before[OrbKind.Tide].Value && after[OrbKind.Tide].IsActivated
                    && after.Foreground == OrbKind.Tide, "actual Trickle increases and activates foreground Tide step" + step);
            if (step == 4)
                Check(after[OrbKind.Tide].IsActivated && session.Orbs.SettlementsThisCombat > settlements,
                    "actual Springwater settles Tide without extinguishing it");
            if (step == 7)
                Check(after[OrbKind.Tide].IsLocked && !after[OrbKind.Tide].IsActivated, "actual TidalGravity locks and extinguishes Tide");
            if (step == 8)
                Check(after[OrbKind.Tide].Value > before[OrbKind.Tide].Value && after[OrbKind.Tide].IsLocked
                    && !after[OrbKind.Tide].IsActivated && after.Foreground == OrbKind.Tide,
                    "locked Tide can gain and move but cannot activate");
            if (step == 9)
                Check(after[OrbKind.Growth].Value > before[OrbKind.Growth].Value && after[OrbKind.Growth].IsActivated,
                    "actual Renewal increases and activates Growth");
            if (step is 2 or 6 or 10)
                Check(practice.Player.PlayerCombatState!.TurnNumber == turn + 1 && after.Orbs.All(o => !o.IsActivated)
                    && session.Orbs.SettlementsThisCombat > settlements, "native end turn settles then extinguishes before next turn step" + step);
            evidence.Add(new { phase = "after", observation = PracticeObservation(practice) });
        }
        Check(ReferenceEquals(SaveManager.Instance.Progress, ordinaryProgress)
            && ProgressMemoryFingerprint(ordinaryProgress, ignoreCompleted: true) == progress,
            "completed practice restores original progress with only completion receipt added");
        CheckCompletedProgressFiles(ordinarySerialized);
        Check(ordinaryProgress.FtueCompleted.Contains(LibrarianPracticeSession.Completed), "completed real lesson records its profile-local completion receipt");
        CheckRunFiles(saveHashes, "ordinary save unchanged after completed practice");
        File.WriteAllText(Path.Combine(_output, "practice-observations.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        return initial;
    }

    internal static async Task Run()
    {
        _checks = 0; _screenshots = 0;
        _explanationScreenshots = 0; _taskScreenshots = 0; _progressWriterProbes = 0; _guiCardPlays = 0;
        _captureEvidence.Clear(); _progressEvidence.Clear(); _cancelEvidence.Clear(); _protectionEvidence.Clear(); _ritsuProgress = null;
        Check(System.Environment.GetEnvironmentVariable("LIBRARIAN_RUNTIME_AUDIT") == "1"
            && System.Environment.GetEnvironmentVariable("LIBRARIAN_ONBOARDING_AUDIT") == "1"
            && OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase), "explicit isolated validation profile");
        _output = System.Environment.GetEnvironmentVariable("LIBRARIAN_ONBOARDING_OUTPUT") ?? throw new InvalidOperationException("Onboarding output required");
        Check(Path.IsPathFullyQualified(_output) && DisplayServer.GetName() != "headless", "absolute native-rendering output");
        Directory.CreateDirectory(_output);
        if (System.Environment.GetEnvironmentVariable("LIBRARIAN_ONBOARDING_RESTART") == "read")
        {
            await RestartRead();
            return;
        }
        bool success = false;
        var originalProgress = SaveManager.Instance.Progress;
        string originalLanguage = LibrarianLanguage.Selected;
        var originalSize = Game.GetWindow().Size;
        bool originalCompact = LibrarianPreferences050.Current.CompactHoverTips;
        string preferencesPath = LibrarianPreferences050.FilePath;
        byte[]? originalPreferences = File.Exists(preferencesPath) ? File.ReadAllBytes(preferencesPath) : null;
        var suspended = new List<(Node node, bool enabled)>();
        try
        {
            MainFile.Logger.Info("ONBOARDING_AUDIT_BEGIN profile=" + OS.GetUserDataDir());
            var serialized = JsonSerializer.Deserialize<SerializableProgress>(JsonSerializer.Serialize(originalProgress.ToSerializable()))!;
            SaveManager.Instance.Progress = ProgressState.FromSerializable(serialized, new DeserializationContext());
            LibrarianUnlocks040.ApplyChoice(SaveManager.Instance.Progress, true);
            var menu = Game.MainMenu ?? throw new InvalidOperationException("Native main menu unavailable");
            foreach (var node in menu.GetChildren().Where(n => n is LibrarianUpdateNotice051 or LibrarianArchitectReview102))
            {
                suspended.Add((node, node.IsProcessing())); node.SetProcess(false);
            }
            Modals.Clear(); await Wait(3);
            bool globalTutorials = SaveManager.Instance.Progress.EnableFtues;
            SaveManager.Instance.Progress.EnableFtues = false;
            Receipts(true, true);
            var run = await Game.StartNewSingleplayerRun(ModelDb.Character<LibrarianCharacter>(), true,
                ActModel.GetDefaultList(), [], "LIBRARIAN_ONBOARDING", GameMode.Standard);
            var player = run.Players.Single();
            await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<TunnelerWeak>().ToMutable());
            await Until(() => CombatManager.Instance.IsInProgress && player.PlayerCombatState?.Hand.Cards.Count > 0,
                "ordinary fixture reaches actual first hand", 30);
            Check(Modals.OpenModal is null && player.Character is LibrarianCharacter,
                "ordinary Librarian run enters native combat without onboarding interruption");
            await Capture("combat-before-save");
            await SaveManager.Instance.SaveRun(null);
            var saved = SaveManager.Instance.LoadRunSave();
            Check(saved.Success && saved.SaveData is not null, "native ordinary run save can be read");
            var restored = RunState.FromSerializable(saved.SaveData!);
            Check(restored.Players.Single().Character.Id == player.Character.Id, "ordinary run save preserves actual Librarian model");
            await Game.ReturnToMainMenu();
            menu = await ReadyMenu(suspended);
            var ordinarySaveHashes = RunFileHashes();
            Check(ordinarySaveHashes["current_run.save"] is not null, "ordinary save byte hash exists before practice");
            File.WriteAllText(Path.Combine(_output, "ordinary-run-hashes-before.json"), JsonSerializer.Serialize(ordinarySaveHashes));
            SaveManager.Instance.Progress.EnableFtues = globalTutorials;
            DisplayServer.WindowSetSize(new Vector2I(1280, 720)); await Wait();
            Receipts(false, false);
            LibrarianLanguage.Select("zhs");
            var screen = await OpenSelection(menu);
            Check(Modals.OpenModal is null, "vanilla Ironclad selection has no Librarian prompt");
            await SelectLibrary(screen);
            var consent = await Modal(LibrarianTutorialMode.Consent);
            Check(consent.Elapsed < 5 && !consent.SkipReady && !consent.Panel.NoButton.IsEnabled, "real initial five-second refusal lock");
            Check(ButtonText(consent.Panel.NoButton).Contains('（'), "countdown displayed on refusal button");
            consent.No();
            consent.Panel.NoButton.EmitSignal(NClickableControl.SignalName.Released, consent.Panel.NoButton);
            await NativeAction(MegaInput.cancel);
            await Keyboard(Key.Escape);
            await MouseClick(consent.Panel.NoButton);
            Check(ReferenceEquals(Modals.OpenModal, consent) && !LibrarianOnboarding.Has(LibrarianOnboarding.Decision), "No direct signal and native cancel cannot bypass countdown");
            await Capture("consent-zhs-1280-countdown");
            await Until(() => consent.SkipReady, "actual five-second countdown completes", 8);
            Check(consent.Elapsed >= 5 && consent.Panel.NoButton.IsEnabled
                && ButtonText(consent.Panel.NoButton) == LibrarianOnboarding.Text("skip"), "refusal enabled and countdown removed after five seconds");
            consent.No(); await Wait();
            Check(Modals.OpenModal is null && LibrarianOnboarding.Has(LibrarianOnboarding.Decision)
                && !LibrarianOnboarding.Has(LibrarianOnboarding.Accepted), "decline records only character-local choice");
            await SelectLibrary(screen); await Wait(.8);
            Check(Modals.OpenModal is null, "declined tutorial is asked only once");

            SaveManager.Instance.Progress.EnableFtues = false;
            Receipts(false, false); await SelectLibrary(screen);
            consent = await Modal(LibrarianTutorialMode.Consent);
            var ordinaryProgress = SaveManager.Instance.Progress;
            var completeProtection = CaptureProgressProtection();
            await MouseClick(consent.Panel.YesButton);
            var practice = await Practice();
            Check(!SaveManager.Instance.Progress.EnableFtues, "character tutorial remains available without re-enabling global FTUE");
            Check(ordinaryProgress.FtueCompleted.Contains(LibrarianOnboarding.Decision)
                && ordinaryProgress.FtueCompleted.Contains(LibrarianOnboarding.Accepted), "acceptance receipts persist before real lesson");
            string initialPractice = await CompletePractice(practice, ordinaryProgress, ordinarySaveHashes);
            CheckRestoredProgressProtection(completeProtection, "completed lesson");
            Check(!SaveManager.Instance.Progress.EnableFtues, "real lesson completion leaves original disabled global tutorial switch unchanged");
            SaveManager.Instance.SaveProgressFile();
            var savedProgress = SaveManager.Instance.InitProgressData();
            Check(savedProgress.Success && savedProgress.SaveData is not null
                && savedProgress.SaveData.FtueCompleted.Contains(LibrarianOnboarding.Decision)
                && savedProgress.SaveData.FtueCompleted.Contains(LibrarianOnboarding.Accepted)
                && savedProgress.SaveData.FtueCompleted.Contains(LibrarianPracticeSession.Completed)
                && !savedProgress.SaveData.EnableFtues, "native progress file round-trip reads tutorial receipts and original global FTUE state");

            menu = await ReadyMenu(suspended);
            SaveManager.Instance.Progress.EnableFtues = globalTutorials;
            Receipts(false, false);
            screen = await OpenSelection(menu); await SelectLibrary(screen);
            consent = await Modal(LibrarianTutorialMode.Consent);
            ordinaryProgress = SaveManager.Instance.Progress;
            var interruptedProtection = CaptureProgressProtection();
            consent.Panel.YesButton.GrabFocus(); await NativeAction(MegaInput.select);
            practice = await Practice();
            string secondPractice = PracticeFingerprint(practice);
            Check(secondPractice == initialPractice, "fixed seed restarts identical initial hand orbs enemies and seed");
            File.WriteAllText(Path.Combine(_output, "practice-initial-repeatability.json"), JsonSerializer.Serialize(
                new { first = initialPractice, second = secondPractice, equal = true }, new JsonSerializerOptions { WriteIndented = true }));
            Check(!ReferenceEquals(SaveManager.Instance.Progress, ordinaryProgress), "second practice clones progress independently");
            var interruptedHashes = ProgressFileHashes();
            string interruptedProgress = ProgressMemoryFingerprint(ordinaryProgress);
            _ = ProgressFingerprint(ordinaryProgress);
            await ExerciseProgressWriters(interruptedHashes, "second lesson before interruption");
            var interruptedOverlay = (LibrarianPracticeOverlay)Modals.OpenModal!;
            await MouseClick(PracticeContinueButton(interruptedOverlay));
            await Until(() => practice.AwaitingAction && Modals.OpenModal is null, "second practice permits first actual card");
            var firstCard = practice.CurrentCard!;
            await NativePracticeAction(new PlayCardAction(firstCard, null), "interrupted practice first Spark");
            await Until(() => practice.Step == 1 && Modals.OpenModal is LibrarianPracticeOverlay, "interrupted practice observes real Spark");
            ((LibrarianPracticeOverlay)Modals.OpenModal!).Exit();
            await Until(() => !LibrarianPracticeSession.Active && Game.MainMenu is { } m && m.IsVisibleInTree(),
                "interrupted practice returns to menu", 35);
            CheckRestoredProgressProtection(interruptedProtection, "interrupted lesson");
            Check(ReferenceEquals(SaveManager.Instance.Progress, ordinaryProgress)
                && ProgressMemoryFingerprint(ordinaryProgress) == interruptedProgress,
                "mid-lesson exit restores original progress without gameplay changes");
            CheckProgressFiles(interruptedHashes, "mid-lesson exit preserves native progress and Ritsu mirror bytes");
            CheckRunFiles(ordinarySaveHashes, "ordinary save unchanged after interrupted practice");
            Check(SaveManager.Instance.Progress.EnableFtues == globalTutorials, "interrupted lesson keeps original global FTUE switch");
            menu = await ReadyMenu(suspended);
            screen = await OpenSelection(menu);

            Receipts(true, false, 1); await SelectLibrary(screen);
            var offer = await Modal(LibrarianTutorialMode.CompactOffer); offer.Yes(); await Wait();
            Check(LibrarianPreferences050.Current.CompactHoverTips && LibrarianOnboarding.Has(LibrarianOnboarding.CompactDecision), "first victory offer enables compact mode and records choice");
            LibrarianPreferences050.Load();
            Check(LibrarianPreferences050.Current.CompactHoverTips, "compact offer preference reloads from disk");
            await SelectLibrary(screen); await Wait(.8);
            Check(Modals.OpenModal is null, "victory offer does not repeat after choice");
            Receipts(true, false, 1); await SelectLibrary(screen);
            offer = await Modal(LibrarianTutorialMode.CompactOffer); offer.No(); await Wait();
            Check(!LibrarianPreferences050.Current.CompactHoverTips && LibrarianOnboarding.Has(LibrarianOnboarding.CompactDecision), "victory offer decline retains complete explanations");
            var compactProgress = SaveManager.Instance.InitProgressData();
            Check(compactProgress.Success && compactProgress.SaveData is not null
                && compactProgress.SaveData.FtueCompleted.Contains(LibrarianOnboarding.CompactDecision), "native progress file reads compact-choice receipt");
            Receipts(false, false, 0, 1); await SelectLibrary(screen); await Wait(.8);
            Check(Modals.OpenModal is null, "previous loss is treated as prior play and no victory offer");
            Receipts(false, false);
            SaveManager.Instance.Progress.GetOrCreateCharacterStats(ModelDb.Character<LibrarianCharacter>().Id).Playtime = 1;
            await SelectLibrary(screen); await Wait(.8);
            Check(Modals.OpenModal is null, "nonstandard prior playtime prevents a false first-play prompt");

            Receipts(false, false);
            var blocker = NGenericPopup.Create() ?? throw new InvalidOperationException("Native blocking popup unavailable");
            Modals.Add(blocker); blocker.GetNode<NVerticalPopup>("VerticalPopup").SetText("Native modal fixture", "Librarian onboarding must wait.");
            await SelectLibrary(screen); await Wait(.8);
            Check(ReferenceEquals(Modals.OpenModal, blocker) && !LibrarianOnboarding.Has(LibrarianOnboarding.Decision), "occupied native modal defers prompt without consuming receipt");
            Modals.Clear(); consent = await Modal(LibrarianTutorialMode.Consent);
            Check(!LibrarianOnboarding.Has(LibrarianOnboarding.Decision), "pending tutorial opens after occupied modal is released");
            Button(screen, false).Select(); Modals.Clear(); await Wait();
            blocker = NGenericPopup.Create() ?? throw new InvalidOperationException("Second native blocking popup unavailable");
            Modals.Add(blocker); await SelectLibrary(screen); await Wait(.8);
            Button(screen, false).Select(); Modals.Clear(); await Wait(.8);
            Check(Modals.OpenModal is null, "switching to vanilla character cancels pending prompt");
            await SelectLibrary(screen); consent = await Modal(LibrarianTutorialMode.Consent);
            Modals.Clear(); menu.SubmenuStack.Pop(); await Wait(.8);
            Check(Modals.OpenModal is null && !LibrarianOnboarding.Has(LibrarianOnboarding.Decision), "leaving selection without an answer consumes no receipt");
            screen = await OpenSelection(menu);
            await SelectLibrary(screen); consent = await Modal(LibrarianTutorialMode.Consent);
            await Until(() => consent.SkipReady, "selection navigation consent countdown completes", 8);
            consent.No(); await Wait();
            Check(SaveManager.Instance.Progress.EnableFtues == globalTutorials, "global tutorial switch unchanged after selection navigation");
            Receipts(true, true, 1);
            foreach (var resolution in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                DisplayServer.WindowSetSize(resolution); await Wait(.4);
                foreach (string language in new[] { "zhs", "eng" })
                {
                    LibrarianLanguage.Select(language);
                    foreach (var mode in new[] { LibrarianTutorialMode.Consent, LibrarianTutorialMode.CompactOffer })
                    {
                        var visual = LibrarianOnboarding.Show(mode) ?? throw new InvalidOperationException("Visual modal unavailable");
                        await Wait(.3);
                        await Capture($"{mode.ToString().ToLowerInvariant()}-{language}-{resolution.X}");
                        Modals.Clear(); await Wait();
                    }
                }
            }
            DisplayServer.WindowSetSize(new Vector2I(1280, 720)); await Wait();
            await CheckHoverTips();
            Check(ModSettingsRegistry.TryGetPage("Librarian", LibrarianSettings041.DisplayPageId, out var pageDefinition), "RitsuLib settings page registered");
            var toggle = pageDefinition!.Sections.Single(s => s.Id == "display").Entries.OfType<ToggleModSettingsEntryDefinition>().Single(e => e.Id == "compact_hover_tips");
            toggle.Binding.Write(true); toggle.Binding.Save();
            Check(LibrarianPreferences050.Current.CompactHoverTips, "RitsuLib toggle enables compact mode");
            toggle.Binding.Write(false); toggle.Binding.Save();
            Check(!LibrarianPreferences050.Current.CompactHoverTips, "RitsuLib toggle restores full mode");
            foreach (string language in new[] { "zhs", "eng" })
            {
                LibrarianLanguage.Select(language);
                var opened = await ModSettingsNavigator.OpenByIdsAsync("Librarian", LibrarianSettings041.DisplayPageId, sectionId: "display", options: new ModSettingsOpenOptions { Highlight = false, Focus = true });
                Check(opened.Success, "native RitsuLib display settings opened " + language); await Wait(.5);
                await Capture("ritsu-display-" + language);
                var submenu = Descendants(Game).OfType<RitsuModSettingsSubmenu>().Last(n => n.IsVisibleInTree());
                if (submenu.GetParent() is NSubmenuStack stack && ReferenceEquals(stack.Peek(), submenu)) stack.Pop();
                await Wait();
            }
            menu.SubmenuStack.Pop(); await Wait(.5);
            Receipts(true, true);
            SaveManager.Instance.Progress.EnableFtues = false;
            CheckRunFiles(ordinarySaveHashes, "ordinary save unchanged before normal reload");
            File.WriteAllText(Path.Combine(_output, "ordinary-run-hashes-after.json"), JsonSerializer.Serialize(RunFileHashes()));
            await RunManager.Instance.SetUpSavedSingleplayer(restored, saved.SaveData!);
            await Game.LoadRun(restored, saved.SaveData!.PreFinishedRoom); await Game.Transition.FadeIn();
            Check(RunManager.Instance.IsInProgress && restored.Players.Single().Character is LibrarianCharacter, "native saved run reloads Librarian");
            await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<TunnelerWeak>().ToMutable());
            var reloaded = restored.Players.Single();
            await Until(() => CombatManager.Instance.IsInProgress && reloaded.PlayerCombatState?.Hand.Cards.Count > 0, "reloaded run reaches actual hand", 30);
            await Capture("combat-after-reload");
            Check(LibrarianOnboarding.Has(LibrarianOnboarding.Decision) && LibrarianOnboarding.Has(LibrarianOnboarding.CompactDecision), "profile choices survive menu and run reload");
            MainFile.Logger.Info("SAVE_RELOAD_ONBOARDING_AUDIT_PASS saves=1 reloads=1 nativeCombatVisits=2 original_profile_untouched=True");
            await Game.ReturnToMainMenu(); await Wait();
            Check(_explanationScreenshots == 48 && _taskScreenshots == 14 && _guiCardPlays == 1 && _cancelEvidence.Count == 2,
                "all twelve bilingual lesson pages at both real dimensions, task hints, GUI drag and controlled Cancel covered");
            File.WriteAllText(Path.Combine(_output, "onboarding-audit.json"), JsonSerializer.Serialize(new
            {
                checks = _checks, screenshots = _screenshots, menu = true, tutorialSteps = 12,
                practiceStarts = 2, practiceCompletions = 1, practiceExits = 1, nativePlayerActions = true,
                guiMouseCardPlays = _guiCardPlays, controlledAcceptedCancelRetries = _cancelEvidence.Count,
                strictDimensionLessonScreenshots = _explanationScreenshots, operationTaskHintScreenshots = _taskScreenshots,
                progressFilesTracked = 4, guardedProgressWriterProbes = _progressWriterProbes,
                ritsuWriterReflection = true, interruptedProgressByteHashesPreserved = true,
                completedProgressSemanticsPreserved = true, ordinarySaveHashesPreserved = true,
                progressReloads = 2, saves = 1, reloads = 1, realMultiplayer = false
            }, new JsonSerializerOptions { WriteIndented = true }));
            MainFile.Logger.Info($"ONBOARDING_AUDIT_PASS checks={_checks} screenshots={_screenshots} tutorialSteps=12 practiceStarts=2 nativePlayerActions=True guiMouseCardPlays={_guiCardPlays} controlledCancelRetries={_cancelEvidence.Count} strictDimensionLessonScreenshots={_explanationScreenshots} operationTaskHintScreenshots={_taskScreenshots} progressFiles=4 progressWriterProbes={_progressWriterProbes} ordinarySaveHashesPreserved=True nativeProgressRead=True save=True reload=True realMultiplayer=False");
            success = true;
        }
        catch (Exception error) { MainFile.Logger.Error($"ONBOARDING_AUDIT_FAIL checks={_checks} screenshots={_screenshots} {error}"); throw; }
        finally
        {
            if (LibrarianPracticeSession.Instance is { } activePractice && LibrarianPracticeSession.Active)
                await activePractice.ExitAsync();
            Modals.Clear();
            SaveManager.Instance.Progress = originalProgress; SaveManager.Instance.SaveProgressFile();
            LibrarianLanguage.Select(originalLanguage);
            LibrarianPreferences050.Current.CompactHoverTips = originalCompact;
            if (originalPreferences is null) { if (File.Exists(preferencesPath)) File.Delete(preferencesPath); }
            else File.WriteAllBytes(preferencesPath, originalPreferences);
            DisplayServer.WindowSetSize(originalSize);
            foreach (var entry in suspended) if (GodotObject.IsInstanceValid(entry.node)) entry.node.SetProcess(entry.enabled);
            if (success && System.Environment.GetEnvironmentVariable("LIBRARIAN_ONBOARDING_RESTART") == "write")
            {
                LibrarianOnboarding.Record(LibrarianOnboarding.Decision);
                LibrarianOnboarding.Record(LibrarianOnboarding.CompactDecision);
                LibrarianPreferences050.Current.CompactHoverTips = true;
                LibrarianPreferences050.Save();
                MainFile.Logger.Info("ONBOARDING_RESTART_WRITE_PASS receipts=2 compact=True isolated_fixture=True");
            }
            // Returning to the menu starts native asynchronous character preloading.
            // Let it finish before shutdown rather than quitting inside restoration.
            await Wait(3);
            Game.Quit();
        }
    }
    private static async Task RestartRead()
    {
        try
        {
            Check(LibrarianOnboarding.Has(LibrarianOnboarding.Decision) && LibrarianOnboarding.Has(LibrarianOnboarding.CompactDecision), "new process loaded both profile receipts from disk");
            Check(LibrarianPreferences050.Current.CompactHoverTips, "new process loaded compact preference from disk");
            var menu = Game.MainMenu ?? throw new InvalidOperationException("Main menu unavailable");
            foreach (var node in menu.GetChildren().Where(n => n is LibrarianUpdateNotice051 or LibrarianArchitectReview102)) node.SetProcess(false);
            Modals.Clear(); await Wait(3);
            LibrarianUnlocks040.ApplyChoice(SaveManager.Instance.Progress, true);
            var screen = await OpenSelection(menu);
            await SelectLibrary(screen); await Wait(1);
            Check(Modals.OpenModal is null, "new process suppresses both answered prompts on character selection");
            await Capture("restart-selection-suppressed");
            menu.SubmenuStack.Pop(); await Wait(3);
            MainFile.Logger.Info($"ONBOARDING_RESTART_READ_PASS checks={_checks} receipts=2 compact=True selectionSuppressed=True");
        }
        catch (Exception error) { MainFile.Logger.Error("ONBOARDING_RESTART_AUDIT_FAIL " + error); throw; }
        finally { Game.Quit(); }
    }
}
