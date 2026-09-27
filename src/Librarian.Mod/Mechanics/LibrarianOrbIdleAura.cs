using Godot;
using Librarian.Core;

namespace Librarian.Mechanics;

/// <summary>Independent elemental silhouettes and particles around a stationary core. No gameplay RNG.</summary>
public partial class LibrarianOrbIdleAura : Node2D
{
    internal OrbKind Kind { get; init; }
    internal bool Front { get; init; }
    private float _phase;
    private float _strength;
    internal void SetAppearance(float phase, bool active, bool sessionLocked)
    {
        _phase = phase;
        _strength = sessionLocked ? 0.12f : active ? 0.9f : 0.28f;
        QueueRedraw();
    }
    public override void _Draw()
    {
        if (Kind == OrbKind.Fire) Fire();
        else if (Kind == OrbKind.Tide) Tide();
        else Growth();
    }
    private Color Ink(string hex, float alpha = 1) => new Color(hex, alpha * _strength);
    private void Fire()
    {
        if (!Front)
        {
            // Tongues bend and change length independently, outside the numeric core.
            for (int i = 0; i < 7; i++)
            {
                float x = (i - 3) * 7;
                float y = -Mathf.Sqrt(Math.Max(0, 27 * 27 - x * x)) + 4;
                float phase = _phase * 3.8f + i * 1.7f;
                float length = 11 + 6 * (1 + Mathf.Sin(phase));
                var b = new Vector2(x, y);
                var tip = b + new Vector2(Mathf.Sin(phase * .73f) * 7, -length);
                DrawColoredPolygon([b + new Vector2(-6, 4), b + new Vector2(-7, -length * .35f), tip,
                    b + new Vector2(5, -length * .4f), b + new Vector2(6, 4)], Ink("ef6f2d", .75f));
                DrawColoredPolygon([b + new Vector2(-3, 2), b.Lerp(tip, .68f), b + new Vector2(3, 2)], Ink("ffd887", .9f));
            }
        }
        else if (LibrarianPreferences050.Current.Particles)
            for (int i = 0; i < 9; i++)
            {
                float t = Mathf.PosMod(_phase * .5f + i * .137f, 1);
                var p = new Vector2((i % 5 - 2) * 11 + Mathf.Sin(t * 6 + i) * 4, -20 - 36 * t);
                DrawLine(p, p + new Vector2(1, 3), Ink(i % 2 == 0 ? "ffd887" : "ff8a44", Mathf.Sin(t * Mathf.Pi)), 1.6f, true);
            }
    }
    private void Tide()
    {
        // Back/front water ribbons create depth without spinning the central artwork.
        for (int band = 0; band < 3; band++)
        {
            var points = new Vector2[25];
            for (int i = 0; i < points.Length; i++)
            {
                float a = i / 24f * Mathf.Pi + (Front ? 0 : Mathf.Pi);
                float ripple = Mathf.Sin(a * 4 - _phase * 3 + band) * 1.8f;
                points[i] = new Vector2(Mathf.Cos(a) * (29 + band * 2 + ripple), Mathf.Sin(a) * (23 + band * 2 + ripple));
            }
            DrawPolyline(points, Ink(band == 1 ? "d0f8ff" : "5bbbdc", band == 1 ? .7f : .35f), band == 1 ? 1.4f : 2.8f, true);
        }
        if (Front && LibrarianPreferences050.Current.Particles)
            for (int i = 0; i < 5; i++)
            {
                float a = _phase * 1.2f + i * Mathf.Tau / 5;
                DrawCircle(new Vector2(Mathf.Cos(a) * 35, Mathf.Sin(a) * 29), 1.6f, Ink("b1efff", .75f));
            }
    }
    private void Growth()
    {
        for (int side = -1; side <= 1; side += 2)
        {
            if (!Front)
            {
                var stem = new Vector2[20];
                for (int i = 0; i < stem.Length; i++)
                {
                    float t = i / 19f;
                    stem[i] = new Vector2(side * (19 + Mathf.Sin(t * Mathf.Pi) * 13 + Mathf.Sin(_phase + t * 3) * 1.5f), 26 - t * 54);
                }
                DrawPolyline(stem, Ink("719b52", .75f), 2, true);
                for (int i = 4; i < 18; i += 5)
                {
                    var p = stem[i];
                    float sway = Mathf.Sin(_phase * 1.6f + i) * 3;
                    var tip = p + new Vector2(side * (9 + sway), -8);
                    DrawColoredPolygon([p, p + new Vector2(side * 2, -8), tip, p + new Vector2(side * 8, 1)], Ink("a8cf77", .9f));
                    DrawLine(p, tip, Ink("e0eeb1", .55f), 1, true);
                }
            }
            else if (LibrarianPreferences050.Current.Particles)
                for (int i = 0; i < 3; i++)
                {
                    float t = Mathf.PosMod(_phase * .2f + i * .31f, 1);
                    var p = new Vector2(side * (29 + Mathf.Sin(t * 5 + i) * 7), 20 - t * 57);
                    DrawCircle(p, 1.3f, Ink("d4ed99", Mathf.Sin(t * Mathf.Pi) * .7f));
                }
        }
    }
}
