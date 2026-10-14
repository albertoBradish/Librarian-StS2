using Godot;
using MegaCrit.Sts2.Core.Entities.Players;

namespace Librarian.Mechanics;

/// <summary>
/// One bounded, procedural fire projectile and its impact. Its trajectory is a
/// presentation snapshot; no target lookup, gameplay RNG, damage or audio occurs here.
/// </summary>
public partial class LibrarianFireballVfx : Node2D
{
    private const int TailSegments = 14;
    private const float ImpactLifetime = .32f;
    private const float MinimumFlameRadius = 1.5f;
    private const float MinimumDrawOpacity = 1f / 128;
    private readonly Vector2[] _contour = new Vector2[36];
    private readonly Vector2[] _trail = new Vector2[TailSegments * 2];
    private readonly Vector2[] _tongue = new Vector2[5];
    private Vector2 _destination;
    private float _progress;
    private float _timeline;
    private float _age;
    private float _impactAge;
    private bool _impacted;
    internal Player? EffectOwner { get; init; }
    internal float TravelDuration { get; init; } = .3f;
    internal float ImpactRadius { get; init; } = 38;
    internal int SettlementValue { get; init; } = 10;
    internal bool FinishImpactAfterCombat { get; init; }
    // A square-root curve keeps small settlements readable and large values bounded.
    internal float SizeFactor => SizeForValue(SettlementValue);
    internal static float SizeForValue(int value)
        => Math.Clamp(.6f + .4f * MathF.Sqrt(Math.Max(0, value) / 10f), .6f, 2.4f);
    // Read-only observations for isolated native probes; these never affect combat.
    internal float TravelProgress => _progress;
    internal bool HasImpacted => _impacted;
    internal float ImpactProgress => Math.Clamp(_impactAge / ImpactLifetime, 0, 1);
    internal Vector2 Destination => _destination;
    internal Vector2 FlightPoint => Path(_progress);
    internal bool TrailVisible => LibrarianPreferences050.Current.Particles && !LibrarianPreferences050.Current.ReducedMotion;
    internal int VisibleEmberCount => TrailVisible ? (_impacted ? 9 : 5) : 0;

    internal void CaptureDestination(Vector2 globalPoint) => _destination = ToLocal(globalPoint);
    internal void Advance(float progress)
    {
        _timeline = Math.Clamp(progress, 0, 1);
        _progress = LibrarianPreferences050.Current.ReducedMotion ? _timeline
            : MathF.Pow(Math.Clamp((_timeline - .18f) / .82f, 0, 1), 2.1f);
        QueueRedraw();
    }
    internal void Impact()
    {
        Name = "LibrarianFireImpact";
        _progress = 1;
        _impacted = true;
        _impactAge = 0;
        QueueRedraw();
    }
    internal Vector2 Path(float progress)
        => _destination * progress + new Vector2(0, -28 * Mathf.Sin(Mathf.Pi * progress));
    private Vector2 Tangent(float progress)
    {
        var tangent = _destination + new Vector2(0, -28 * Mathf.Pi * Mathf.Cos(Mathf.Pi * progress));
        return tangent.LengthSquared() > .01f ? tangent.Normalized() : Vector2.Right;
    }
    public override void _EnterTree() => LibrarianCardVfx050.Enter();
    public override void _ExitTree() => LibrarianCardVfx050.Exit();
    public override void _Process(double delta)
    {
        _age += (float)delta;
        if (_impacted) _impactAge += (float)delta;
        var prefs = LibrarianPreferences050.Current;
        bool finishingKill = FinishImpactAfterCombat && _impacted && prefs.OrbEffects
            && MegaCrit.Sts2.Core.Saves.SaveManager.Instance.PrefsSave.FastMode != MegaCrit.Sts2.Core.Settings.FastModeType.Instant
            && (prefs.TeammateEffects || EffectOwner is null || MegaCrit.Sts2.Core.Context.LocalContext.IsMe(EffectOwner));
        if ((!finishingKill && !LibrarianCardVfx050.Available(EffectOwner, true))
            || (_impacted ? _impactAge >= ImpactLifetime : _age >= Math.Max(.8f, TravelDuration * 4)))
        {
            QueueFree();
            return;
        }
        Modulate = new Color(1, 1, 1, LibrarianPreferences050.Current.EffectOpacity / 100f);
        QueueRedraw();
    }
    public override void _Draw()
    {
        if (_impacted) DrawImpact();
        else DrawFlight();
    }

