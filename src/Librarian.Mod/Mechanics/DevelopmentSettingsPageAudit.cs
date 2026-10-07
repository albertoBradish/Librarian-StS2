using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using STS2RitsuLib.Settings;

namespace Librarian.Mechanics;

/// <summary>Opt-in native settings, exports, preview playback and causal presentation captures.</summary>
internal static class DevelopmentSettingsPageAudit
{
    private static int _checks;
    private static string? _originalPreferences;
    private static string? _originalLanguageFile;
    private static string _originalLanguage = "";
    private static string _expectedPreferences = "";
    private static object? _lastClick;
    private static readonly List<object> Captures = [];
    private static NGame Game => NGame.Instance!;
    private static bool CaptureOnly => System.Environment.GetEnvironmentVariable("LIBRARIAN_SETTINGS_CAPTURE_ONLY") == "1";
    private static bool Restart => System.Environment.GetEnvironmentVariable("LIBRARIAN_SETTINGS_PHASE") == "restart";
    private static string Output => System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT")
        ?? throw new InvalidOperationException("Settings audit output is required");
    private static readonly (string Page, string Section)[] Pages =
    [ ("librarian-settings", "language"), ("librarian-display", "display"),
      ("librarian-effects", "effects"), ("librarian-effects", "audio"),
      ("librarian-tools", "translations"), ("librarian-tools", "diagnostics"),
      ("librarian-tools", "progression"), ("librarian-tools", "defaults"), ("librarian-tools", "debug") ];

