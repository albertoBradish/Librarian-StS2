using System.IO;
using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;

namespace Librarian.Mechanics;

/// <summary>Disposable combat fixture: captures the new circle through the actual orb display.</summary>
internal static class DevelopmentRevision038OrbVisualAudit
{
    private static void Require(bool pass, string text)
    {
        if (!pass) throw new InvalidOperationException("038 orb visual: " + text);
        MainFile.Logger.Info("VISUAL_038_ORB_CHECK_PASS " + text);
    }
    private static async Task Wait(double seconds = 0.55)
        => await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    internal static async Task Run(Player player)
    {
        if (!DevelopmentVisualAudit.Enabled) return;
        Require(OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase), "isolated fixture profile");
        Require(DisplayServer.GetName() != "headless", "rendering enabled");
        const string output = @"D:\Slay The Spire_Mod Dev\.research\revision-v038-screenshots";
        Directory.CreateDirectory(output);
        var session = LibrarianRuntime.Get(player);
        LibrarianOrbPanel.Refresh(session);
        await Wait();
        var display = LibrarianOrbPanel.GetDisplay(session)!;
        Require(display != null && display.IsVisibleInTree(), "real combat orb display visible");
        var circle = display!.GetNode<LibrarianOrbMagicCircle>("MagicCircle");
        Require(circle.GetIndex() == 0 && circle.ZIndex == 0, "circle shares combat plane and draws before all orb labels and locks");
        Require(circle.Position.IsEqualApprox(Vector2.Zero) && circle.Rotation == 0, "ring fixed to foreground slot");
        foreach (var kind in Enum.GetValues<OrbKind>()) display.Pulse(kind);
        await Wait();
        Require(circle.Position.IsEqualApprox(Vector2.Zero) && circle.Scale.IsEqualApprox(Vector2.One), "orb pulses do not move or scale magic circle");
        var expectedPositions = session.Orbs.Positions.ToArray();
        var before = session.Orbs.Snapshot();
        display.RefreshState();
        Require(before == session.Orbs.Snapshot() || before.Orbs.SequenceEqual(session.Orbs.Snapshot().Orbs), "visual refresh does not mutate orb state");
        var window = NGame.Instance!.GetWindow();
        var originalSize = window.Size;
        try
        {
            foreach (var resolution in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                window.Size = resolution; await Wait();
                var centers = Enum.GetValues<OrbKind>().Select(kind => display.GetNode<Control>(kind.ToString())).ToArray();
                foreach (var slot in centers)
                {
                    var center = slot.Position + new Vector2(36, 36);
                    Require(center.DistanceTo(LibrarianOrbMagicCircle.RingCenter) + 36 * slot.Scale.X < LibrarianOrbMagicCircle.OuterRadius,
                        "ring contains " + slot.Name);
                }
                var foreground = display.GetNode<Control>(session.Orbs.Foreground.ToString());
                Require(Math.Abs(foreground.Position.X + 36) < 0.1 && Math.Abs(foreground.Position.Y) < 0.1,
                    "needle points toward centered lower foreground");
                Require(LibrarianOrbMagicCircle.NeedleTip.Y < foreground.Position.Y, "needle ends before foreground numeral region");
                // Geometry alone cannot prove visible rendering: compare actual viewport pixels
                // at the unobstructed needle, with and without the decorative node.
                circle.Visible = false;
                await NGame.Instance.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var hidden = NGame.Instance.GetViewport().GetTexture().GetImage();
                circle.Visible = true;
                await NGame.Instance.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
                var path = Path.Combine(output, $"038-magic-circle-{resolution.X}x{resolution.Y}.png");
                Require(hidden.SavePng(Path.Combine(output, $"038-magic-circle-hidden-{resolution.X}x{resolution.Y}.png")) == Error.Ok, "saved hidden diagnostic capture");
                Require(image.SavePng(path) == Error.Ok, "saved " + path);
                var viewport = circle.GetViewport();
                // GlobalTransformWithCanvas stops at logical viewport coordinates. Root
                // canvas-items stretch maps those coordinates onto the captured texture.
                var toPixels = viewport.GetStretchTransform() * circle.GetGlobalTransformWithCanvas();
                MainFile.Logger.Info($"VISUAL_038_PIXEL_TRANSFORM window={window.Size} image={image.GetWidth()}x{image.GetHeight()} viewport={viewport.GetVisibleRect()} stretch={viewport.GetStretchTransform()} final={viewport.GetFinalTransform()} localToViewport={circle.GetGlobalTransformWithCanvas()}");
                int changed = 0;
                foreach (float y in new[] { -53f, -47f, -41f, -35f, -29f })
                {
                    var pixel = toPixels * new Vector2(0, y);
                    int x = (int)Mathf.Round(pixel.X), py = (int)Mathf.Round(pixel.Y);
                    Require(x >= 0 && py >= 0 && x < image.GetWidth() && py < image.GetHeight(), "needle pixel lies inside viewport");
                    var on = image.GetPixel(x, py);
                    var off = hidden.GetPixel(x, py);
                    float difference = Math.Abs(on.R - off.R) + Math.Abs(on.G - off.G) + Math.Abs(on.B - off.B);
                    MainFile.Logger.Info($"VISUAL_038_PIXEL_SAMPLE local=(0,{y}) pixel=({x},{py}) on={on} off={off} rgbDifference={difference}");
                    if (difference > 0.08f) changed++;
                }
                Require(changed >= 3, "visible needle changes at least three actual viewport samples");
            }
        }
        finally { circle.Visible = true; window.Size = originalSize; }
        Require(expectedPositions.SequenceEqual(session.Orbs.Positions), "capture preserves order and foreground");
        MainFile.Logger.Info("REVISION_038_ORB_VISUAL_AUDIT_PASS radius=138 needle=foreground preservedSlots=true resolutions=2");
    }
}
