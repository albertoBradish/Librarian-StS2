using System.IO;
using System.Text.Json;
using Godot;
using Librarian.LibrarianCode;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;

namespace Librarian.Mechanics;

/// <summary>Explicit isolated-profile popup geometry and screenshot verification.</summary>
internal static class DevelopmentRevision102Beta2PopupAudit
{
    private static int _checks;
    private static string Output => System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT")
        ?? @"D:\Slay The Spire_Mod Dev\.research\channels\stable\workspace\outputs\revision-v1.1.0-stable\screenshots";
    private static void Check(bool ok, string text)
    {
        if (!ok) throw new InvalidOperationException("102 beta2 popup: " + text);
        _checks++; MainFile.Logger.Info("V102_BETA2_POPUP_CHECK_PASS " + text);
    }
    private static async Task Wait(double seconds = .2) => await NGame.Instance!.ToSignal(
        NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private static object Vec(Vector2 value) => new { x = value.X, y = value.Y };
    private static object Geometry(Control node) => new
    {
        name = node.Name.ToString(), position = Vec(node.Position), size = Vec(node.Size), pivot = Vec(node.PivotOffset),
        anchors = new[] { node.AnchorLeft,node.AnchorTop,node.AnchorRight,node.AnchorBottom },
        offsets = new[] { node.OffsetLeft,node.OffsetTop,node.OffsetRight,node.OffsetBottom },
        global_transform_with_canvas = node.GetGlobalTransformWithCanvas().ToString()
    };
    private static Vector2 PixelPoint(Control node, Vector2 local) =>
        node.GetViewport().GetStretchTransform() * node.GetGlobalTransformWithCanvas() * local;
    private static Vector2[] PixelCorners(Control node) =>
        [PixelPoint(node,Vector2.Zero),PixelPoint(node,new(node.Size.X,0)),PixelPoint(node,node.Size),PixelPoint(node,new(0,node.Size.Y))];
    private static bool Inside(Control inner, Control outer)
    {
        var corners = PixelCorners(outer);
        float minX = corners.Min(p => p.X), maxX = corners.Max(p => p.X);
        float minY = corners.Min(p => p.Y), maxY = corners.Max(p => p.Y);
        return PixelCorners(inner).All(p => p.X >= minX - 1 && p.X <= maxX + 1 && p.Y >= minY - 1 && p.Y <= maxY + 1);
    }
    private static string? ReadFile(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

    internal static async Task Run(NMainMenu menu)
    {
        Check(System.Environment.GetEnvironmentVariable("LIBRARIAN_RUNTIME_AUDIT") == "1"
            && OS.GetUserDataDir().Contains("revision030-userdata",StringComparison.OrdinalIgnoreCase), "isolated opt-in profile");
        var game = NGame.Instance!;
        var container = NModalContainer.Instance!;
        Check(container.OpenModal is null && !menu.SubmenuStack.SubmenusOpen, "free main-menu modal slot");
        var schedulers = menu.GetChildren().Where(n => n is LibrarianUpdateNotice051 or LibrarianArchitectReview102)
            .Select(n => (node:n,process:n.IsProcessing())).ToArray();
        foreach (var entry in schedulers) entry.node.SetProcess(false);
        var settings = SaveManager.Instance.SettingsSave;
        var originalAspect = settings.AspectRatioSetting;
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
        string? welcomeFile = ReadFile(LibrarianNoticeHistory051.FilePath);
        string? reviewFile = ReadFile(LibrarianArchitectReviewHistory102.FilePath);
        var epochs = SaveManager.Instance.Progress.Epochs.Select(e => (e.Id,e.State,e.ObtainDate)).ToArray();
        var markers = SaveManager.Instance.Progress.FtueCompleted.Order().ToArray();
        var metrics = new List<object>();
        Directory.CreateDirectory(Output);
        try
        {
            // Record the actual loaded native scene before our layout is applied.
            var native = NGenericPopup.Create() ?? throw new InvalidOperationException("Native popup unavailable");
            container.Add(native); await Wait();
            var nativePanel = native.GetNode<NVerticalPopup>("VerticalPopup");
            metrics.Add(new { kind = "native-baseline", root = Geometry(native), panel = Geometry(nativePanel), modal = Geometry(container) });
            Check(native.AnchorLeft == .5f && native.AnchorTop == .5f && nativePanel.AnchorLeft == 0
                && nativePanel.AnchorTop == 0, "loaded native parent centered and child zero-anchored");
            container.Clear(); await Wait();
            var cases = new[]
            {
                (size:new Vector2I(1280,720),aspect:AspectRatioSetting.Auto),
                (size:new Vector2I(1920,1080),aspect:AspectRatioSetting.Auto),
                (size:new Vector2I(2560,1600),aspect:AspectRatioSetting.Auto),
                (size:new Vector2I(2560,1080),aspect:AspectRatioSetting.Auto),
                (size:new Vector2I(2560,1600),aspect:AspectRatioSetting.SixteenByTen),
                (size:new Vector2I(1280,720),aspect:AspectRatioSetting.SixteenByNine),
                (size:new Vector2I(2560,1080),aspect:AspectRatioSetting.TwentyOneByNine)
            };
            int opened = 0;
            foreach (var sample in cases)
            {
                settings.AspectRatioSetting = sample.aspect;
                settings.Fullscreen = false;
                settings.WindowSize = sample.size;
                game.ApplyDisplaySettings();
                // Some desktops clamp ApplyDisplaySettings to the monitor size. The
                // capture must use the requested viewport, not silently pass a smaller one.
                DisplayServer.WindowSetSize(sample.size);
                await Wait(.4);
                Check(DisplayServer.WindowGetSize() == sample.size, "requested window size " + sample.size + " " + sample.aspect);
                foreach (string language in new[] { "zhs", "eng" })
                {
                    LibrarianLanguage.Select(language);
                    foreach (string kind in new[] { "welcome", "review" })
                    {
                        var popup = kind == "welcome"
                            ? LibrarianUpdateNotice051.Show(LibrarianUpdateNotice051.CurrentVersion ?? "1.1.0", _ => opened++)
                            : LibrarianArchitectReview102.Show(preview:true,openLink:_ => opened++);
                        Check(popup is not null,"created " + kind + " " + language);
                        await Wait();
                        var panel = popup!.GetNode<NVerticalPopup>("VerticalPopup");
                        var middle = panel.GetNode<NPopupYesNoButton>(kind == "welcome" ? "NeverShowButton" : "FeedbackButton");
                        await game.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                        using var image = game.GetViewport().GetTexture().GetImage();
                        var imageSize = new Vector2(image.GetWidth(),image.GetHeight());
                        var center = PixelPoint(panel,panel.Size * .5f);
                        var name = $"popup-{kind}-{language}-{sample.size.X}x{sample.size.Y}-{sample.aspect}";
                        Check(center.DistanceTo(imageSize * .5f) <= 1.5f,"panel centered in captured pixels " + name);
                        Check(panel.Size.IsEqualApprox(new Vector2(720,659)) && panel.Position.IsZeroApprox(),"panel fills centered root " + name);
                        Check(PixelCorners(panel).All(p => p.X >= 0 && p.Y >= 0 && p.X <= imageSize.X && p.Y <= imageSize.Y),"panel inside capture " + name);
                        Check(panel.NoButton.Position.X < middle.Position.X && middle.Position.X < panel.YesButton.Position.X,
                            "three buttons retain order " + name);
                        foreach (var button in new[] { panel.NoButton,middle,panel.YesButton })
                            Check(button.Visible && Inside(button.GetNode<Control>("%Visuals"),panel),"button visual inside panel " + name + " " + button.Name);
                        Check(Inside(panel.GetNode<Control>("Header"),panel) && Inside(panel.GetNode<Control>("Description"),panel),"text bounds inside panel " + name);
                        Check(image.SavePng(Path.Combine(Output,name + ".png")) == Error.Ok,"capture " + name);
                        metrics.Add(new
                        {
                            name,requested_window = sample.size.ToString(),actual_window = DisplayServer.WindowGetSize().ToString(),
                            aspect = sample.aspect.ToString(),content_scale_size = window.ContentScaleSize.ToString(),
                            content_scale_aspect = window.ContentScaleAspect.ToString(),capture_size = Vec(imageSize),
                            viewport_rect = game.GetViewport().GetVisibleRect().ToString(),stretch = game.GetViewport().GetStretchTransform().ToString(),
                            final_transform = game.GetViewport().GetFinalTransform().ToString(),panel_center_pixels = Vec(center),
                            root = Geometry(popup),panel = Geometry(panel),modal = Geometry(container)
                        });
                        container.Clear(); await Wait(.12);
                    }
                }
            }
            settings.AspectRatioSetting = AspectRatioSetting.Auto;
            settings.WindowSize = new Vector2I(1280,720);
            game.ApplyDisplaySettings();
            var resizing = LibrarianUpdateNotice051.Show(LibrarianUpdateNotice051.CurrentVersion ?? "1.1.0", _ => opened++)
                ?? throw new InvalidOperationException("Resize popup unavailable");
            foreach (var size in new[] { new Vector2I(2560,1600),new Vector2I(1280,720) })
            {
                DisplayServer.WindowSetSize(size); await Wait(.4);
                var panel = resizing.GetNode<NVerticalPopup>("VerticalPopup");
                await game.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                using var image = game.GetViewport().GetTexture().GetImage();
                var imageSize = new Vector2(image.GetWidth(),image.GetHeight());
                var center = PixelPoint(panel,panel.Size * .5f);
                Check(center.DistanceTo(imageSize * .5f) <= 1.5f,"already-open popup remains centered after resize " + size);
                metrics.Add(new { kind = "live-resize",window = size.ToString(),capture_size = Vec(imageSize),
                    panel_center_pixels = Vec(center),root = Geometry(resizing),panel = Geometry(panel) });
            }
            container.Clear(); await Wait();
            Check(opened == 0,"showing and layout changes never dispatch external links");
            Check(welcomeFile == ReadFile(LibrarianNoticeHistory051.FilePath) && reviewFile == ReadFile(LibrarianArchitectReviewHistory102.FilePath),
                "welcome close and review preview preserve history files");
            Check(epochs.SequenceEqual(SaveManager.Instance.Progress.Epochs.Select(e => (e.Id,e.State,e.ObtainDate)))
                && markers.SequenceEqual(SaveManager.Instance.Progress.FtueCompleted.Order()),"popup layout preserves native progression");
            File.WriteAllText(Path.Combine(Output,"popup-layout-metrics.json"),JsonSerializer.Serialize(metrics,new JsonSerializerOptions { WriteIndented=true }));
            MainFile.Logger.Info($"V102_BETA2_POPUP_AUDIT_PASS checks={_checks} cases=7 languages=2 popup_types=2 captures=28");
        }
        finally
        {
            container.Clear();
            LibrarianLanguage.Select(originalLanguage);
            settings.AspectRatioSetting = originalAspect;
            settings.Fullscreen = originalFullscreen;
            settings.WindowSize = originalSavedSize;
            settings.WindowPosition = originalSavedPosition;
            window.ContentScaleSize = originalScaleSize;
            window.ContentScaleAspect = originalScaleAspect;
            window.Size = originalSize;
            window.Position = originalPosition;
            window.Mode = originalMode;
            foreach (var entry in schedulers)
                if (GodotObject.IsInstanceValid(entry.node)) entry.node.SetProcess(entry.process);
        }
    }
}
