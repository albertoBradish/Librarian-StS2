using Godot;
using Librarian.LibrarianCode;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace Librarian.Mechanics;

/// <summary>Native rendered selection regression after the user's V0.5.0 reset.</summary>
internal static class DevelopmentSelection050Audit
{
    internal static async Task Run(NCharacterSelectScreen screen)
    {
        if (!DevelopmentVisualAudit.Enabled) return;
        int checks = 0;
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("Selection050: " + label);
            checks++;
            MainFile.Logger.Info("SELECTION050_CHECK_PASS " + label);
        }
        async Task Wait(double seconds) => await screen.ToSignal(screen.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        Check(OS.GetUserDataDir().Contains("revision030-userdata"), "isolated validation profile");
        Check(DisplayServer.GetName() != "headless", "native renderer");
        var output = Path.GetFullPath(System.Environment.GetEnvironmentVariable("LIBRARIAN_VISUAL_OUTPUT")!);
        Check(output.StartsWith(@"D:\Slay The Spire_Mod Dev\outputs\", StringComparison.OrdinalIgnoreCase), "workspace screenshots");
        Directory.CreateDirectory(output);
        var parent = screen.GetNode<Control>("AnimatedBg");
        Check(parent is NCharacterSelectScreenBg, "native background resize controller retained");
        Check(parent.GetChildCount() == 1, "one selected character background");
        var selection = parent.GetChild<LibrarianCharacterSelect040>(0);
        var painting = selection.Illustration;
        Check(painting.Texture.ResourcePath == LibrarianCharacterSelect040.IllustrationPath, "approved continuous painting retained");
        Check(selection.GetChildCount() == 1 && painting.GetChildCount() == 0, "no overlay dust or extra hand/orb sprites");
        Check(painting.Material is null && painting.Modulate == Colors.White, "unmodified painting without distortion or pulse shader");
        Check(selection.MouseFilter == Control.MouseFilterEnum.Ignore && painting.MouseFilter == Control.MouseFilterEnum.Ignore,
            "background cannot intercept native button input");
        var window = screen.GetWindow();
        var original = window.Size;
        try
        {
            foreach (var requested in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080), new Vector2I(1280, 800) })
            {
                window.Size = requested;
                await Wait(0.65);
                var position = painting.Position;
                var size = painting.Size;
                var rect = new Rect2(position, size);
                Check(rect.Encloses(selection.VisibleArtworkRect), "painting covers viewport " + requested);
                Check(painting.Rotation == 0 && painting.Scale == Vector2.One, "no artificial rocking or zoom " + requested);
                await Wait(0.7);
                Check(painting.Position.IsEqualApprox(position) && painting.Size.IsEqualApprox(size), "idle composition remains stable " + requested);
                await screen.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = screen.GetViewport().GetTexture().GetImage();
                string path = Path.Combine(output, $"selection-reset-request-{requested.X}x{requested.Y}.png");
                Check(image.SavePng(path) == Error.Ok, "rendered screenshot " + requested);
                MainFile.Logger.Info($"SELECTION050_SCREENSHOT path={path} actual={image.GetWidth()}x{image.GetHeight()}");
            }
        }
        finally { window.Size = original; await Wait(0.3); }
        MainFile.Logger.Info($"SELECTION050_AUDIT_PASS checks={checks} requestedLayouts=3 nativeSelectionFlow=True spineMigration=False");
    }
}
