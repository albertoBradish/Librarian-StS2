using System.Text.Json;
using Godot;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Animation;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace Librarian.Mechanics;

/// <summary>Native selection-only checks within the existing opt-in, isolated full run.</summary>
internal static class DevelopmentSelection137Audit
{
    private static IEnumerable<Node> Desc(Node n)
    {
        foreach (var child in n.GetChildren())
        {
            yield return child;
            foreach (var d in Desc(child)) yield return d;
        }
    }
    internal static async Task Run(NCharacterSelectScreen screen)
    {
        if (!DevelopmentVisualAudit.Enabled) return;
        int checks = 0;
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("Selection137: " + label);
            checks++; MainFile.Logger.Info("SELECTION137_CHECK_PASS " + label);
        }
        async Task Wait(double seconds = .2) => await screen.ToSignal(screen.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        string output = Path.GetFullPath(System.Environment.GetEnvironmentVariable("LIBRARIAN_VISUAL_OUTPUT")!);
        Check(output.StartsWith(@"D:\Slay The Spire_Mod Dev\outputs\", StringComparison.OrdinalIgnoreCase), "explicit isolated screenshot output");
        Check(OS.GetUserDataDir().Contains("revision030-userdata"), "isolated profile");
        Check(DisplayServer.GetName() != "headless", "native rendering display");
        var bg = screen.GetNode<Control>("AnimatedBg");
        var selection = bg.GetChild<LibrarianCharacterSelect137>(0);
        Check(bg is NCharacterSelectScreenBg && bg.GetChildCount() == 1, "native background controller and one selected scene");
        Check(selection.Spine.GetClass() == "SpineSprite", "actual game SpineSprite");
        Check(selection.Spine.GetChildren().OfType<NSpineAutoPlayer>().Count() == 1, "original auto-player is direct child exactly once");
        Check(selection.MouseFilter == Control.MouseFilterEnum.Ignore, "native menu input remains unobstructed");
        using (var bankValue = selection.Spine.Get("skeleton_data_res"))
        {
            var bank = bankValue.AsGodotObject()!;
            Check(bank.ResourcePath() == LibrarianCharacterSelect137.BankPath, "owned selection bank resource path");
            using var a = bank.Call("get_animations"); using var animations = a.AsGodotArray();
            Check(animations.Count == 1, "selection bank contains exactly one animation");
            var animation = animations[0].AsGodotObject()!;
            using var name = animation.Call("get_name"); using var duration = animation.Call("get_duration");
            Check(name.AsString() == LibrarianCharacterSelect137.LoopName && Math.Abs(duration.AsDouble() - 10) < .0001, "native loop metadata 10 seconds");
        }
        var prefs = LibrarianPreferences050.Current;
        var previous = (prefs.OrbIdle, prefs.Particles, prefs.ReducedMotion);
        var window = screen.GetWindow(); var oldSize = window.Size; var oldMode = window.Mode;
        var evidence = new List<object>();
        async Task Capture(string label)
        {
            await screen.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = screen.GetViewport().GetTexture().GetImage();
            string path = Path.Combine(output, label + ".png");
            Check(image.SavePng(path) == Error.Ok, "native frame " + label);
            evidence.Add(new { label, path, width = image.GetWidth(), height = image.GetHeight(), eye = selection.BonePosition("eye_anchor").ToString() });
        }
        try
        {
            prefs.ReducedMotion = false; prefs.OrbIdle = true; prefs.Particles = true;
            window.Mode = Window.ModeEnum.Windowed;
            foreach (var size in new[] { new Vector2I(1280,720), new Vector2I(1920,1080) })
            {
                window.Size = size; await Wait(.7);
                Check(selection.ArtworkRect.Encloses(selection.VisibleArtworkRect), "full viewport coverage " + size);
                Check(Math.Abs(selection.Spine.Scale.X - selection.Spine.Scale.Y) < .0001, "artwork aspect ratio " + size);
                Check(selection.Moving && selection.ParticlesEmitting, "independent skeleton and native particles enabled " + size);
                var hand0 = selection.BonePosition("near_hand"); var eye0 = selection.BonePosition("eye_anchor");
                await Capture("selection137-" + size.X + "-normal-0");
                await Wait(1.15);
                var hand1 = selection.BonePosition("near_hand"); var eye1 = selection.BonePosition("eye_anchor");
                Check(hand0.DistanceTo(hand1) > .02f && eye0.DistanceTo(eye1) > .02f, "real native animated bones advance " + size);
                await Capture("selection137-" + size.X + "-normal-1");
                if (size.X == 1280 && System.Environment.GetEnvironmentVariable("LIBRARIAN_SELECTION137_CAPTURE") == "1")
                {
                    string sequence = Path.Combine(output, "selection137-loop"); Directory.CreateDirectory(sequence);
                    ulong started = Time.GetTicksMsec(); var timeline = new List<object>();
                    while (Time.GetTicksMsec() - started < 10100)
                    {
                        await screen.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        double seconds = (Time.GetTicksMsec() - started) / 1000d;
                        using var frame = screen.GetViewport().GetTexture().GetImage();
                        string file = Path.Combine(sequence, $"{timeline.Count:0000}.png");
                        Check(frame.SavePng(file) == Error.Ok, "native loop frame " + timeline.Count);
                        timeline.Add(new { file, seconds });
                        await Wait(.035);
                    }
                    File.WriteAllText(Path.Combine(output,"selection137-loop-timeline.json"),JsonSerializer.Serialize(timeline));
                    MainFile.Logger.Info($"SELECTION137_LOOP_CAPTURE_PASS frames={timeline.Count} elapsedMs={Time.GetTicksMsec()-started}");
                }
                prefs.Particles = false; await Wait();
                Check(!selection.ParticlesEmitting, "particle setting suppresses both emitters " + size);
                await Capture("selection137-" + size.X + "-particles-off");
                prefs.ReducedMotion = true; await Wait(.35);
                var still = selection.LocalBonePosition("near_hand"); await Wait(.5);
                MainFile.Logger.Info($"SELECTION137_STILL moving={selection.Moving} particles={selection.ParticlesEmitting} localDelta={still.DistanceTo(selection.LocalBonePosition("near_hand"))}");
                Check(!selection.Moving && !selection.ParticlesEmitting && still.DistanceTo(selection.LocalBonePosition("near_hand")) < .01f,
                    "reduced motion stabilizes bones and decoration " + size);
                await Capture("selection137-" + size.X + "-reduced");
                prefs.ReducedMotion = false; prefs.OrbIdle = false; await Wait(.3);
                still = selection.LocalBonePosition("near_hand"); await Wait(.3);
                Check(still.DistanceTo(selection.LocalBonePosition("near_hand")) < .01f, "idle toggle stabilizes skeleton " + size);
                prefs.OrbIdle = true; prefs.Particles = true; await Wait(.3);
            }
            var other = Desc(screen).OfType<NCharacterSelectButton>().Single(b => b.Character is Ironclad);
            var own = Desc(screen).OfType<NCharacterSelectButton>().Single(b => b.Character is LibrarianCharacter);
            var old = selection;
            other.Select(); await Wait(.7);
            Check(!GodotObject.IsInstanceValid(old), "old owned selection tree released on native switch");
            Check(!Desc(bg).OfType<LibrarianCharacterSelect137>().Any(), "no owned emitters remain under another character");
            own.Select(); await Wait(.7);
            selection = bg.GetChild<LibrarianCharacterSelect137>(0);
            Check(selection.Spine.GetChildren().OfType<NSpineAutoPlayer>().Count() == 1, "reselection creates exactly one native auto-player");
        }
        finally
        {
            (prefs.OrbIdle, prefs.Particles, prefs.ReducedMotion) = previous;
            window.Size = oldSize; window.Mode = oldMode; await Wait(.3);
        }
        File.WriteAllText(Path.Combine(output,"selection137-native.json"),JsonSerializer.Serialize(new { passed=true, checks, frames=evidence, nativeRuntime=true, visualAcceptance="pending" },new JsonSerializerOptions{WriteIndented=true}));
        MainFile.Logger.Info($"SELECTION137_AUDIT_PASS checks={checks} layouts=2 settings=True switchCleanup=True nativeSpine=True");
    }
    private static string ResourcePath(this GodotObject resource) => resource is Resource r ? r.ResourcePath : "";
}