    private void DrawFlight()
    {
        bool reduced = LibrarianPreferences050.Current.ReducedMotion;
        var head = reduced ? Vector2.Zero : FlightPoint;
        var direction = reduced ? Vector2.Right : Tangent(_progress);
        float charge = Math.Clamp(_timeline / .18f, 0, 1);
        float envelope = Math.Min(1, .45f + charge * .55f + _progress * 5);
        if (reduced) envelope *= 1 - _progress * .45f;
        float radius = (reduced ? 12 : (8.5f + 5 * charge) + 1.5f * Mathf.Sin(_progress * Mathf.Pi)) * SizeFactor;
        if (TrailVisible) DrawTrail(1);
        DrawCircle(head, radius * 1.65f, new Color(1, .28f, .06f, envelope * .09f));
        DrawFlame(head, direction, radius, envelope, reduced ? 0 : _age * 32);
        if (!TrailVisible) return;
        var normal = new Vector2(-direction.Y, direction.X);
        for (int i = 0; i < 5; i++)
        {
            float behind = (i + 1) / 6f;
            float sample = Math.Max(0, _progress - TailWindow() * behind);
            float flicker = .5f + .5f * Mathf.Sin(_age * 23 + i * 2.1f);
            var point = Path(sample) + normal * (i % 2 == 0 ? 1 : -1) * (6 + behind * 8) * SizeFactor;
            DrawCircle(point, (1.2f + flicker) * SizeFactor, new Color(1, .58f, .13f, (1 - behind) * .65f));
        }
    }

    private float TailWindow() => Math.Min(.32f, 90 / Math.Max(90, _destination.Length()));
    private void DrawTrail(float opacity)
    {
        if (opacity <= MinimumDrawOpacity) return;
        float window = Math.Min(_progress, TailWindow());
        if (window < .001f) return;
        // A single tapered ribbon follows the captured curve, rather than a row of dots.
        for (int i = 0; i < TailSegments; i++)
        {
            float along = i / (TailSegments - 1f);
            float sample = _progress - window * (1 - along);
            var direction = Tangent(sample);
            var normal = new Vector2(-direction.Y, direction.X);
            float width = (1 + along * along * 9) * SizeFactor;
            float distortion = Mathf.Sin(_age * 26 - along * 12) * (1 - along) * 2.3f;
            var center = Path(sample) + normal * distortion;
            _trail[i] = center + normal * width;
            _trail[_trail.Length - 1 - i] = center - normal * width;
        }
        DrawColoredPolygon(_trail, new Color(.9f, .19f, .035f, opacity * .48f));
        for (int i = 0; i < _trail.Length; i++)
        {
            int mirrored = _trail.Length - 1 - i;
            if (i >= mirrored) break;
            var center = (_trail[i] + _trail[mirrored]) * .5f;
            _trail[i] = center + (_trail[i] - center) * .44f;
            _trail[mirrored] = center + (_trail[mirrored] - center) * .44f;
        }
        DrawColoredPolygon(_trail, new Color(1, .62f, .12f, opacity * .76f));
    }

