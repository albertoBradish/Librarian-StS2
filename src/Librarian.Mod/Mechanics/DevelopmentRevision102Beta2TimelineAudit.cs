using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;
using HarmonyLib;
using Librarian.LibrarianCode;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Timeline;
using MegaCrit.Sts2.addons.mega_text;

namespace Librarian.Mechanics;

/// <summary>Temporary isolated unlock fixture; native timeline, fonts and rich-text layout.</summary>
internal static class DevelopmentRevision102Beta2TimelineAudit
{
    private static int _checks;
    // Stable 0.107.1 keeps this native art-state property private. Invoke the
    // original getter instead of replacing its state test with our path check.
    private static readonly System.Reflection.MethodInfo NativeHasRealPortraitGetter =
        AccessTools.PropertyGetter(typeof(EpochModel), "HasRealPortrait")
        ?? throw new MissingMethodException(typeof(EpochModel).FullName, "get_HasRealPortrait");
    private static bool NativeHasRealPortrait(EpochModel epoch)
        => (bool)NativeHasRealPortraitGetter.Invoke(epoch, null)!;
    private static readonly Vector2I[] CaptureWindows = [new(1280,720),new(1920,1080),new(2560,1600)];
    private static string Output => System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT")
        ?? @"D:\Slay The Spire_Mod Dev\.research\channels\stable\workspace\outputs\revision-v1.1.0-stable\screenshots";
    private static readonly Regex ColorTags = new(@"\[(gold|blue|aqua|green|orange|pink|purple|red)(?:\s[^\]]*)?\]",RegexOptions.IgnoreCase);
    private static readonly Regex MotionTags = new(@"\[(sine|jitter)(?:\s[^\]]*)?\]",RegexOptions.IgnoreCase);
    private static readonly Regex MotionSpans = new(@"\[(sine|jitter)(?:\s[^\]]*)?\](.*?)\[/\1\]",RegexOptions.IgnoreCase|RegexOptions.Singleline);
    private static readonly Regex AnyTag = new(@"\[[^\]\r\n]+\]");
    private static bool TagsPaired(string text)
    {
        var stack = new Stack<string>();
        foreach (Match tag in AnyTag.Matches(text))
        {
            string token = tag.Value[1..^1];
            bool closing = token.StartsWith('/');
            string name = (closing ? token[1..] : token).Split([' ','='],2,StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
            if (closing)
            {
                if (stack.Count==0 || stack.Pop()!=name) return false;
            }
            else stack.Push(name);
        }
        return stack.Count==0;
    }
    private static void Check(bool ok,string text)
    {
        if (!ok) throw new InvalidOperationException("102 beta2 timeline: " + text);
        _checks++; MainFile.Logger.Info("V102_BETA2_TIMELINE_CHECK_PASS " + text);
    }
    private static async Task Wait(double seconds = .2) => await NGame.Instance!.ToSignal(
        NGame.Instance.GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
    private static IEnumerable<Node> Desc(Node root)
    {
        foreach (var child in root.GetChildren()) { yield return child; foreach (var nested in Desc(child)) yield return nested; }
    }
    private static object Vec(Vector2 value) => new { x=value.X,y=value.Y };
    private static Transform2D ToPixels(Control node) => node.GetViewport().GetStretchTransform() * node.GetGlobalTransformWithCanvas();
    private static object Bounds(Control node) => new
    {
        position=Vec(node.Position),size=Vec(node.Size),
        pixel_top_left=Vec(ToPixels(node)*Vector2.Zero),pixel_bottom_right=Vec(ToPixels(node)*node.Size)
    };
    private static bool FitsCapture(Control node,Vector2 captureSize)
    {
        var transform = ToPixels(node);
        return new[] { Vector2.Zero,new Vector2(node.Size.X,0),node.Size,new Vector2(0,node.Size.Y) }
            .Select(p => transform*p).All(p => p.X >= -1 && p.Y >= -1 && p.X <= captureSize.X+1 && p.Y <= captureSize.Y+1);
    }

    internal static async Task Run(NMainMenu menu)
    {
        Check(System.Environment.GetEnvironmentVariable("LIBRARIAN_RUNTIME_AUDIT") == "1"
            && OS.GetUserDataDir().Contains("revision030-userdata",StringComparison.OrdinalIgnoreCase),"isolated opt-in profile");
        Check(!menu.SubmenuStack.SubmenusOpen && NModalContainer.Instance?.OpenModal is null,"free native main menu");
        var game = NGame.Instance!;
        var progress = SaveManager.Instance.Progress;
        // Native override is memory-only; retain complete state/date/order for restoration.
        var epochList = (List<SerializableEpoch>)AccessTools.Field(typeof(ProgressState),"_epochs").GetValue(progress)!;
        var originalEpochs = epochList.Select(e => new SerializableEpoch(e.Id,e.State) { ObtainDate=e.ObtainDate }).ToArray();
        var markers = (HashSet<string>)AccessTools.Field(typeof(ProgressState),"_ftueCompleted").GetValue(progress)!;
        var originalMarkers = markers.ToArray();
        var schedulers = menu.GetChildren().Where(n => n is LibrarianUpdateNotice051 or LibrarianArchitectReview102)
            .Select(n => (node:n,process:n.IsProcessing())).ToArray();
        foreach (var entry in schedulers) entry.node.SetProcess(false);
        var settings = SaveManager.Instance.SettingsSave;
        var originalSavedSize = settings.WindowSize;
        var originalSavedPosition = settings.WindowPosition;
        bool originalFullscreen = settings.Fullscreen;
        var window = game.GetWindow();
        var originalSize = window.Size;
        var originalPosition = window.Position;
        var originalMode = window.Mode;
        var originalScaleSize = window.ContentScaleSize;
        var originalScaleAspect = window.ContentScaleAspect;
        string originalLanguage = LibrarianLanguage.Selected;
        var metrics = new List<object>();
        var portraits = new HashSet<string>(StringComparer.Ordinal);
        var textureIds = new HashSet<ulong>();
        var storyColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var storyMotions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        NTimelineScreen? timeline = null;
        Directory.CreateDirectory(Output);
        try
        {
            MainFile.Logger.Info("V102_BETA2_TIMELINE_FIXTURE temporary_isolated_reveal=True natural_progression=False");
            progress.ObtainEpochOverride("NEOW_EPOCH",EpochState.Revealed);
            foreach (int chapter in Enumerable.Range(1,7)) progress.ObtainEpochOverride(LibrarianUnlocks040.Id(chapter),EpochState.Revealed);
            Check(Enumerable.Range(1,7).All(n => progress.Epochs.Any(e => e.Id == LibrarianUnlocks040.Id(n) && e.State == EpochState.Revealed)),
                "seven chapters temporarily available for visual inspection");
            foreach (var size in CaptureWindows)
            {
                settings.Fullscreen = false;
                settings.WindowSize = size;
                game.ApplyDisplaySettings();
                DisplayServer.WindowSetSize(size); await Wait(.4);
                Check(DisplayServer.WindowGetSize() == size,"actual requested window " + size);
                foreach (string language in new[] { "zhs","eng" })
                {
                    LibrarianLanguage.Select(language);
                    timeline = menu.SubmenuStack.PushSubmenuType<NTimelineScreen>(); await Wait(1);
                    var inspect = timeline.GetNode<NEpochInspectScreen>("%EpochInspectScreen");
                    foreach (int chapter in Enumerable.Range(1,7))
                    {
                        string id = LibrarianUnlocks040.Id(chapter);
                        string expected = $"res://Librarian/images/timeline/v1.0.2-beta2/epoch-{chapter:00}.png";
                        var epoch = EpochModel.Get(id);
                        var slot = Desc(timeline).OfType<NEpochSlot>().Single(s => s.model.Id == id);
                        Check(epoch.ResolvedPortraitPath == expected && ResourceLoader.Exists(expected) && NativeHasRealPortrait(epoch),
                            "versioned real portrait " + chapter + " " + language);
                        timeline.OpenInspectScreen(slot,playAnimation:false); await Wait(.9);
                        Check(inspect.IsVisibleInTree(),"native chapter inspect visible " + chapter);
                        var portrait = inspect.GetNode<TextureRect>("%Portrait");
                        var placeholder = inspect.GetNode<Control>("%PlaceholderLabel");
                        var fancy = inspect.GetNode<MegaRichTextLabel>("%FancyText");
                        var expectedTexture = ResourceLoader.Load<Texture2D>(expected);
                        Check(portrait.Texture is not null && portrait.Texture.GetWidth()>0 && portrait.Texture.GetHeight()>0
                            && portrait.Texture.GetInstanceId() == expectedTexture.GetInstanceId(),"native portrait texture resolves " + chapter);
                        Check(!placeholder.Visible,"native placeholder caption hidden " + chapter);
                        portraits.Add(expected); textureIds.Add(portrait.Texture!.GetInstanceId());
                        string text = fancy.Text,parsed = fancy.GetParsedText();
                        string label = language + " " + chapter + " " + size;
                        Check(text == epoch.Description && parsed.Length>80,"native chapter body is current story " + label);
                        Check(!parsed.Contains("未来揭晓",StringComparison.Ordinal) && !parsed.Contains("yet to be revealed",StringComparison.OrdinalIgnoreCase)
                            && !parsed.Contains("占位",StringComparison.Ordinal) && !parsed.Contains("placeholder",StringComparison.OrdinalIgnoreCase),
                            "story contains no placeholder notice " + label);
                        Check(!AnyTag.IsMatch(parsed),"native rich-text tags parsed without residue " + label);
                        Check(TagsPaired(text),"native rich-text tags correctly paired " + label);
                        var colors = ColorTags.Matches(text).Select(m => m.Groups[1].Value.ToLowerInvariant()).Distinct().ToArray();
                        var motions = MotionTags.Matches(text).Select(m => m.Groups[1].Value.ToLowerInvariant()).ToArray();
                        int movingCharacters = MotionSpans.Matches(text).Sum(m => AnyTag.Replace(m.Groups[2].Value,"").Length);
                        Check(colors.Length>=1,"native accent color present " + label);
                        Check(motions.Length<=2 && movingCharacters<=parsed.Length*.25,"limited native motion emphasis " + label);
                        foreach (string color in colors) storyColors.Add(color);
                        foreach (string motion in motions) storyMotions.Add(motion);
                        var effects = fancy.CustomEffects.Select(e => e.AsGodotObject().GetType().Name).ToArray();
                        Check(fancy.BbcodeEnabled && colors.All(color => effects.Contains("RichText" + char.ToUpperInvariant(color[0]) + color[1..]))
                            && motions.All(motion => effects.Contains("RichText" + char.ToUpperInvariant(motion[0]) + motion[1..])),
                            "native accent and motion effects installed " + label);
                        int fontSize = fancy.GetThemeFontSize("normal_font_size");
                        int contentHeight = fancy.GetContentHeight();
                        Check(fontSize>=fancy.MinFontSize && fontSize<=fancy.MaxFontSize,"native default font range " + label);
                        Check(!fancy.ScrollActive && contentHeight<=fancy.Size.Y+1 && fancy.VisibleRatio>=.999f,
                            "complete story fits native text height " + label);
                        await game.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                        using var image = game.GetViewport().GetTexture().GetImage();
                        var captureSize = new Vector2(image.GetWidth(),image.GetHeight());
                        Check(FitsCapture(fancy,captureSize) && FitsCapture(portrait,captureSize),"native text and art stay within capture " + label);
                        string name = $"story-{language}-{chapter:00}-{size.X}x{size.Y}";
                        Check(image.SavePng(Path.Combine(Output,name + ".png")) == Error.Ok,"capture " + name);
                        metrics.Add(new
                        {
                            name,chapter,language,temporary_isolated_reveal=true,natural_progression=false,
                            window=DisplayServer.WindowGetSize().ToString(),aspect=settings.AspectRatioSetting.ToString(),
                            capture_size=Vec(captureSize),content_scale_size=window.ContentScaleSize.ToString(),
                            content_scale_aspect=window.ContentScaleAspect.ToString(),stretch=game.GetViewport().GetStretchTransform().ToString(),
                            portrait_path=epoch.ResolvedPortraitPath,texture_resource_path=portrait.Texture.ResourcePath,
                            texture_size=Vec(portrait.Texture.GetSize()),has_real_portrait=NativeHasRealPortrait(epoch),placeholder_visible=placeholder.Visible,
                            text,parsed_text=parsed,content_height=contentHeight,control_height=fancy.Size.Y,
                            lines=fancy.GetLineCount(),visible_lines=fancy.GetVisibleLineCount(),font_size=fontSize,
                            native_min_font=fancy.MinFontSize,native_max_font=fancy.MaxFontSize,
                            effective_font_pixels=fontSize*ToPixels(fancy).Scale.Y,font_resource=fancy.GetThemeFont("normal_font").ResourcePath,
                            colors,motions,moving_characters=movingCharacters,installed_effects=effects,
                            text_bounds=Bounds(fancy),portrait_bounds=Bounds(portrait)
                        });
                        inspect.Close(); await Wait(.3);
                    }
                    menu.SubmenuStack.Pop(); timeline=null; await Wait(.3);
                }
            }
            // Godot can release and recreate textures between closing timeline views.
            // Paths are stable; each opened chapter already matched its loaded texture.
            Check(portraits.Count==7 && textureIds.Count>=7,"seven distinct real portrait resources");
            Check(storyColors.Count>=3 && storyMotions.Contains("sine") && storyMotions.Contains("jitter"),
                "native narrative spans multiple colors and both subtle motion types");
            File.WriteAllText(Path.Combine(Output,"story-layout-metrics.json"),JsonSerializer.Serialize(metrics,new JsonSerializerOptions { WriteIndented=true }));
            MainFile.Logger.Info($"V102_BETA2_TIMELINE_AUDIT_PASS checks={_checks} languages=2 chapters=7 resolutions={CaptureWindows.Length} captures={metrics.Count} native_fonts=True temporary_unlock=True");
        }
        finally
        {
            if (timeline is not null && GodotObject.IsInstanceValid(timeline))
            {
                timeline.GetNode<NEpochInspectScreen>("%EpochInspectScreen").Close();
                if (ReferenceEquals(menu.SubmenuStack.Peek(),timeline)) menu.SubmenuStack.Pop();
            }
            epochList.Clear(); epochList.AddRange(originalEpochs);
            markers.Clear(); foreach (string marker in originalMarkers) markers.Add(marker);
            LibrarianLanguage.Select(originalLanguage);
            settings.Fullscreen=originalFullscreen;
            settings.WindowSize=originalSavedSize;
            settings.WindowPosition=originalSavedPosition;
            window.ContentScaleSize=originalScaleSize;
            window.ContentScaleAspect=originalScaleAspect;
            window.Size=originalSize;
            window.Position=originalPosition;
            window.Mode=originalMode;
            foreach (var entry in schedulers)
                if (GodotObject.IsInstanceValid(entry.node)) entry.node.SetProcess(entry.process);
            Check(originalEpochs.Select(e => (e.Id,e.State,e.ObtainDate)).SequenceEqual(progress.Epochs.Select(e => (e.Id,e.State,e.ObtainDate)))
                && originalMarkers.Order().SequenceEqual(progress.FtueCompleted.Order()),"temporary fixture restores epoch state dates order and markers");
        }
    }
}
