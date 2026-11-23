using System.IO;
using System.Text.Json;
using System.Threading;
using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace Librarian.Mechanics;

/// <summary>Opt-in, bounded real viewport evidence. Never drives the UI or changes game time.</summary>
internal static class DevelopmentCardRemasterCapture
{
    private static readonly HashSet<string> Claimed = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> Counts = new(StringComparer.Ordinal);
    private static readonly List<Task> Pending = [];
    private static readonly List<object> Evidence = [];
    private static readonly List<string> Errors = [];
    private static string _directory = "";
    private static CancellationTokenSource? _cancel;
    internal static bool Enabled { get; private set; }
    internal static int Saved { get; private set; }

    internal static void Configure(string output)
    {
        Enabled = System.Environment.GetEnvironmentVariable("LIBRARIAN_CARD_REMASTER_CAPTURE") == "1"
            && System.Environment.GetEnvironmentVariable("LIBRARIAN_CARD_REMASTER_PHASE") != "restart";
        if (!Enabled) return;
        _cancel?.Dispose();
        _cancel = new();
        _directory = Path.Combine(output, "scratch", "vfx-keyframes");
        Directory.CreateDirectory(_directory);
        Claimed.Clear(); Counts.Clear(); Pending.Clear(); Evidence.Clear(); Errors.Clear(); Saved = 0;
    }

    internal static void Observe(string scenario, LibrarianCardRemasterObservation observation)
    {
        if (!Enabled) return;
        bool settings = scenario.StartsWith("settings:", StringComparison.Ordinal);
        bool selected = scenario == "manual:" + observation.CardClass + ":False"
            && observation.CardClass is "LibrarianStrike" or "NeedleFlurry" or "FlameStrike"
                or "TidalErosion" or "SteamBlast" or "Rootbind" or "WearyIncantation" or "Bookworm" or "TreeRingBurst";
        if (!settings && !selected) return;
        if (observation.Phase == "flight")
            Schedule(scenario, "flight-middle", observation.CardClass,
                observation.Effect is LibrarianCardFlightVfx flight ? flight.TravelDuration * .45 : .075, observation.Effect);
        else if (observation.Phase == "contact")
        {
            Schedule(scenario, "contact", observation.CardClass, 0, observation.Effect);
            Schedule(scenario, "residue", observation.CardClass, .13, observation.Effect);
        }
        else if (observation.Phase == "native-result" && scenario is "settings:Instant" or "settings:CardEffectsOff")
            Schedule(scenario, "native-only", observation.CardClass, 0, null);
    }

    internal static void Observe(string scenario, string phase, string source)
    {
        if (!Enabled) return;
        if(source=="MultiplayerPlaceholderB" && phase is "binding-registered" or "binding-payout-start" or "binding-payout-complete")
        {
            Schedule(scenario,phase,source,.06,null);
            return;
        }
        if (scenario == "boundary:fatal-earth-collapse" && phase == "impact" && source == "EarthCollapse")
        {
            Schedule(scenario,"fatal-impact-"+Counts.GetValueOrDefault(scenario),source,.04,null);
            return;
        }
        if (!scenario.EndsWith(":False", StringComparison.Ordinal)) return;
        bool selected = (phase == "block" && source == "LibrarianDefend")
            || (phase == "impact" && source == "EarthCollapse")
            || (phase == "status" && source == "StudyNotes")
            || (phase == "trigger" && source is "SeedburialStrike" or "LifeSymphony" or "ToBeContinued" or "UnretreatingTide");
        if (selected) Schedule(scenario, phase, source, .04, null);
    }

    private static void Schedule(string scenario, string phase, string source, double delay, Node2D? effect)
    {
        if (Counts.GetValueOrDefault(scenario) >= 3 || Claimed.Count >= 64 || !Claimed.Add(scenario + ":" + phase)) return;
        Counts[scenario] = Counts.GetValueOrDefault(scenario) + 1;
        Pending.Add(Capture(scenario, phase, source, delay, effect));
    }

