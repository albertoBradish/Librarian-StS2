using System.IO;
using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace Librarian.Mechanics;

internal static class DevelopmentRevision0311OrbVisualAudit
{
    private static string Output => System.Environment.GetEnvironmentVariable("LIBRARIAN_040_ONLY") == "1"
        ? @"D:\Slay The Spire_Mod Dev\.research\revision-v040-screenshots"
        : @"D:\Slay The Spire_Mod Dev\.research\revision-v0311-screenshots";
    private static void Require(bool pass, string text)
    {
        if (!pass) throw new InvalidOperationException("0311 orb visual: " + text);
        MainFile.Logger.Info("VISUAL_0311_ORB_CHECK_PASS " + text);
    }
    private static async Task Wait(double seconds = 0.45)
        => await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private static bool HasReference(Node node)
        => node.Name.ToString().StartsWith("LibrarianReference", StringComparison.Ordinal)
            || node.GetType().Name == "LibrarianOrbReferenceStrip" || node.GetChildren().Any(HasReference);
    private static async Task<Image> Capture(string name)
    {
        await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = NGame.Instance.GetViewport().GetTexture().GetImage();
        Require(image.SavePng(Path.Combine(Output, name + ".png")) == Error.Ok, "saved " + name);
        return image;
    }
    internal static async Task Run(Player player)
    {
        if (!DevelopmentVisualAudit.Enabled) return;
        Require(OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase), "isolated fixture profile");
        Require(DisplayServer.GetName() != "headless", "actual rendering enabled");
        Directory.CreateDirectory(Output);
        var session = LibrarianRuntime.Get(player);
        LibrarianOrbPanel.Refresh(session);
        await Wait(1);
        var display = LibrarianOrbPanel.GetDisplay(session)!;
        var before = session.Orbs.Snapshot();
        var window = NGame.Instance!.GetWindow();
        var originalSize = window.Size;
        NSimpleCardSelectScreen? selector = null;
        try
        {
            foreach (var resolution in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                window.Size = resolution; await Wait();
                string suffix = $"{resolution.X}x{resolution.Y}";
                selector = NSimpleCardSelectScreen.Create(player.PlayerCombatState!.Hand.Cards.Take(1).ToArray(),
                    new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, 0, 1) { Cancelable = true });
                NOverlayStack.Instance!.Push(selector);
                await Wait();
                Require(ActiveScreenContext.Instance.IsCurrent(selector), "native selection is active");
                Require(display.IsVisibleInTree() && display.MagicCircle.IsVisibleInTree(), "original combat orbs stay mounted during selection");
                Require(!HasReference(NGame.Instance), "selection creates no reference strip or additional orb window");
                Require(Enum.GetValues<OrbKind>().All(k => display.GetNode<Control>(k.ToString()).MouseFilter == Control.MouseFilterEnum.Ignore), "combat orb hover does not intercept selector");
                using var selected = await Capture("0311-native-selection-" + suffix);
                var peek = selector.GetNode<NPeekButton>("%PeekButton");
                peek.SetPeeking(true);
                await Wait();
                Require(peek.IsPeeking && display.IsVisibleInTree(), "native peek reveals original battlefield");
                Require(!HasReference(NGame.Instance), "native peek creates no extra status UI");
                display.SetProcess(false);
                using var shown = await Capture("0311-native-peek-" + suffix);
                display.Modulate = Colors.Transparent;
                display.MagicCircle.Modulate = Colors.Transparent;
                using var hidden = await Capture("0311-native-peek-hidden-" + suffix);
                foreach (var kind in Enum.GetValues<OrbKind>())
                {
                    var slot = display.GetNode<Control>(kind.ToString());
                    var transform = slot.GetViewport().GetStretchTransform() * slot.GetGlobalTransformWithCanvas();
                    int changed = 0;
                    for (int x = 16; x <= 56; x += 8) for (int y = 16; y <= 56; y += 8)
                    {
                        var point = transform * new Vector2(x, y);
                        int px = (int)point.X, py = (int)point.Y;
                        if (px < 0 || py < 0 || px >= shown.GetWidth() || py >= shown.GetHeight()) continue;
                        var on = shown.GetPixel(px, py); var off = hidden.GetPixel(px, py);
                        if (Math.Abs(on.R - off.R) + Math.Abs(on.G - off.G) + Math.Abs(on.B - off.B) > 0.08f) changed++;
                    }
                    Require(changed >= 5, "native peek shows actual original orb pixels " + kind);
                }
                display.Modulate = Colors.White; display.MagicCircle.Modulate = Colors.White; display.SetProcess(true);
                peek.SetPeeking(false);
                await Wait(0.1);
                Require(ActiveScreenContext.Instance.IsCurrent(selector), "return from peek preserves native selection");
                NOverlayStack.Instance.Remove(selector); selector = null;
                await Wait();
                Require(!HasReference(NGame.Instance), "exit selection leaves no additional orb window");
                Require(display.GetNode<Control>(OrbKind.Fire.ToString()).MouseFilter == Control.MouseFilterEnum.Pass, "normal orb hover restored");
                Require(display.MagicCircle.EntranceProgress == 1, "peek and selector do not restart ring entrance");
                using var exited = await Capture("0311-selection-closed-" + suffix);
            }
            Require(before.Orbs.SequenceEqual(session.Orbs.Snapshot().Orbs), "native selection and peek preserve orb state");
        }
        finally
        {
            if (selector is not null && GodotObject.IsInstanceValid(selector)) NOverlayStack.Instance?.Remove(selector);
            display.Modulate = Colors.White; display.MagicCircle.Modulate = Colors.White; display.SetProcess(true);
            window.Size = originalSize;
        }
        MainFile.Logger.Info("REVISION_0311_ORB_VISUAL_AUDIT_PASS noReferenceStrip=true nativeSelection=true nativePeek=true originalOrbs=true resolutions=2");
    }
}
