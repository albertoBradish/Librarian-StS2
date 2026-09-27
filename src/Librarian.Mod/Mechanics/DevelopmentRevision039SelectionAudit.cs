using System.IO;
using Godot;
using Librarian.LibrarianCode;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace Librarian.Mechanics;

/// <summary>Invoke only from the opt-in isolated visual fixture, after selecting Librarian in the native menu.</summary>
internal static class DevelopmentRevision039SelectionAudit
{
    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    internal static async Task Capture(NCharacterSelectScreen screen)
    {
        if (!DevelopmentVisualAudit.Enabled) return;
        string output = System.Environment.GetEnvironmentVariable("LIBRARIAN_VISUAL_OUTPUT")
            ?? throw new InvalidOperationException("039 selection audit needs an explicit output path.");
        string full = Path.GetFullPath(output);
        if (!Path.IsPathFullyQualified(output) || !(full.StartsWith(@"D:\Slay The Spire_Mod Dev\.research\", StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(@"D:\Slay The Spire_Mod Dev\outputs\revision-v1.0.0-stable\audit-history\", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("039 selection screenshots must stay in the workspace.");
        Directory.CreateDirectory(full);
        int checks = 0;
        void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException("039 selection: " + message);
            checks++;
            MainFile.Logger.Info("SELECTION039_CHECK_PASS " + message);
        }
        async Task Frames(int count = 3)
        {
            for (int i = 0; i < count; i++)
                await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        var selection = Descendants(screen.GetNode("AnimatedBg")).OfType<LibrarianCharacterSelect039>().Single();
        Check(selection.IsVisibleInTree(), "new scene is visible in native selection background");
        var hero = selection.Hero;
        Check(hero.Texture.ResourcePath == LibrarianCharacterSelect039.HeroPath, "new complete illustration, no old body-part substitution");
        Check(Descendants(selection).OfType<TextureRect>().Count() == 2, "one continuous hero and one environment texture");
        Check(Descendants(selection).OfType<Control>().All(c => c.MouseFilter == Control.MouseFilterEnum.Ignore), "art does not intercept native menu input");
        using (var art = hero.Texture.GetImage())
        {
            if (art.IsCompressed()) Check(art.Decompress() == Error.Ok, "hero pixels decompress");
            Check(art.DetectAlpha() != Image.AlphaMode.None, "new illustration has real alpha");
        }
        var window = screen.GetWindow();
        Vector2I previousSize = window.Size;
        bool previousPause = selection.AuditPaused;
        double previousTime = selection.AnimationTime;
        try
        {
            selection.AuditPaused = true;
            foreach (var resolution in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                window.Size = resolution;
                await Frames(8);
                selection.SampleAt(0);
                Vector2 origin = hero.Position;
                Check(hero.Size.X > 0 && hero.Size.Y > 0 && hero.Position.X >= selection.VisibleArtworkRect.Position.X + selection.VisibleArtworkRect.Size.X * 0.43f,
                    "illustration reserves left information region " + resolution);
                Check(Mathf.Abs(hero.Size.X / hero.Size.Y - hero.Texture.GetSize().X / hero.Texture.GetSize().Y) < 0.001f,
                    "illustration aspect ratio preserved " + resolution);
                foreach (double time in new[] { 0d, 2d })
                {
                    selection.SampleAt(time);
                    await Frames();
                    Check(Mathf.Abs(hero.Rotation) <= 0.0051f && hero.Scale.X >= 0.998f && hero.Scale.X <= 1.0021f,
                        "bounded whole-image motion " + resolution + " t=" + time);
                    Check(Mathf.Abs(((ShaderMaterial)hero.Material).GetShaderParameter("motion_time").AsSingle() - (float)time) < 0.001f,
                        "page shader receives sampled animation time " + resolution + " t=" + time);
                    await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var frame = screen.GetViewport().GetTexture().GetImage();
                    Check(frame.GetWidth() == resolution.X && frame.GetHeight() == resolution.Y, "actual screenshot dimensions " + resolution);
                    string file = Path.Combine(full, $"039-character-select-{resolution.X}x{resolution.Y}-t{time:0}.png");
                    Check(frame.SavePng(file) == Error.Ok, "selection screenshot saved");
                    MainFile.Logger.Info("SELECTION039_SCREENSHOT path=" + file);
                }
                float distance = hero.Position.DistanceTo(origin), unit = selection.VisibleArtworkRect.Size.Y / 1080f;
                Check(distance > 5f * unit && distance < 11f * unit, "sampled breathing visibly moves continuous illustration " + resolution);
            }
            double held = selection.AnimationTime;
            await Frames();
            Check(selection.AnimationTime == held, "sampling pause is stable");
            selection.AuditPaused = false;
            await Frames();
            Check(selection.AnimationTime > held, "live process resumes animation");
        }
        finally
        {
            selection.AuditPaused = true;
            window.Size = previousSize;
            await Frames();
            selection.SampleAt(previousTime);
            selection.AuditPaused = previousPause;
        }
        MainFile.Logger.Info($"SELECTION039_AUDIT_PASS checks={checks} resolutions=2 frames=4 continuousHero=true userArtReview=pending");
    }
}