    private static async Task Capture(string scenario, string phase, string source, double delay, Node2D? effect)
    {
        try
        {
            var game = NGame.Instance ?? throw new InvalidOperationException("Native game is unavailable for requested capture.");
            ulong requestedFrame = Engine.GetProcessFrames();
            if (delay > 0 && !await WaitSignal(game.GetTree().CreateTimer(delay), SceneTreeTimer.SignalName.Timeout)) return;
            if (!await WaitSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw)) return;
            using var image = game.GetViewport().GetTexture().GetImage();
            string name = string.Join('_', scenario.Split(Path.GetInvalidFileNameChars())) + "-" + phase + ".png";
            string path = Path.Combine(_directory, name);
            if (image.IsEmpty() || image.SavePng(path) != Error.Ok) throw new IOException("Unable to save native VFX keyframe: " + path);
            bool valid = effect is not null && GodotObject.IsInstanceValid(effect) && effect.IsInsideTree();
            var flight = valid ? effect as LibrarianCardFlightVfx : null;
            var window=game.GetWindow();
            Vector2I display=DisplayServer.WindowGetSize();
            Vector2I content=window.ContentScaleSize;
            var layerBounds=new List<object>();
            var biteBounds=new List<object>();
            if(valid)
            {
                foreach(var particles in Descendants(effect!).OfType<GpuParticles2D>())
                {
                    // Opt-in evidence only: CaptureRect synchronizes the GPU. Never used in production sizing.
                    Rect2 local=particles.CaptureRect();
                    Rect2 global=particles.GetGlobalTransform()*local;
                    layerBounds.Add(new {name=particles.Name.ToString(),local_coords=particles.LocalCoords,
                        source_local_coords=particles.HasMeta("librarian_source_local_coords")?particles.GetMeta("librarian_source_local_coords").AsBool():particles.LocalCoords,
                        node_scale=new{x=particles.Scale.X,y=particles.Scale.Y},
                        process_material_class=particles.ProcessMaterial?.GetClass().ToString(),
                        local_rect=new{x=local.Position.X,y=local.Position.Y,width=local.Size.X,height=local.Size.Y},
                        global_rect=new{x=global.Position.X,y=global.Position.Y,width=global.Size.X,height=global.Size.Y}});
                }
                foreach(var bite in Descendants(effect!).OfType<LibrarianBoundedBiteVfx>())
                {
                    Rect2 bounds=bite.GetGlobalTransform()*bite.LastNativeBounds;
                    biteBounds.Add(new{fitted=bite.NativeFitted,x=bounds.Position.X,y=bounds.Position.Y,width=bounds.Size.X,height=bounds.Size.Y});
                }
            }
            Evidence.Add(new
            {
                scenario, phase, source, file = name, requested_frame = requestedFrame,
                captured_frame = Engine.GetProcessFrames(), width = image.GetWidth(), height = image.GetHeight(),
                effect_valid = valid, flight_progress = flight?.Progress, contact = flight?.HasContact,
                display_server_window_size=new{x=display.X,y=display.Y},
                window_size=new{x=window.Size.X,y=window.Size.Y},
                content_scale_size=new{x=content.X,y=content.Y},
                viewport_texture_size=new{x=image.GetWidth(),y=image.GetHeight()},
                target_design_size=flight is null?null:new{x=flight.TargetDesignSize.X,y=flight.TargetDesignSize.Y},
                native_layer_bounds=layerBounds,
                native_bite_bounds=biteBounds,
                note = "Real native viewport after FramePostDraw; readback/PNG time is capture overhead, not a performance sample."
            });
            Saved++;
        }
        catch (Exception error) { Errors.Add(scenario + "/" + phase + ": " + error); }
    }
    private static IEnumerable<Node> Descendants(Node root)
    {
        foreach(Node child in root.GetChildren())
        {yield return child;foreach(Node nested in Descendants(child))yield return nested;}
    }

    private static async Task<bool> WaitSignal(GodotObject source, StringName signal)
    {
        if (_cancel?.IsCancellationRequested != false) return false;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callback = Callable.From((Action)(() => { completion.TrySetResult(true); }));
        source.Connect(signal, callback);
        using var registration = _cancel.Token.Register(() => completion.TrySetResult(false));
        try
        {
            var completed = await Task.WhenAny(completion.Task, Task.Delay(2500));
            if (completed != completion.Task) throw new TimeoutException("Native capture signal did not arrive: " + signal);
            return await completion.Task;
        }
        finally
        {
            if (GodotObject.IsInstanceValid(source) && source.IsConnected(signal, callback)) source.Disconnect(signal, callback);
        }
    }

    internal static async Task Stop()
    {
        if (!Enabled) return;
        _cancel?.Cancel();
        await Task.WhenAll(Pending);
        WriteManifest();
    }

    internal static async Task Drain()
    {
        if (!Enabled) return;
        await Task.WhenAll(Pending);
        WriteManifest();
        if (Errors.Count > 0) throw new InvalidOperationException("Requested native capture failed: " + string.Join("; ", Errors));
    }

    private static void WriteManifest()
    {
        File.WriteAllText(Path.Combine(_directory, "manifest.json"), JsonSerializer.Serialize(new
        {
            enabled_explicitly = true, saved = Saved, maximum_per_scene = 3,
            intended_use = "Review spell body/direction/contact/native overlap and setting degradation; retain selected evidence after review.",
            performance_caveat = "This optional evidence pass includes viewport readback and PNG encoding; frame telemetry is not performance acceptance.",
            evidence = Evidence, errors = Errors
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