    private static void Check(bool ok, string label)
    {
        if (!ok)
        {
            try
            {
                Directory.CreateDirectory(Output);
                string name = "capture-fail-" + _checks;
                using var image = Game.GetViewport().GetTexture().GetImage();
                image.SavePng(Path.Combine(Output, name + ".png"));
                File.WriteAllText(Path.Combine(Output, name + ".json"), JsonSerializer.Serialize(new { label, last_click = _lastClick },
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* Keep the original assertion as the failure owner. */ }
            throw new InvalidOperationException("Settings page: " + label);
        }
        _checks++;
        MainFile.Logger.Info("SETTINGS_PAGE_CHECK_PASS " + label);
    }
    private static async Task Wait(double seconds = .25) => await Game.ToSignal(
        Game.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private static IEnumerable<Node> Desc(Node root)
    {
        foreach (var child in root.GetChildren())
        { yield return child; foreach (var item in Desc(child)) yield return item; }
    }
    private static string? ReadOptional(string path) => File.Exists(path) ? File.ReadAllText(path) : null;
    private static void RestoreFile(string path, string? value)
    { if (value is null) File.Delete(path); else File.WriteAllText(path, value); }
    private static void CloseSettings()
    {
        foreach (var menu in Desc(Game).OfType<RitsuModSettingsSubmenu>().Where(m => m.IsVisibleInTree()).ToArray())
            if (menu.GetParent() is NSubmenuStack stack && ReferenceEquals(stack.Peek(), menu)) stack.Pop();
    }
    private static async Task<RitsuModSettingsSubmenu> Open(string page, string section, string? entry = null)
    {
        var result = await ModSettingsNavigator.OpenByIdsAsync("Librarian", page, sectionId: section, entryId: entry,
            options: new ModSettingsOpenOptions { Highlight = false, Focus = true });
        Check(result.Success, "native navigate " + page + "/" + section + "/" + entry);
        await Wait(.35);
        return Desc(Game).OfType<RitsuModSettingsSubmenu>().Last(n => n.IsVisibleInTree());
    }
    private static async Task Shot(string name)
    {
        Directory.CreateDirectory(Output);
        await Game.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = Game.GetViewport().GetTexture().GetImage();
        Check(image.SavePng(Path.Combine(Output, name + ".png")) == Error.Ok, "native screenshot " + name);
    }
    private static async Task Click(Control control)
    {
        var ancestors = new List<Control>();
        for (Node? parent = control.GetParent(); parent is not null; parent = parent.GetParent())
        {
            if (parent is Control ancestor) ancestors.Add(ancestor);
            if (parent is ScrollContainer scroll) scroll.EnsureControlVisible(control);
        }
        await Wait(.15);
        Rect2 Clip()
        {
            var clip = control.GetViewportRect();
            foreach (var ancestor in ancestors.Where(n => n.ClipContents)) clip = clip.Intersection(ancestor.GetGlobalRect());
            return clip;
        }
        bool Visible()
        {
            var clip = Clip(); var rect = control.GetGlobalRect();
            return control.IsVisibleInTree() && rect.Position.Y >= clip.Position.Y - 1 && rect.End.Y <= clip.End.Y + 1
                && rect.Position.X >= clip.Position.X - 1 && rect.End.X <= clip.End.X + 1;
        }
        static float[] Coordinates(Rect2 rect) => [rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y];
        void Diagnostic(string stage, int wheels) => _lastClick = new {
            stage, wheel_inputs = wheels, target = control.GetPath().ToString(), target_type = control.GetType().FullName,
            target_rect = Coordinates(control.GetGlobalRect()), effective_clip = Coordinates(Clip()),
            ancestors = ancestors.Select(n => new { path = n.GetPath().ToString(), type = n.GetType().FullName,
                rect = Coordinates(n.GetGlobalRect()), n.ClipContents }).ToArray()
        };
        int wheelInputs = 0;
        while (!Visible() && wheelInputs < 18)
        {
            Diagnostic("before-native-wheel", wheelInputs);
            var clip = Clip();
            var wheelPoint = control.GetViewport().GetStretchTransform() * clip.GetCenter();
            Input.WarpMouse(wheelPoint);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = wheelPoint, GlobalPosition = wheelPoint });
            var direction = control.GetGlobalRect().GetCenter().Y > clip.GetCenter().Y ? MouseButton.WheelDown : MouseButton.WheelUp;
            Input.ParseInputEvent(new InputEventMouseButton { Position = wheelPoint, GlobalPosition = wheelPoint, ButtonIndex = direction, Pressed = true, Factor = 1 });
            wheelInputs++; await Wait(.18);
        }
        Diagnostic("before-native-click", wheelInputs);
        Check(Visible(), "actual mouse target lies within native clipping bounds " + control.Name);
        var point = control.GetViewport().GetStretchTransform() * control.GetGlobalTransformWithCanvas() * (control.Size * .5f);
#if LIBRARIAN_BETA
        NControllerManager.Instance!.ForceMouseMode();
#endif
        Input.WarpMouse(point);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = point, GlobalPosition = point });
        await Wait(.10);
        foreach (bool pressed in new[] { true, false })
            Input.ParseInputEvent(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = pressed });
        await Wait(.4);
        Input.WarpMouse(new Vector2(12, 12));
        Input.ParseInputEvent(new InputEventMouseMotion { Position = new(12, 12), GlobalPosition = new(12, 12) });
    }
    private static bool HasText(Node node, string text) => Desc(node).Any(n => n is Label l && l.Text == text
        || n is RichTextLabel r && r.GetParsedText() == text);
    private static Button ActionButton(Node menu, string key, string buttonKey)
    {
        string title = LibrarianLanguage.Text("main_menu_ui", "LIBRARIAN_SETTINGS." + key);
        string text = LibrarianLanguage.Text("main_menu_ui", "LIBRARIAN_SETTINGS." + buttonKey);
        var rows = Desc(menu).OfType<Control>().Where(n => HasText(n, title) && Desc(n).OfType<Button>().Any(b => b.Text == text))
            .OrderBy(n => Desc(n).Count()).ToArray();
        Check(rows.Length > 0, "native action row " + key);
        return Desc(rows[0]).OfType<Button>().Single(b => b.Text == text);
    }
    private static void Bounds(Control control, string label)
    {
        var rect = control.GetGlobalRect(); var viewport = control.GetViewportRect();
        Check(rect.Position.X >= viewport.Position.X - 2 && rect.Position.Y >= viewport.Position.Y - 2
            && rect.End.X <= viewport.End.X + 2 && rect.End.Y <= viewport.End.Y + 2, "viewport bounds " + label);
    }

    internal static async Task Menu(NMainMenu menu)
    {
        Check(OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase)
            && System.Environment.GetEnvironmentVariable("LIBRARIAN_RUNTIME_AUDIT") == "1", "isolated explicit opt-in profile");
        Check(DisplayServer.GetName() != "headless", "native rendering available");
        Directory.CreateDirectory(Output);
        // Let this isolated process's timed update toast finish naturally so it
        // cannot cover settings screenshots. Do not change its saved receipt.
        await Wait(LibrarianUpdateNotice051.ToastDurationSeconds + 1);
        if (Restart)
        {
            string expected = File.ReadAllText(Path.Combine(Output, "persisted-preferences.json"));
            Check(JsonSerializer.Serialize(LibrarianPreferences050.Current) == expected, "new process reloads saved preferences");
            await Open("librarian-display", "display", "locked_orb_display");
            await Shot("settings-restart-persisted"); CloseSettings();
            MainFile.Logger.Info("SETTINGS_PAGE_RESTART_PASS"); return;
        }
        _originalPreferences = ReadOptional(LibrarianPreferences050.FilePath);
        _originalLanguageFile = ReadOptional(LibrarianLanguage.FilePath);
        _originalLanguage = LibrarianLanguage.Selected;
        foreach (string pageId in Pages.Select(p => p.Page).Distinct())
        {
            Check(ModSettingsRegistry.TryGetPage("Librarian", pageId, out var page), "registered " + pageId);
            Check(pageId == "librarian-settings" || page!.ParentPageId == "librarian-settings", "page hierarchy " + pageId);
            Check(page!.Sections.SelectMany(s => s.Entries).Select(e => e.Id).Distinct().Count()
                == page.Sections.SelectMany(s => s.Entries).Count(), "unique stable entry IDs " + pageId);
        }
        Vector2I windowSize = DisplayServer.WindowGetSize();
        try
        {
            foreach (string language in new[] { "zhs", "eng" })
            {
                LibrarianLanguage.Select(language);
                foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
                {
                    DisplayServer.WindowSetSize(size); await Wait(.4);
                    Check(DisplayServer.WindowGetSize() == size, "actual settings window " + size);
                    foreach (var (page, section) in Pages)
                    {
                        var settings = await Open(page, section);
                        Bounds(settings, page + "/" + section + " " + language + " " + size);
                        await Shot($"settings-{language}-{size.X}x{size.Y}-{page}-{section}");
                        CloseSettings();
                    }
                }
            }
            if (!CaptureOnly)
            {
                foreach (string language in new[] { "zhs", "eng" })
                {
                    var previewSize = language == "zhs" ? new Vector2I(1280, 720) : new Vector2I(1920, 1080);
                    DisplayServer.WindowSetSize(previewSize); await Wait(.4);
                    Check(DisplayServer.WindowGetSize() == previewSize, "actual preview window " + language + " " + previewSize);
                    LibrarianLanguage.Select(language); await Previews(language);
                }
                await SoundPreview();
            }
            LibrarianLanguage.Select("zhs");
            if (!CaptureOnly) await Exports();
            Check(LibrarianPreferences050.Current.SoundVolume >= 0 && LibrarianPreferences050.Current.SoundVolume <= 100,
                "audio controls retain normalized settings");
        }
        finally { CloseSettings(); DisplayServer.WindowSetSize(windowSize); }
        MainFile.Logger.Info("SETTINGS_PAGE_MENU_PASS");
    }

    private static async Task Previews(string language)
    {
        foreach (string pageId in new[] { "librarian-display", "librarian-effects" })
        {
            ModSettingsRegistry.TryGetPage("Librarian", pageId, out var page);
            foreach (var section in page!.Sections)
                foreach (var entry in section.Entries.OfType<CustomModSettingsEntryDefinition>().Where(e => e.Id.EndsWith("_preview")))
                {
                    var settings = await Open(pageId, section.Id, entry.Id);
                    string key = entry.Id[..^8];
                    var preview = Desc(settings).OfType<LibrarianSettingsPreview>().Single(n => n.SettingKey == key);
                    var button = preview.GetNode<Button>("PreviewActions/ExpandPreview");
                    Check(!preview.Expanded && !preview.Playing, "preview starts collapsed " + key);
                    await Click(button);
                    Check(preview.Expanded && preview.Loaded && !preview.Playing, "preview loads without autoplay " + key);
                    Check(preview.GetNode<Label>("PreviewDetails/PreviewCaption").Text ==
                        LibrarianLanguage.Text("main_menu_ui", "LIBRARIAN_SETTINGS.preview_" + key), "localized preview caption " + key);
                    var rendered = preview.GetNode<LibrarianSettingsPreviewImage>("PreviewDetails/PreviewImage");
                    if (rendered.Animated)
                    {
                        await Click(preview.GetNode<Button>("PreviewActions/PlayPreview"));
                        Check(preview.Playing, "actual preview playback action " + key);
                        int frame = rendered.Frame; await Wait(.3);
                        Check(rendered.Frame != frame, "native preview advances atlas frame " + key);
                    }
                    await Shot("settings-preview-" + key + "-" + language);
                    await Click(button);
                    Check(!preview.Expanded && !preview.Playing && !rendered.IsProcessing(), "preview collapse stops playback " + key);
                    CloseSettings();
                }
        }
        MainFile.Logger.Info("SETTINGS_PAGE_PREVIEWS_PASS");
    }

    private static async Task SoundPreview()
    {
        ModSettingsRegistry.TryGetPage("Librarian", "librarian-effects", out var page);
        var audio = page!.Sections.Single(s => s.Id == "audio");
        var enabled = audio.Entries.OfType<ToggleModSettingsEntryDefinition>().Single(e => e.Id == "orb_sounds").Binding;
        var volume = audio.Entries.OfType<IntSliderModSettingsEntryDefinition>().Single(e => e.Id == "volume").Binding;
        bool originalEnabled = enabled.Read(); int originalVolume = volume.Read();
        try
        {
            foreach (var (soundOn, level, expected) in new[] { (true, 60, 1L), (false, 60, 0L), (true, 0, 0L) })
            {
                enabled.Write(soundOn); enabled.Save(); volume.Write(level); volume.Save();
                var settings = await Open("librarian-effects", "audio", "sound_preview");
                await Wait(.5);
                long before = LibrarianOrbAudio.Submitted;
                await Click(ActionButton(settings, "sound_preview", "listen"));
                Check(LibrarianOrbAudio.Submitted == before + expected,
                    $"actual audio preview submission enabled={soundOn} volume={level}");
                if (expected == 1) await Shot("settings-sound-preview");
                CloseSettings();
            }
            MainFile.Logger.Info("SETTINGS_PAGE_AUDIO_PREVIEW_PASS nativeSubmitted=True humanListening=False");
        }
        finally
        { enabled.Write(originalEnabled); enabled.Save(); volume.Write(originalVolume); volume.Save(); }
    }

    private static async Task Exports()
    {
        const string literal = "[b]下载[/b]";
        var plain = new RichTextLabel { Visible = false, BbcodeEnabled = true, Text = LibrarianSettings041.PlainText(literal) };
        Game.AddChild(plain);
        try
        { await Wait(.1); Check(plain.GetParsedText() == literal, "native rich text preserves escaped path brackets"); }
        finally { plain.QueueFree(); }
        string downloads = LibrarianExportDestination.DownloadsDirectory;
        string godotDownloads = OS.GetSystemDir(OS.SystemDir.Downloads);
        string normalizedDownloads = Path.GetFullPath(downloads);
        string normalizedGodotDownloads = Path.GetFullPath(godotDownloads);
        File.WriteAllText(Path.Combine(Output, "downloads-paths.json"), JsonSerializer.Serialize(new {
            downloads, godot_downloads = godotDownloads, normalized_downloads = normalizedDownloads,
            normalized_godot_downloads = normalizedGodotDownloads
        }, new JsonSerializerOptions { WriteIndented = true }));
        Check(Path.IsPathFullyQualified(downloads) && string.Equals(normalizedDownloads, normalizedGodotDownloads,
            StringComparison.OrdinalIgnoreCase), "native Downloads default");
        var templates = await Open("librarian-tools", "translations", "language_templates");
        int attempts = LibrarianExportDestination.OpenAttemptCount;
        await Click(ActionButton(templates, "language_templates", "export_button"));
        string directory = LibrarianLanguage.TemplateDirectory;
        Check(Path.GetDirectoryName(directory) == downloads && Directory.Exists(directory), "actual template export is under Downloads");
        Check(!directory.EndsWith(".partial", StringComparison.OrdinalIgnoreCase), "completed template export is atomically finalized");
        Check(LibrarianExportDestination.OpenAttemptCount == attempts + 1
            && LibrarianExportDestination.LastOpenedDirectory == directory
            && LibrarianExportDestination.LastShellOpenResult == Error.Ok, "template export opens its folder through OS");
        Check(Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories).Any(), "template export contains translations");
        await Shot("settings-template-export");
        var hashes = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(p => p,
            p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
        string packPath = LibrarianLanguage.PackDirectory;
        string fixture = Path.Combine(Output, "template-export-fixture"); Directory.CreateDirectory(fixture);
        LibrarianLanguage.ExportTemplates(false, fixture);
        Check(LibrarianLanguage.TemplateDirectory != directory && LibrarianLanguage.PackDirectory == packPath,
            "template export remains separate from custom language loading");
        Check(hashes.All(p => File.Exists(p.Key) && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p.Key))) == p.Value),
            "another export preserves all previous template bytes");
        CloseSettings();
        var diagnostics = await Open("librarian-tools", "diagnostics", "export");
        var before = Directory.Exists(downloads) ? Directory.EnumerateFiles(downloads, "Librarian-logs-*.zip").ToHashSet() : new HashSet<string>();
        attempts = LibrarianExportDestination.OpenAttemptCount;
        await Click(ActionButton(diagnostics, "export", "export_button"));
        for (int i = 0; i < 200 && LibrarianExportDestination.OpenAttemptCount == attempts; i++) await Wait(.1);
        var created = Directory.EnumerateFiles(downloads, "Librarian-logs-*.zip").Where(p => !before.Contains(p)).ToArray();
        Check(created.Length == 1, "actual log action creates one Downloads ZIP");
        Check(LibrarianExportDestination.OpenAttemptCount == attempts + 1
            && LibrarianExportDestination.LastOpenedDirectory == downloads
            && LibrarianExportDestination.LastShellOpenResult == Error.Ok, "log export opens Downloads through OS");
        using (var archive = ZipFile.OpenRead(created[0]))
        {
            Check(archive.GetEntry("versions.json") is not null && archive.GetEntry("export-summary.json") is not null,
                "log ZIP contains version metadata and summary");
            Check(archive.Entries.Any(e => e.FullName.StartsWith("logs/"))
                && archive.Entries.All(e => !e.FullName.Contains("save", StringComparison.OrdinalIgnoreCase)), "log ZIP contains logs and no saves");
        }
        Check(LibrarianSettings041.ExportStatus.Contains(created[0]), "actual export status shows full saved path");
        File.WriteAllText(Path.Combine(Output, "exports.json"), JsonSerializer.Serialize(new { downloads, log = created[0], templates = directory,
            LibrarianExportDestination.LastOpenedDirectory, LibrarianExportDestination.LastShellOpenResult,
            LibrarianExportDestination.OpenAttemptCount }, new JsonSerializerOptions { WriteIndented = true }));
        await Shot("settings-log-export"); CloseSettings();
        MainFile.Logger.Info("SETTINGS_PAGE_EXPORTS_PASS");
    }

    private static string Rules(Player player)
    {
        var s = LibrarianRuntime.Get(player);
        return JsonSerializer.Serialize(new { orbs = s.Orbs.Snapshot(), s.Orbs.SettlementsThisTurn, s.Orbs.SettlementsThisCombat,
            ledger = s.Orbs.BlockLedger.Snapshot(), s.Waves.Amount, player.Creature.CurrentHp, player.Creature.MaxHp, player.Creature.Block });
    }
    private static Rect2 CharacterCrop(Player player)
    {
        var rect = NCombatRoom.Instance!.GetCreatureNode(player.Creature)!.Hitbox.GetGlobalRect();
        return rect.GrowIndividual(150, 210, 170, 140).Intersection(Game.GetViewport().GetVisibleRect());
    }
    private static async Task Sequence(Player player, string key, string variant, Rect2 crop, int count = 24, Action<int>? eventPerFrame = null)
    {
        string rules = Rules(player);
        var frames = new List<object>();
        var timer = Stopwatch.StartNew();
        int[]? pixelCrop = null;
        for (int i = 0; i < count; i++)
        {
            eventPerFrame?.Invoke(i);
            await Wait(i == 0 ? .08 : .125);
            await Game.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = Game.GetViewport().GetTexture().GetImage();
            string relative = $"raw/{key}/{variant}/frame-{i:000}.png";
            string absolute = Path.Combine(Output, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            Check(image.SavePng(absolute) == Error.Ok, "native media frame " + key + "/" + variant + "/" + i);
            var visible = Game.GetViewport().GetVisibleRect();
            var scale = new Vector2(image.GetWidth() / visible.Size.X, image.GetHeight() / visible.Size.Y);
            var rect = new Rect2I((Vector2I)(crop.Position * scale), (Vector2I)(crop.Size * scale));
            pixelCrop = [rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y];
            frames.Add(new { path = relative, time_ms = timer.Elapsed.TotalMilliseconds, width = image.GetWidth(), height = image.GetHeight() });
        }
        Check(Rules(player) == rules, "presentation capture preserves combat rules " + key + "/" + variant);
        Captures.Add(new { key, variant, crop = pixelCrop, preferences = JsonSerializer.SerializeToElement(LibrarianPreferences050.Current),
            rule_state = rules, frames });
    }

    internal static async Task Combat(Player player)
    {
        Check(NCombatRoom.Instance is not null && player.Creature.CombatState is not null, "real new run enters combat");
        LibrarianLanguage.Select("zhs"); DisplayServer.WindowSetSize(new Vector2I(1280, 720)); await Wait(.4);
        var session = LibrarianRuntime.Get(player);
        var context = new ThrowingPlayerChoiceContext();
        foreach (var (kind, value) in new[] { (OrbKind.Fire, 9), (OrbKind.Tide, 12), (OrbKind.Growth, 16) })
        {
            await LibrarianRuntime.Dispatch(session, context, session.Orbs.Unlock(kind));
            await LibrarianRuntime.Dispatch(session, context, session.Orbs.LoseAll(kind, OrbScope.All));
            await LibrarianRuntime.Dispatch(session, context, session.Orbs.Strengthen(kind, value, OrbScope.All));
        }
        session.Waves.Add(18); LibrarianOrbPanel.Refresh(session); await Wait(1);
        var display = LibrarianOrbPanel.GetDisplay(session)!;
        var normalFast = SaveManager.Instance.PrefsSave.FastMode;
        SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
        try
        {
            if (CaptureOnly)
            {
                Rect2 crop = CharacterCrop(player);
                foreach (var (key, property) in new[] { ("circle", nameof(LibrarianPreferences050.MagicCircle)),
                    ("idle", nameof(LibrarianPreferences050.OrbIdle)), ("particles", nameof(LibrarianPreferences050.Particles)),
                    ("reduced_motion", nameof(LibrarianPreferences050.ReducedMotion)), ("wave", nameof(LibrarianPreferences050.WaveBar)) })
                {
                    foreach (bool on in new[] { false, true })
                    {
                        LibrarianPreferences050.Reset();
                        typeof(LibrarianPreferences050).GetProperty(property)!.SetValue(LibrarianPreferences050.Current, on);
                        await Wait(.4);
                        await Sequence(player, key, on ? "on" : "off", crop, key is "circle" or "wave" ? 1 : 24);
                    }
                }
                foreach (var mode in Enum.GetValues<LibrarianLockedOrbDisplayMode>())
                {
                    LibrarianPreferences050.Reset(); LibrarianPreferences050.Current.LockedOrbDisplay = mode;
                    if (session.Orbs.LockedTurns(OrbKind.Fire) == 0)
                        await LibrarianRuntime.Dispatch(session, context, session.Orbs.Lock(OrbKind.Fire, 2));
                    await Wait(.5); await Sequence(player, "locked_orb_display", mode.ToString(), crop, 1);
                }
                await LibrarianRuntime.Dispatch(session, context, session.Orbs.Unlock(OrbKind.Fire)); await Wait(.7);
                foreach (bool on in new[] { false, true })
                {
                    LibrarianPreferences050.Reset(); LibrarianPreferences050.Current.TideBlockFeedback = on;
                    await Wait(1.9);
                    await Sequence(player, "tide_block_feedback", on ? "on" : "off", crop, 8,
                        i => { if (i == 0) display.ShowTideChange(12, false); });
                }
                foreach (bool on in new[] { false, true })
                {
                    LibrarianPreferences050.Reset(); LibrarianPreferences050.Current.OrbEffects = on;
                    var travel = LibrarianOrbVfx.Travel(session, OrbKind.Fire, receivingOrb: OrbKind.Growth);
                    await Sequence(player, "orb_effects", on ? "on" : "off", crop, 8,
                        i => { if (i % 3 == 0) _ = LibrarianOrbVfx.Travel(session, OrbKind.Fire, receivingOrb: OrbKind.Growth); });
                    await travel; await Wait(.7);
                }
                foreach (var (key, low, high) in new[] { ("card_effects", 0, 1), ("opacity", 20, 100), ("limit", 4, 40) })
                    foreach (int value in new[] { low, high })
                    {
                        LibrarianPreferences050.Reset();
                        if (key == "card_effects") LibrarianPreferences050.Current.CardEffects = value == 1;
                        if (key == "opacity") LibrarianPreferences050.Current.EffectOpacity = value;
                        if (key == "limit") LibrarianPreferences050.Current.EffectLimit = value;
                        await Wait(.7); LibrarianCardVfx050.ResetCounters();
                        await Sequence(player, key, key == "card_effects" ? value == 1 ? "on" : "off" : value.ToString(), crop, 24,
                            i => { if (i % 4 == 0) for (int n = 0; n < (key == "limit" ? 12 : 1); n++)
                                LibrarianCardVfx050.Emit(player.Creature, new(SpellElement050.Fire, SpellShape050.Ember), "impact", "SettingsCapture", player, 75); });
                        if (key == "limit") Check(LibrarianCardVfx050.Peak <= value && (value != 4 || LibrarianCardVfx050.Dropped > 0), "effect limit gates actual production spawns " + value);
                    }
                await HoverCapture(player);
                File.WriteAllText(Path.Combine(Output, "captures.json"), JsonSerializer.Serialize(new {
                    game_version =
#if LIBRARIAN_BETA
                        NGame.GetGameVersion(),
#else
                        MegaCrit.Sts2.Core.Debug.ReleaseInfoManager.Instance.ReleaseInfo?.Version,
#endif
                    game_assembly = typeof(SaveManager).Assembly.GetName().Version?.ToString(),
                    mod_assembly_sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(LibrarianSettings041).Assembly.Location))),
                    capture_log = System.Environment.GetEnvironmentVariable("LIBRARIAN_SETTINGS_CAPTURE_LOG"),
                    mode = "native settings toggles and production cosmetic events in isolated actual combat", records = Captures
                }, new JsonSerializerOptions { WriteIndented = true }));
                MainFile.Logger.Info("SETTINGS_PAGE_NATIVE_CAPTURES_PASS records=" + Captures.Count);
            }
            else
            {
                string before = Rules(player);
                await Open("librarian-display", "display", "wave"); await Shot("settings-in-combat"); CloseSettings();
                Check(Rules(player) == before, "opening settings preserves combat rules");
            }
            LibrarianPreferences050.Reset();
            LibrarianPreferences050.Current.SoundVolume = 70;
            LibrarianPreferences050.Current.ReducedMotion = true;
            LibrarianPreferences050.Current.LockedOrbDisplay = LibrarianLockedOrbDisplayMode.ValueAndTurns;
            LibrarianPreferences050.Save(); LibrarianPreferences050.Load();
            _expectedPreferences = JsonSerializer.Serialize(LibrarianPreferences050.Current);
            Check(LibrarianPreferences050.Current.SoundVolume == 70 && LibrarianPreferences050.Current.ReducedMotion
                && LibrarianPreferences050.Current.LockedOrbDisplay == LibrarianLockedOrbDisplayMode.ValueAndTurns, "all preference kinds persist to disk");
            File.WriteAllText(Path.Combine(Output, "persisted-preferences.json"), _expectedPreferences);
            await Shot("settings-pre-save-combat");
        }
        finally { SaveManager.Instance.PrefsSave.FastMode = normalFast; }
        MainFile.Logger.Info("SETTINGS_PAGE_COMBAT_PASS");
    }

    private static async Task HoverCapture(Player player)
    {
        var layer = new CanvasLayer { Layer = 120 }; Game.AddChild(layer);
        var panel = new Control(); layer.AddChild(panel);
        var card = player.RunState.CreateCard(ModelDb.Card<Spark>(), player);
        var node = PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>();
        panel.AddChild(node); node.Model = card;
        node.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal); node.Position = new(300, 410); node.Scale = Vector2.One * .8f;
        int full = 0;
        try
        {
            foreach (bool on in new[] { false, true })
            {
                LibrarianPreferences050.Reset(); LibrarianPreferences050.Current.CompactHoverTips = on;
                var owner = new Control { Position = new(430, 210), Size = new(20, 20) }; panel.AddChild(owner);
                var tips = card.HoverTips.ToArray();
                if (!on) full = tips.Length; else Check(tips.Length < full, "compact mode reduces native nested tooltips");
                var set = NHoverTipSet.CreateAndShow(owner, tips, HoverTipAlignment.Right);
                Check(set is not null, "actual native hover renderer " + on);
                set!.GetParent().RemoveChild(set); panel.AddChild(set);
                await Wait(.3);
                await Sequence(player, "compact_hover_tips", on ? "on" : "off", new Rect2(180, 130, 1030, 550), 1);
                NHoverTipSet.Remove(owner); owner.QueueFree(); await Wait(.15);
            }
        }
        finally { layer.QueueFree(); await Wait(.2); }
    }

    internal static async Task AfterReload(Player player)
    {
        LibrarianPreferences050.Load();
        Check(JsonSerializer.Serialize(LibrarianPreferences050.Current) == _expectedPreferences, "real saved run reload retains presentation settings");
        Check(RunManager.Instance.IsInProgress && player.Character is LibrarianCharacter,
            "real saved run reload retains active run and librarian character");
        await Shot("settings-post-reload-run");
        if (!CaptureOnly)
        {
            RestoreFile(LibrarianPreferences050.FilePath, _originalPreferences); LibrarianPreferences050.Load();
            LibrarianLanguage.Select(_originalLanguage); RestoreFile(LibrarianLanguage.FilePath, _originalLanguageFile);
            Check(ReadOptional(LibrarianPreferences050.FilePath) == _originalPreferences, "original isolated preferences restored");
        }
        MainFile.Logger.Info($"SETTINGS_PAGE_AUDIT_PASS checks={_checks} captureOnly={CaptureOnly} liveMulticlient=False");
    }
}
