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
internal static class DevelopmentSelection138Audit
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
            if (!ok) throw new InvalidOperationException("Selection138: " + label);
            checks++; MainFile.Logger.Info("SELECTION138_CHECK_PASS " + label);
        }
        async Task Wait(double seconds = .2) => await screen.ToSignal(screen.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        string output = Path.GetFullPath(System.Environment.GetEnvironmentVariable("LIBRARIAN_VISUAL_OUTPUT")!);
        Check(output.StartsWith(@"D:\Slay The Spire_Mod Dev\outputs\", StringComparison.OrdinalIgnoreCase), "explicit isolated screenshot output");
        Check(OS.GetUserDataDir().Contains("revision030-userdata"), "isolated profile");
        Check(DisplayServer.GetName() != "headless", "native rendering display");
        var bg = screen.GetNode<Control>("AnimatedBg");
        var selection = bg.GetChild<LibrarianCharacterSelect138>(0);
        Check(bg is NCharacterSelectScreenBg && bg.GetChildCount() == 1, "native background controller and one selected scene");
        Check(selection.Spine.GetClass() == "SpineSprite", "actual game SpineSprite");
        Check(selection.Spine.GetChildren().OfType<NSpineAutoPlayer>().Count() == 1, "original auto-player is direct child exactly once");
        Check(selection.MouseFilter == Control.MouseFilterEnum.Ignore, "native menu input remains unobstructed");
        using (var bankValue = selection.Spine.Get("skeleton_data_res"))
        {
            var bank = bankValue.AsGodotObject()!;
            Check(bank.ResourcePath() == LibrarianCharacterSelect138.BankPath, "owned selection bank resource path");
            using var a = bank.Call("get_animations"); using var animations = a.AsGodotArray();
            Check(animations.Count == 1, "selection bank contains exactly one animation");
            var animation = animations[0].AsGodotObject()!;
            using var name = animation.Call("get_name"); using var duration = animation.Call("get_duration");
            Check(name.AsString() == LibrarianCharacterSelect138.LoopName && Math.Abs(duration.AsDouble() - 10) < .0001, "native loop metadata 10 seconds");
        }
        var prefs = LibrarianPreferences050.Current;
        var previous = (prefs.OrbIdle, prefs.Particles, prefs.ReducedMotion);
        var window = screen.GetWindow(); var oldSize = window.Size; var oldMode = window.Mode;
        var evidence = new List<object>();
        async Task Capture(string label)
        {
            await screen.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = screen.GetViewport().GetTexture().GetImage();
            Check(!image.IsEmpty(), "native rendered image " + label);
            if (!DevelopmentVisualAudit.SaveScreenshots) { evidence.Add(new { label, saved = false, width = image.GetWidth(), height = image.GetHeight() }); return; }
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
                Check(selection.BeamsEmitting && selection.RearLayerOrder, "native segmented emitters between opaque background and transparent actor " + size);
                Check(selection.DecorationCount == Math.Clamp(prefs.EffectLimit,4,40), "dust and beams share the configured decoration budget " + size);
                var hand0 = selection.BonePosition("near_hand"); var eye0 = selection.BonePosition("eye_anchor");
                await Capture("selection138-" + size.X + "-normal-0");
                await Wait(1.15);
                var hand1 = selection.BonePosition("near_hand"); var eye1 = selection.BonePosition("eye_anchor");
                Check(hand0.DistanceTo(hand1) > .02f && eye0.DistanceTo(eye1) > .02f, "real native animated bones advance " + size);
                await Capture("selection138-" + size.X + "-normal-1");
                if (DevelopmentVisualAudit.SaveScreenshots && size.X == 1280 && System.Environment.GetEnvironmentVariable("LIBRARIAN_SELECTION138_CAPTURE") == "1")
                {
                    string sequence = Path.Combine(output, "selection138-loop"); Directory.CreateDirectory(sequence);
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
                    File.WriteAllText(Path.Combine(output,"selection138-loop-timeline.json"),JsonSerializer.Serialize(timeline));
                    MainFile.Logger.Info($"SELECTION138_LOOP_CAPTURE_PASS frames={timeline.Count} elapsedMs={Time.GetTicksMsec()-started}");
                }
                prefs.Particles = false; await Wait();
                Check(!selection.ParticlesEmitting, "particle setting suppresses both emitters " + size);
                await Capture("selection138-" + size.X + "-particles-off");
                prefs.ReducedMotion = true; await Wait(.35);
                var still = selection.LocalBonePosition("near_hand"); await Wait(.5);
                MainFile.Logger.Info($"SELECTION138_STILL moving={selection.Moving} particles={selection.ParticlesEmitting} localDelta={still.DistanceTo(selection.LocalBonePosition("near_hand"))}");
                Check(!selection.Moving && !selection.ParticlesEmitting && still.DistanceTo(selection.LocalBonePosition("near_hand")) < .01f,
                    "reduced motion stabilizes bones and decoration " + size);
                await Capture("selection138-" + size.X + "-reduced");
                prefs.ReducedMotion = false; prefs.OrbIdle = false; await Wait(.3);
                still = selection.LocalBonePosition("near_hand"); await Wait(.3);
                Check(still.DistanceTo(selection.LocalBonePosition("near_hand")) < .01f, "idle toggle stabilizes skeleton " + size);
                prefs.OrbIdle = true; prefs.Particles = true; await Wait(.3);
            }
            // Render an opt-in diagnostic behind the actor. The exposed
            // background must change while a covered book pixel stays intact.
            prefs.ReducedMotion = true; prefs.Particles = false; await Wait(.3);
            var empty = new Vector2(200,100); var covered = new Vector2(1050,730);
            Vector2I Pixel(Vector2 point) => (Vector2I)(selection.Spine.GetGlobalTransformWithCanvas() * point);
            float Difference(Color a, Color b) => Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B);
            using (var before = screen.GetViewport().GetTexture().GetImage())
            {
                var strip = new Line2D { Name="TemporaryRearOcclusionCheck",
                    Position=selection.Spine.Position, Scale=selection.Spine.Scale,
                    Points=[new(100,100),new(300,100),new(1000,730),new(1100,730)], Width=30, DefaultColor=new Color(1,0,1) };
                selection.AddChild(strip); selection.MoveChild(strip,selection.Spine.GetIndex()); await Wait(.15);
                await screen.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                using var after=screen.GetViewport().GetTexture().GetImage();
                if (DevelopmentVisualAudit.SaveScreenshots) before.SavePng(Path.Combine(output,"selection138-rear-occlusion-before.png"));
                if (DevelopmentVisualAudit.SaveScreenshots) after.SavePng(Path.Combine(output,"selection138-rear-occlusion-proof.png"));
                MainFile.Logger.Info($"SELECTION138_OCCLUSION emptyPixel={Pixel(empty)} coveredPixel={Pixel(covered)} emptyDifference={Difference(before.GetPixelv(Pixel(empty)),after.GetPixelv(Pixel(empty)))} coveredDifference={Difference(before.GetPixelv(Pixel(covered)),after.GetPixelv(Pixel(covered)))}");
                Check(Difference(before.GetPixelv(Pixel(empty)),after.GetPixelv(Pixel(empty)))>.3f,"rear layer visible over background in native renderer");
                Check(Difference(before.GetPixelv(Pixel(covered)),after.GetPixelv(Pixel(covered)))<.02f,"actual book pixels occlude rear layer in native renderer");
                if (DevelopmentVisualAudit.SaveScreenshots) Check(after.SavePng(Path.Combine(output,"selection138-rear-occlusion-proof.png"))==Error.Ok,"native rear occlusion proof saved");
                strip.QueueFree(); await Wait(.15);
            }
            // Regression for the user-reported pasted background at the wrist.
            // Real native compositing must expose the backdrop through removed
            // matte regions, while adjacent original hand pixels stay opaque.
            var maskPoints = new (string Name, Vector2 Point, bool Transparent)[]
            {
                ("near-wrist", new(640,421), true),
                ("near-palm-gap", new(600,390), true),
                ("far-knuckle-gap", new(1590,430), true),
                ("near-finger", new(480,340), false)
            };
            using (var before = screen.GetViewport().GetTexture().GetImage())
            {
                var proof = new Node2D { Name="Selection1312MatteProof", Position=selection.Spine.Position, Scale=selection.Spine.Scale };
                foreach (var point in maskPoints)
                    proof.AddChild(new Polygon2D { Polygon=[point.Point-new Vector2(7,7),point.Point+new Vector2(7,-7),point.Point+new Vector2(7,7),point.Point+new Vector2(-7,7)], Color=new Color(1,0,1) });
                selection.AddChild(proof); selection.MoveChild(proof,selection.Spine.GetIndex()); await Wait(.15);
                await screen.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                using var after=screen.GetViewport().GetTexture().GetImage();
                foreach (var point in maskPoints)
                {
                    float difference=Difference(before.GetPixelv(Pixel(point.Point)),after.GetPixelv(Pixel(point.Point)));
                    MainFile.Logger.Info($"SELECTION1312_MATTE name={point.Name} transparent={point.Transparent} difference={difference}");
                    Check(point.Transparent ? difference>.3f : difference<.02f,"native corrected alpha "+point.Name);
                }
                if (DevelopmentVisualAudit.SaveScreenshots) Check(after.SavePng(Path.Combine(output,"selection1312-matte-proof.png"))==Error.Ok,"native corrected matte proof saved");
                proof.QueueFree(); await Wait(.15);
            }
            // Duplicate the real emitter, preserving its native velocity/forces.
            // A single magenta beam over an empty part of the same live background
            // lets two actual rendered frames measure horizontal drift directly.
            prefs.ReducedMotion=false; prefs.OrbIdle=false; prefs.Particles=true; await Wait(.3);
            var beam=(CpuParticles2D)selection.PrimaryBeam.Duplicate();
            beam.Name="NativeVerticalTrajectoryProbe"; beam.Emitting=false;
            beam.Amount=1; beam.OneShot=true; beam.Explosiveness=1; beam.Preprocess=0;
            beam.Position=new Vector2(110,300); beam.EmissionShape=CpuParticles2D.EmissionShapeEnum.Point;
            beam.Color=Colors.White; beam.Modulate=Colors.White; beam.Randomness=0;
            beam.ColorRamp=new Gradient { Offsets=[0,1], Colors=[new Color(1,0,1),new Color(1,0,1)] };
            selection.RearLayer.AddChild(beam); beam.Restart(); beam.Emitting=true;
            try
            {
                Check(beam.Direction==Vector2.Up && beam.Spread==0 && beam.Gravity.X==0,"native beam has no angular spread or lateral acceleration");
                async Task<Vector2> BeamCenter(string name)
                {
                    await screen.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    using var im=screen.GetViewport().GetTexture().GetImage();
                    var t=selection.RearLayer.GetGlobalTransformWithCanvas();
                    Vector2 lo=t*new Vector2(80,20), hi=t*new Vector2(140,460);
                    double sx=0,sy=0;int count=0;
                    for(int y=Math.Max(0,(int)lo.Y);y<Math.Min(im.GetHeight(),(int)hi.Y);y++)
                        for(int x=Math.Max(0,(int)lo.X);x<Math.Min(im.GetWidth(),(int)hi.X);x++)
                        {
                            var c=im.GetPixel(x,y);
                            if(c.R>.80f && c.B>.60f && c.G<.75f){sx+=x;sy+=y;count++;}
                        }
                    if (DevelopmentVisualAudit.SaveScreenshots) Check(im.SavePng(Path.Combine(output,"selection139-vertical-"+name+".png"))==Error.Ok,"native trajectory capture "+name);
                    MainFile.Logger.Info($"SELECTION139_VERTICAL_SAMPLE name={name} pixels={count} lo={lo} hi={hi} position={beam.Position} emitting={beam.Emitting}");
                    Check(count>20,"native trajectory beam visible "+name);
                    return new Vector2((float)(sx/count),(float)(sy/count));
                }
                await Wait(.45);var a=await BeamCenter("start");
                await Wait(1.1);var b=await BeamCenter("later");
                Check(Math.Abs(b.X-a.X)<1.5f && a.Y-b.Y>20,"rendered beam rises vertically with less than 1.5px lateral drift");
                File.WriteAllText(Path.Combine(output,"selection139-vertical-motion.json"),JsonSerializer.Serialize(new { passed=true,start=new{x=a.X,y=a.Y},later=new{x=b.X,y=b.Y},horizontalDrift=Math.Abs(b.X-a.X),rise=a.Y-b.Y,source="duplicate of actual native production beam, identical direction/velocity/forces; only spawn/count/color changed" }));
                MainFile.Logger.Info($"SELECTION139_VERTICAL_PASS horizontalDrift={Math.Abs(b.X-a.X)} rise={a.Y-b.Y}");
            }
            finally { beam.QueueFree(); await Wait(.15); }
            prefs.ReducedMotion=false; prefs.OrbIdle=true; prefs.Particles=true; await Wait(.2);
            var other = Desc(screen).OfType<NCharacterSelectButton>().Single(b => b.Character is Ironclad);
            var own = Desc(screen).OfType<NCharacterSelectButton>().Single(b => b.Character is LibrarianCharacter);
            var old = selection;
            other.Select(); await Wait(.7);
            Check(!GodotObject.IsInstanceValid(old), "old owned selection tree released on native switch");
            Check(!Desc(bg).OfType<LibrarianCharacterSelect138>().Any(), "no owned emitters remain under another character");
            own.Select(); await Wait(.7);
            selection = bg.GetChild<LibrarianCharacterSelect138>(0);
            Check(selection.Spine.GetChildren().OfType<NSpineAutoPlayer>().Count() == 1, "reselection creates exactly one native auto-player");
        }
        finally
        {
            (prefs.OrbIdle, prefs.Particles, prefs.ReducedMotion) = previous;
            window.Size = oldSize; window.Mode = oldMode; await Wait(.3);
        }
        File.WriteAllText(Path.Combine(output,"selection138-native.json"),JsonSerializer.Serialize(new { passed=true, checks, frames=evidence, nativeRuntime=true, visualAcceptance="pending" },new JsonSerializerOptions{WriteIndented=true}));
        MainFile.Logger.Info($"SELECTION138_AUDIT_PASS checks={checks} layouts=2 settings=True switchCleanup=True nativeSpine=True");
    }
    private static string ResourcePath(this GodotObject resource) => resource is Resource r ? r.ResourcePath : "";
}
