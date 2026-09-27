using System.IO;
using System.Reflection;
using Godot;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Relics;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.InspectScreens;
using MegaCrit.Sts2.Core.Saves;

namespace Librarian.Mechanics;

/// <summary>Uses the game's real inspection screen, including its native frame and transitions.</summary>
internal static class DevelopmentRevision038RelicVisualAudit
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Require(bool pass, string text)
    {
        if (!pass) throw new InvalidOperationException("038 relic visual: " + text);
        MainFile.Logger.Info("VISUAL_038_RELIC_CHECK_PASS " + text);
    }
    private static async Task Wait()
        => await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(0.55), SceneTreeTimer.SignalName.Timeout);

    private static Rect2I AlphaBounds(Texture2D texture)
    {
        using var image = texture.GetImage();
        Require(image != null && !image.IsEmpty(), "read texture " + texture.ResourcePath);
        if (image!.IsCompressed()) Require(image.Decompress() == Error.Ok, "decompress " + texture.ResourcePath);
        int left = image.GetWidth(), top = image.GetHeight(), right = -1, bottom = -1;
        for (int y = 0; y < image.GetHeight(); y++)
            for (int x = 0; x < image.GetWidth(); x++)
                if (image.GetPixel(x, y).A > 20f / 255f)
                { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
        Require(right >= left && bottom >= top, "visible subject " + texture.ResourcePath);
        return new Rect2I(left, top, right - left + 1, bottom - top + 1);
    }

    internal static async Task Run(Player player)
    {
        if (!DevelopmentVisualAudit.Enabled) return;
        Require(OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase), "isolated fixture profile");
        Require(DisplayServer.GetName() != "headless", "rendering enabled");
        string output = System.Environment.GetEnvironmentVariable("LIBRARIAN_040_ONLY") == "1"
            ? @"D:\Slay The Spire_Mod Dev\.research\revision-v040-screenshots"
            : @"D:\Slay The Spire_Mod Dev\.research\revision-v038-screenshots";
        Directory.CreateDirectory(output);
        var own = player.Character.RelicPool.AllRelics.Append(ModelDb.Relic<TatteredSpellScroll>())
            .Append(ModelDb.Relic<RestoredSpellScroll>()).DistinctBy(r => r.Id).ToArray();
        Require(own.Length == 9, "all nine Librarian relics");
        RelicModel[] catalog = [ModelDb.Relic<DataDisk>(), .. own];
        // Only the isolated development profile is touched. Discovery permits normal native text and colors.
        foreach (var relic in catalog) SaveManager.Instance.MarkRelicAsSeen(relic);
        var game = NGame.Instance!;
        var screen = game.GetInspectRelicScreen();
        var window = game.GetWindow();
        var originalSize = window.Size;
        try
        {
            foreach (var resolution in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                window.Size = resolution; await Wait();
                Rect2? referenceFrame = null, referenceImage = null;
                foreach (var relic in catalog)
                {
                    screen.Open(catalog, relic);
                    // This disposable fixture exposes mod starter/upgrade entries even if progression has not unlocked them.
                    var unlocked = (HashSet<RelicModel>)typeof(NInspectRelicScreen).GetField("_allUnlockedRelics", Private)!.GetValue(screen)!;
                    unlocked.Add(relic.CanonicalInstance);
                    typeof(NInspectRelicScreen).GetMethod("UpdateRelicDisplay", Private)!.Invoke(screen, null);
                    await Wait();
                    var frame = screen.GetNode<Control>("Popup/RelicFrame");
                    var image = screen.GetNode<TextureRect>("%RelicImage");
                    Require(frame.Size.IsEqualApprox(new Vector2(304, 304)), "native frame 304 " + relic.Id);
                    Require(image.Size.IsEqualApprox(new Vector2(192, 192)), "native image rect 192 " + relic.Id);
                    Require(image.StretchMode == TextureRect.StretchModeEnum.KeepAspectCentered, "native aspect-centered mode " + relic.Id);
                    Require((image.Position + image.Size / 2).DistanceTo(frame.Size / 2) < 0.1f, "image centered inside frame " + relic.Id);
                    Require(image.Texture.ResourcePath == relic.BigIcon.ResourcePath, "native display uses model BigIcon " + relic.Id);
                    var frameRect = frame.GetGlobalRect(); var imageRect = image.GetGlobalRect();
                    if (referenceFrame == null) { referenceFrame = frameRect; referenceImage = imageRect; }
                    else
                    {
                        Require(frameRect.Position.DistanceTo(referenceFrame.Value.Position) < 0.2f && frameRect.Size.DistanceTo(referenceFrame.Value.Size) < 0.2f,
                            "same frame position and size as DataDisk " + relic.Id);
                        Require(imageRect.Position.DistanceTo(referenceImage!.Value.Position) < 0.2f && imageRect.Size.DistanceTo(referenceImage.Value.Size) < 0.2f,
                            "same image rectangle as DataDisk " + relic.Id);
                    }
                    var bounds = AlphaBounds(image.Texture);
                    var center = (Vector2)bounds.Position + (Vector2)bounds.Size / 2;
                    var delta = center - image.Texture.GetSize() / 2;
                    if (relic is LibrarianRelic)
                        Require(Math.Abs(delta.X) <= 3 && Math.Abs(delta.Y) <= 3, "painted subject centered within 3 source pixels " + relic.Id + " delta=" + delta);
                    MainFile.Logger.Info($"VISUAL_038_RELIC_GEOMETRY relic={relic.Id} resolution={resolution} texture={image.Texture.ResourcePath} textureSize={image.Texture.GetSize()} alphaBounds={bounds} alphaCenterDelta={delta} frame={frameRect} image={imageRect}");
                    await game.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var capture = game.GetViewport().GetTexture().GetImage();
                    var path = Path.Combine(output, $"038-native-relic-{relic.Id.Entry}-{resolution.X}x{resolution.Y}.png");
                    Require(capture.SavePng(path) == Error.Ok, "saved " + path);
                    screen.Close(); await Wait();
                }
            }
        }
        finally { screen.Close(); window.Size = originalSize; }
        MainFile.Logger.Info("REVISION_038_RELIC_VISUAL_AUDIT_PASS nativeReference=DataDisk customRelics=9 resolutions=2 screenshots=20");
    }
}
