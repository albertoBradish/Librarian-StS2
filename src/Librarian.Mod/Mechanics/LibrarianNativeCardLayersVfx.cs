using Godot;

namespace Librarian.Mechanics;

internal enum LibrarianRemasterMotion { PaperBlade, Needle, Seal, Firebolt, WaterSurge, GroundRoots, SteamFront, ChargedRay }
internal sealed record LibrarianRemasterRecipe(LibrarianRemasterMotion Motion, float Normal, float Fast)
{
    internal static LibrarianRemasterRecipe For(string name, SpellProfile050 profile) => name switch
    {
        "Rootbind" or "SeedburialStrike" or "Bookworm" or "BurnRoots" => new(LibrarianRemasterMotion.GroundRoots, .30f, .15f),
        "TidalErosion" => new(LibrarianRemasterMotion.WaterSurge, .28f, .14f),
        "SteamBlast" => new(LibrarianRemasterMotion.SteamFront, .26f, .13f),
        "NeedleFlurry" or "GapNeedle" => new(LibrarianRemasterMotion.Needle, .13f, .065f),
        "WearyIncantation" or "ScatteredFlames" => new(LibrarianRemasterMotion.Seal, .19f, .095f),
        "TreeRingBurst" => new(LibrarianRemasterMotion.ChargedRay, .32f, .16f),
        _ when profile.Element == SpellElement050.Fire => new(LibrarianRemasterMotion.Firebolt, .23f, .115f),
        _ => new(LibrarianRemasterMotion.PaperBlade, .17f, .085f)
    };
}

/// <summary>
/// Runtime references to script-free native particle layers present in both fixed game PCKs.
/// No upstream files are bundled and no cached Material/ProcessMaterial is modified.
/// Native composite scripts (including their shake/timers) are deliberately not executed:
/// named particle children are detached before the temporary instance ever enters the tree.
/// </summary>
public partial class LibrarianNativeCardLayersVfx : Node2D
{
    private sealed record Layer(string Scene, string? Child, float Scale, Color Tint, bool Core = false);
    private static readonly Dictionary<string, PackedScene> Scenes = new(StringComparer.Ordinal);
    private readonly List<GpuParticles2D> _particles = [];
    private float _age;
    private int _capacity;
    internal float Lifetime { get; private set; } = .8f;
    internal static int Emitters { get; private set; }
    internal static int Capacity { get; private set; }
    internal static int PeakEmitters { get; private set; }
    internal static int PeakCapacity { get; private set; }
    internal bool HasCore { get; private set; }

    private const string Vfx = "res://scenes/vfx/";
    internal static LibrarianNativeCardLayersVfx? Create(LibrarianRemasterMotion motion, Vector2 targetSize, bool flight = false)
    {
        var prefs = LibrarianPreferences050.Current;
        if (!prefs.Particles || prefs.ReducedMotion) return null;
        Color water = new("85d7e5");
        Layer[] layers = flight
            ? motion == LibrarianRemasterMotion.SteamFront
                ? [new("common/vfx_common_round_smoke_flipbook.tscn",null,.18f,new("b9e0df"),true)] : []
            : motion switch
            {
                LibrarianRemasterMotion.Firebolt => [
                    new("fire_impact/vfx_fire_burst_center_flipbook.tscn",null,.48f,Colors.White,true),
                    new("fire_impact/vfx_fire_burst_left_flipbook.tscn",null,.40f,Colors.White),
                    new("fire_impact/vfx_fire_burst_right_flipbook.tscn",null,.40f,Colors.White),
                    new("common/vfx_common_round_smoke_flipbook.tscn",null,.22f,new("9c7765"))],
                LibrarianRemasterMotion.WaterSurge => [
                    new("liquid/vfx_liquid_splash.tscn",null,.20f,new Color(water,.60f),true),
                    new("liquid/vfx_liquid_droplets.tscn",null,.20f,water)],
                LibrarianRemasterMotion.SteamFront => [
                    new("common/vfx_common_round_smoke_flipbook.tscn",null,.42f,new("c9e9e5"),true),
                    new("common/vfx_horizontal_smoke.tscn",null,.32f,new("9fcbd3"))],
                LibrarianRemasterMotion.GroundRoots => [
                    new("vfx_rock_shatter.tscn","smoke",.28f,new("9e9e73"))],
                LibrarianRemasterMotion.Needle => [
                    new("dagger/vfx_dagger_impact_core.tscn",null,.40f,new("e7deb8"),true)],
                LibrarianRemasterMotion.PaperBlade => [
                    new("slash/vfx_slash_core.tscn",null,.20f,new("e5ddba"),true),
                    new("big_slash/vfx_big_slash_impact_side_smoke.tscn",null,.20f,new("b9b6a4"))],
                LibrarianRemasterMotion.ChargedRay => [
                    new("missile/vfx_missile_impact_core.tscn",null,.35f,new("d5c6eb"),true),
                    new("missile/vfx_missile_impact_smoke.tscn",null,.25f,new("c3b2d5"))],
                _ => []
            };
        Vector2? bound = !flight && motion is LibrarianRemasterMotion.ChargedRay or LibrarianRemasterMotion.PaperBlade
            or LibrarianRemasterMotion.WaterSurge
            ? new Vector2(Math.Clamp(targetSize.X*.85f,30,150),Math.Clamp(targetSize.Y*.70f,28,135)) : null;
        return Build(layers,bound);
    }