    private void DrawFlame(Vector2 center, Vector2 direction, float radius, float opacity, float phase)
    {
        // Impact lobes shrink to zero. Even a transparent polygon is triangulated;
        // subpixel rings translated far from the origin can collapse adjacent float
        // vertices. Stop before the smallest (half-radius) contour becomes degenerate.
        if (!float.IsFinite(radius) || radius < MinimumFlameRadius
            || !float.IsFinite(opacity) || opacity <= MinimumDrawOpacity) return;
        var normal = new Vector2(-direction.Y, direction.X);
        FillContour(center, direction, radius, 1.08f, phase);
        DrawColoredPolygon(_contour, new Color(.9f, .19f, .025f, opacity));
        // Unequal flame tongues deform independently; the bright core stays readable.
        for (int i = 0; i < 3; i++)
        {
            float side = i - 1;
            var root = center - direction * radius * .25f + normal * side * radius * .48f;
            float length = radius * (1.5f + .22f * Mathf.Sin(phase * .65f + i * 2));
            _tongue[0] = root + normal * radius * .38f;
            _tongue[1] = root - direction * length * .5f + normal * radius * .26f;
            _tongue[2] = root - direction * length + normal * Mathf.Sin(phase + i * 2) * radius * .24f;
            _tongue[3] = root - direction * length * .55f - normal * radius * .22f;
            _tongue[4] = root - normal * radius * .38f;
            DrawColoredPolygon(_tongue, new Color(1, .4f + i * .06f, .055f, opacity * .92f));
        }
        FillContour(center + direction * radius * .1f, direction, radius * .8f, 1.02f, phase + 1);
        DrawColoredPolygon(_contour, new Color(1, .58f, .08f, opacity));
        FillContour(center + direction * radius * .25f, direction, radius * .5f, 1.08f, phase * .8f);
        DrawColoredPolygon(_contour, new Color(1, .91f, .53f, opacity));
        DrawCircle(center + direction * radius * .35f, radius * .22f, new Color(1, .99f, .83f, opacity));
    }
    private void FillContour(Vector2 center, Vector2 direction, float radius, float stretch, float phase)
    {
        var normal = new Vector2(-direction.Y, direction.X);
        for (int i = 0; i < _contour.Length; i++)
        {
            float angle = i * Mathf.Tau / _contour.Length;
            float warp = 1 + .075f * Mathf.Sin(angle * 3 + phase) + .045f * Mathf.Sin(angle * 5 - phase * .7f);
            _contour[i] = center + direction * (Mathf.Cos(angle) * radius * stretch * warp)
                + normal * (Mathf.Sin(angle) * radius * warp);
        }
    }

    private void DrawImpact()
    {
        float t = ImpactProgress;
        bool reduced = LibrarianPreferences050.Current.ReducedMotion;
        float fade = (1 - t) * (1 - t);
        if (fade <= MinimumDrawOpacity) return;
        var direction = Tangent(1);
        var normal = new Vector2(-direction.Y, direction.X);
        var hit = _destination;
        if (reduced)
        {
            // Local fade only: no travel, expanding ring, shake, flicker or detached particles.
            DrawFlame(hit, Vector2.Up, 16 * SizeFactor, fade, 0);
            return;
        }
        if (TrailVisible && t < .42f) DrawTrail((1 - t / .42f) * .6f);
        float radius = (12 + ImpactRadius * (1 - Mathf.Pow(1 - t, 3))) * SizeFactor;
        DrawCircle(hit, radius * 1.12f, new Color(1, .32f, .05f, fade * .11f));
        // Flatten the impact along the incoming direction, then lift three torn flames.
        for (int i = 0; i < 7; i++)
        {
            float angle = -.85f * Mathf.Pi + i * 1.7f * Mathf.Pi / 6;
            var ray = direction * Mathf.Cos(angle) + normal * Mathf.Sin(angle);
            var center = hit + ray * radius * (.2f + i % 3 * .14f) + new Vector2(0, -t * 12);
            float lobe = (1 - t) * (9 + i % 3 * 3) * SizeFactor;
            DrawFlame(center, ray, lobe, fade * .84f, _age * 21 + i);
        }
        DrawCircle(hit, 10 * (1 - t) * SizeFactor, new Color(1, .96f, .72f, fade));
        if (!TrailVisible) return;
        for (int i = 0; i < 9; i++)
        {
            float angle = i * Mathf.Tau / 9 + .18f;
            var ray = Vector2.FromAngle(angle);
            float distance = (10 + t * ImpactRadius * (1.2f + i % 3 * .13f)) * SizeFactor;
            var point = hit + ray * distance + new Vector2(0, t * t * 26);
            DrawLine(point, point - ray * (3 + (1 - t) * 5), new Color(1, .62f, .18f, fade), 1.7f, true);
        }
    }
}
