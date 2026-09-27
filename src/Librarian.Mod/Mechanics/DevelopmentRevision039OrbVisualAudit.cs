using System.IO;
using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace Librarian.Mechanics;

internal static class DevelopmentRevision039OrbVisualAudit
{
    private static IEnumerable<Node> Descendants(Node root)
    {
        foreach (var child in root.GetChildren())
        { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static void Require(bool pass, string text)
    {
        if (!pass) throw new InvalidOperationException("039 orb visual: " + text);
        MainFile.Logger.Info("VISUAL_039_ORB_CHECK_PASS " + text);
    }
    private static async Task Wait(double seconds = 0.55)
        => await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    internal static async Task Run(Player player)
    {
        if (!DevelopmentVisualAudit.Enabled) return;
        Require(OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase), "isolated fixture profile");
        Require(DisplayServer.GetName() != "headless", "rendering enabled");
        const string output = @"D:\Slay The Spire_Mod Dev\.research\revision-v039-screenshots";
        Directory.CreateDirectory(output);
        var session = LibrarianRuntime.Get(player);
        LibrarianOrbPanel.Refresh(session);
        await Wait();
        var hud = Descendants(NRun.Instance!.GlobalUi.TopBar).OfType<TextureRect>()
            .Single(t => t.Texture?.ResourcePath == "res://Librarian/images/charui/v0.3.9/character_icon.png");
        Require(hud.IsVisibleInTree() && hud.Texture.GetSize() == new Vector2(88, 88), "independent new HUD portrait in native88 footprint");
        using (var icon = hud.Texture.GetImage())
        {
            if (icon.IsCompressed()) Require(icon.Decompress() == Error.Ok, "HUD image decompresses");
            int minX=88, minY=88, maxX=-1, maxY=-1;
            for(int y=0;y<88;y++) for(int x=0;x<88;x++) if(icon.GetPixel(x,y).A>0.1f)
            { minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x);maxY=Math.Max(maxY,y); }
            Require(maxX-minX>=60 && maxY-minY>=75, "HUD silhouette fills native frame without tiny full-body composition");
            Require(Math.Abs((minX+maxX)/2f-43.5f)<2 && Math.Abs((minY+maxY)/2f-43.5f)<2, "HUD painted bounds centered");
        }
        var display = LibrarianOrbPanel.GetDisplay(session)!;
        var creature = NCombatRoom.Instance!.GetCreatureNode(player.Creature)!;
        var circle = display.MagicCircle;
        var needle = display.ForegroundNeedle;
        Require(circle.GetParent() == creature && circle.GetIndex() < creature.Visuals.GetIndex() && circle.ZIndex == 0,
            "rear circle precedes native body on combat plane");
        Require(display.GetIndex() > creature.Visuals.GetIndex(), "orbs and labels draw after body");
        var before = session.Orbs.Snapshot();
        var positions = session.Orbs.Positions.ToArray();
        var slot = display.GetNode<Control>(session.Orbs.Foreground.ToString());
        var sprite = slot.GetChildren().OfType<TextureRect>().First();
        var label = slot.GetChildren().OfType<Label>().First();
        var rootPosition = slot.Position;
        var captionPosition = label.Position;
        var spritePosition = sprite.Position;
        await Wait(0.7);
        Require(sprite.Position.DistanceTo(spritePosition) > 0.05f, "orb art floats over time");
        Require(slot.Position.IsEqualApprox(rootPosition) && label.Position.IsEqualApprox(captionPosition), "idle animation preserves numeral and lock anchors");
        Require(sprite.Position.DistanceTo(LibrarianOrbDisplay.ArtworkOffset(session.Orbs.Foreground)) < 3, "idle movement remains subtle");
        var window = NGame.Instance!.GetWindow();
        var originalSize = window.Size;
        try
        {
            foreach (var resolution in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                window.Size = resolution;
                await Wait();
                var foreground = display.GetNode<Control>(session.Orbs.Foreground.ToString());
                Require((foreground.Position + new Vector2(36, 36)).IsEqualApprox(new Vector2(0, -display.FormationRadius)), "foreground centered above head");
                foreach (var kind in Enum.GetValues<OrbKind>().Where(kind => kind != session.Orbs.Foreground))
                {
                    var center = display.GetNode<Control>(kind.ToString()).Position + new Vector2(36, 36);
                    Require(Math.Abs(center.Y) < 0.1f && Math.Abs(Math.Abs(center.X) - display.FormationRadius) < 0.1f, "background orb alongside upper body " + kind);
                }
                var expectedCenter = creature.Hitbox.GetGlobalTransform() * new Vector2(creature.Hitbox.Size.X / 2, creature.Hitbox.Size.Y * 0.30f);
                Require(circle.GlobalPosition.DistanceTo(expectedCenter) < 0.1f, "circle follows actual hitbox upper body");
                Require(circle.UpperNeedleTip.Y < 0 && circle.UpperNeedleTip.Y > -display.FormationRadius + 30, "upward needle stays outside foreground numeral");
                Require(needle.UpperNeedleTip.Y < needle.VisibleNeedleBase.Y && needle.VisibleNeedleBase.Y < -display.FormationRadius + 58,
                    "short visible needle is above face and points upward");
                // Keep idle animation fixed during the on/off render comparison.
                display.SetProcess(false);
                circle.Modulate = new Color(1, 1, 1, 0);
                needle.Modulate = new Color(1, 1, 1, 0);
                await NGame.Instance.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var hidden = NGame.Instance.GetViewport().GetTexture().GetImage();
                circle.Modulate = Colors.White;
                needle.Modulate = Colors.White;
                await NGame.Instance.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var shown = NGame.Instance.GetViewport().GetTexture().GetImage();
                string suffix = $"{resolution.X}x{resolution.Y}.png";
                Require(hidden.SavePng(Path.Combine(output, "039-rear-circle-hidden-" + suffix)) == Error.Ok, "saved hidden diagnostic");
                Require(shown.SavePng(Path.Combine(output, "039-rear-circle-" + suffix)) == Error.Ok, "saved visible combat capture");
                var toPixels = circle.GetViewport().GetStretchTransform() * circle.GetGlobalTransformWithCanvas();
                int changed = 0;
                foreach (float angle in new[] { -0.75f, -0.65f, -0.35f, -0.25f, 0.25f })
                {
                    var point = toPixels * (new Vector2(Mathf.Cos(angle * Mathf.Pi), Mathf.Sin(angle * Mathf.Pi)) * circle.Radius);
                    int x = (int)Mathf.Round(point.X), y = (int)Mathf.Round(point.Y);
                    Require(x > 0 && y > 0 && x < shown.GetWidth() - 1 && y < shown.GetHeight() - 1, "ring sample inside screenshot");
                    float difference = 0;
                    for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        var on = shown.GetPixel(x + dx, y + dy);
                        var off = hidden.GetPixel(x + dx, y + dy);
                        difference = Math.Max(difference, Math.Abs(on.R - off.R) + Math.Abs(on.G - off.G) + Math.Abs(on.B - off.B));
                    }
                    MainFile.Logger.Info($"VISUAL_039_RING_PIXEL angle={angle} xy=({x},{y}) maxRgbDifference={difference}");
                    if (difference > 0.08f) changed++;
                }
                Require(changed >= 3, "rear ring visibly changes at least three screenshot samples");
                int needleChanged = 0;
                var needleToPixels = needle.GetViewport().GetStretchTransform() * needle.GetGlobalTransformWithCanvas();
                foreach (float progress in new[] { 0.15f, 0.30f, 0.45f, 0.60f, 0.75f })
                {
                    var point = needleToPixels * needle.VisibleNeedleBase.Lerp(needle.UpperNeedleTip, progress);
                    int x = (int)Mathf.Round(point.X), y = (int)Mathf.Round(point.Y);
                    Require(x >= 0 && y >= 0 && x < shown.GetWidth() && y < shown.GetHeight(), "needle sample inside screenshot");
                    var on = shown.GetPixel(x, y);
                    var off = hidden.GetPixel(x, y);
                    float difference = Math.Abs(on.R - off.R) + Math.Abs(on.G - off.G) + Math.Abs(on.B - off.B);
                    MainFile.Logger.Info($"VISUAL_039_NEEDLE_PIXEL progress={progress} xy=({x},{y}) on={on} off={off} rgbDifference={difference}");
                    if (difference > 0.08f) needleChanged++;
                }
                Require(needleChanged >= 3, "upward needle visibly changes at least three screenshot samples");
                display.SetProcess(true);
            }
        }
        finally { circle.Modulate = Colors.White; needle.Modulate = Colors.White; display.SetProcess(true); window.Size = originalSize; }
        Require(before.Orbs.SequenceEqual(session.Orbs.Snapshot().Orbs) && positions.SequenceEqual(session.Orbs.Positions), "visual fixture preserves values activation locks order and foreground");
        MainFile.Logger.Info("REVISION_039_ORB_VISUAL_AUDIT_PASS rearCircle=true upwardNeedle=true idleArt=true stableNumerals=true resolutions=2");
    }
}