    internal static LibrarianNativeCardLayersVfx? Rock()
        => LibrarianPreferences050.Current.Particles && !LibrarianPreferences050.Current.ReducedMotion
            ? Build([new("vfx_rock_shatter.tscn","rock",.42f,Colors.White,true),
                new("vfx_rock_shatter.tscn","smoke",.35f,Colors.White)]) : null;

    private static LibrarianNativeCardLayersVfx? Build(Layer[] layers, Vector2? targetBound = null)
    {
        if (layers.Length == 0) return null;
        var result = new LibrarianNativeCardLayersVfx
        {
            Name = "LibrarianNativeCardLayers",
            Modulate = new(1,1,1,LibrarianPreferences050.Current.EffectOpacity/100f)
        };
        foreach (var layer in layers)
        {
            string path = Vfx + layer.Scene;
            if (!Scenes.TryGetValue(path, out var scene))
            {
                scene = ResourceLoader.Load<PackedScene>(path);
                if (scene is null) continue;
                Scenes.Add(path, scene);
            }
            var instance = scene.Instantiate<Node2D>();
            var particle = layer.Child is null ? instance as GpuParticles2D : instance.GetNodeOrNull<GpuParticles2D>(layer.Child);
            if (particle is null || Emitters + result._particles.Count >= 64 || Capacity + result._capacity + particle.Amount > 256)
            { instance.Free(); continue; }
            if (particle != instance)
            {
                particle.GetParent().RemoveChild(particle);
                instance.Free(); // Never entered tree: native composite _Ready cannot run.
            }
            else particle.Position = Vector2.Zero; // A naked scene root is an emitter, not an authored composition offset.
            particle.Emitting = false;
            // These emitters now live under a positioned/scaled contact wrapper. World-space
            // particles retain process-space size independently of that wrapper (preview02's
            // node bounds shrank while the missile image did not). Emit in this owned local
            // frame so both particle simulation and rendered geometry use the bounded transform.
            particle.SetMeta("librarian_source_local_coords",particle.LocalCoords);
            particle.LocalCoords = true;
            particle.OneShot = true;
            particle.Lifetime = Math.Min(particle.Lifetime, .70);
            particle.SpeedScale = MegaCrit.Sts2.Core.Saves.SaveManager.Instance.PrefsSave.FastMode
                == MegaCrit.Sts2.Core.Settings.FastModeType.Fast ? 1.35f : 1;
            particle.Scale *= layer.Scale;
            if (targetBound is { } budget && layer.Core && particle.Texture is { } texture
                && particle.ProcessMaterial is ParticleProcessMaterial process)
            {
                Vector2 curve = process.ScaleCurve is CurveXyzTexture xyz
                    ? new(Math.Max(.01f,xyz.CurveX?.MaxValue ?? 1),Math.Max(.01f,xyz.CurveY?.MaxValue ?? 1))
                    : process.ScaleCurve is CurveTexture one ? Vector2.One*Math.Max(.01f,one.Curve?.MaxValue ?? 1) : Vector2.One;
                Vector2 envelope = texture.GetSize()*particle.Scale.Abs()*Math.Max(.01f,process.ScaleMax)*curve;
                float fit = Math.Min(1,Math.Min(budget.X/Math.Max(1,envelope.X),budget.Y/Math.Max(1,envelope.Y)));
                particle.Scale *= fit;
            }
            particle.Modulate = layer.Tint;
            result.AddChild(particle);
            result._particles.Add(particle);
            result._capacity += particle.Amount;
            result.HasCore |= layer.Core;
            result.Lifetime = Math.Max(result.Lifetime, (float)(particle.Lifetime / particle.SpeedScale + .05));
        }
        if (result._particles.Count == 0) { result.Free(); return null; }
        return result;
    }

    public override void _EnterTree()
    {
        Emitters += _particles.Count; Capacity += _capacity;
        PeakEmitters = Math.Max(PeakEmitters, Emitters); PeakCapacity = Math.Max(PeakCapacity, Capacity);
    }
    public override void _Ready()
    {
        // Explicit recursive-independent restart is the same on 0.107.1 and 0.111.0.
        foreach (var particle in _particles) particle.Restart();
    }
    public override void _ExitTree()
    { Emitters = Math.Max(0, Emitters - _particles.Count); Capacity = Math.Max(0, Capacity - _capacity); }
    public override void _Process(double delta)
    {
        _age += (float)delta;
        var prefs = LibrarianPreferences050.Current;
        if (_age >= Lifetime || !prefs.CardEffects || !prefs.Particles || prefs.ReducedMotion)
        { QueueFree(); return; }
        Modulate = new(1,1,1,prefs.EffectOpacity/100f);
    }
}
