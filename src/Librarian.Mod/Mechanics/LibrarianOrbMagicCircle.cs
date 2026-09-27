using Godot;

namespace Librarian.Mechanics;

/// <summary>Purely decorative geometry behind the three existing orb slots.</summary>
public partial class LibrarianOrbMagicCircle : Node2D
{
    // Circumcenter of the preserved slot centers: (-96,-68), (96,-68), (0,36).
    internal static readonly Vector2 RingCenter = new(0, -60.30769f);
    internal const float OuterRadius = 138f;
    internal const float InnerRadius = 130f;
    internal static readonly Vector2 NeedleTip = new(0, -7);
    internal float Radius { get; private set; } = 138;
    internal bool IndicatorOnly { get; init; }
    internal Vector2 UpperNeedleTip => new(0, -Radius + 35);
    internal Vector2 VisibleNeedleBase => new(0, -Radius + 57);
    internal Vector2 TopMarkerTip => new(0, -Radius - 44);
    internal float RingAngle { get; private set; }
    internal float EntranceProgress { get; private set; }
    internal float GlowStrength { get; private set; }
    private float _entranceElapsed;
    private float _glowHold;
    private float _castRemaining;
    internal bool OverlimitActive { get; set; }
    internal float OverlimitStrength { get; private set; }
    internal float OverlimitBurst { get; private set; }
    private float _formTime;
    internal void BurstOverlimit() { if (OverlimitActive) OverlimitBurst = 1.2f; }
    internal void CastRevolution() { _castRemaining = 0.6f; _glowHold = 0.5f; }
    public override void _Ready()
    {
        if (IndicatorOnly) return;
        Scale = Vector2.One * 0.16f;
        Modulate = new Color(1, 1, 1, 0);
    }
    internal void PulseGold() => _glowHold = 0.10f;
    internal void Advance(double delta)
    {
        float dt = (float)delta;
        OverlimitStrength = Mathf.MoveToward(OverlimitStrength, OverlimitActive ? 1 : 0, dt * 2.5f);
        if (!OverlimitActive) OverlimitBurst = 0;
        else OverlimitBurst = Math.Max(0, OverlimitBurst - dt);
        if (!LibrarianPreferences050.Current.ReducedMotion) _formTime += dt;
        if (LibrarianPreferences050.Current.ReducedMotion)
        {
            _entranceElapsed = 0.85f; EntranceProgress = 1; Scale = Vector2.One; Modulate = Colors.White; GlowStrength = 0; _castRemaining = 0; QueueRedraw(); return;
        }
        float previous = EntranceProgress;
        _entranceElapsed = Math.Min(0.85f, _entranceElapsed + (float)delta);
        float time = _entranceElapsed / 0.85f;
        EntranceProgress = 1 - Mathf.Pow(1 - time, 3);
        RingAngle -= dt * Mathf.Tau * Mathf.Lerp(1f / 90, 1f / 12, OverlimitStrength)
            + dt * OverlimitBurst * 1.5f + (EntranceProgress - previous) * 1.10f;
        if (_castRemaining > 0)
        {
            float step = Math.Min((float)delta, _castRemaining);
            RingAngle -= step / 0.6f * Mathf.Tau;
            _castRemaining -= step;
        }
        Scale = Vector2.One * (0.16f + EntranceProgress * 0.84f);
        Modulate = new Color(1, 1, 1, EntranceProgress);
        if (_glowHold > 0)
        {
            _glowHold = Math.Max(0, _glowHold - (float)delta);
            GlowStrength = Mathf.MoveToward(GlowStrength, 1, (float)delta * 14);
        }
        else GlowStrength = Mathf.MoveToward(GlowStrength, 0, (float)delta / 0.65f);
        QueueRedraw();
    }
    internal void Configure(float radius)
    {
        if (Mathf.IsEqualApprox(Radius, radius)) return;
        Radius = radius;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var shadow = new Color("18232a") { A = 0.75f };
        if (IndicatorOnly)
        {
            // Only the 22px tip rises in front of the crown. The long clock-hand
            // base remains behind the body, keeping the face unobstructed.
            Vector2[] tip = [VisibleNeedleBase + new Vector2(-3.5f, 0),
                VisibleNeedleBase + new Vector2(3.5f, 0), UpperNeedleTip];
            DrawColoredPolygon(tip, new Color("EBA466"));
            DrawPolyline([tip[0], tip[1], tip[2], tip[0]], shadow, 0.8f, true);
            Vector2[] marker = [TopMarkerTip + new Vector2(-9, -7), TopMarkerTip + new Vector2(9, -7), TopMarkerTip];
            DrawColoredPolygon(marker, new Color("EBA466"));
            DrawPolyline([marker[0], marker[1], marker[2], marker[0]], shadow, 1f, true);
            return;
        }
        var gold = new Color("EBA466") { A = 0.70f };
        var faintGold = new Color("EBA466") { A = 0.30f };
        var center = Vector2.Zero;
        float inner = Radius - 8;
        DrawSetTransform(Vector2.Zero, RingAngle);
        if (OverlimitStrength > 0)
        {
            var prefs = LibrarianPreferences050.Current;
            float strength = OverlimitStrength * prefs.EffectOpacity / 100f;
            float burst = prefs.ReducedMotion ? 0 : OverlimitBurst / 1.2f;
            float breathe = prefs.ReducedMotion ? 1 : 0.88f + 0.12f * Mathf.Sin(_formTime * 2);
            var warm = new Color("ffcf64");
            DrawArc(center, Radius + 3, 0, Mathf.Tau, 160, new Color(warm, strength * (0.11f + burst * 0.10f) * breathe), 22, true);
            DrawArc(center, Radius + 3, 0, Mathf.Tau, 160, new Color(warm, strength * 0.32f), 6, true);
            for (int i = 0; i < 6; i++)
            {
                float angle = i * Mathf.Tau / 6;
                DrawArc(center, Radius + 11, angle, angle + 0.55f, 20, new Color("ffe8ac") { A = strength * 0.8f }, 2.2f, true);
            }
            if (burst > 0)
                DrawArc(center, Radius + 10 + (1 - burst) * 35, 0, Mathf.Tau, 128, new Color("ffe8ac") { A = burst * strength * 0.7f }, 3, true);
            if (prefs.Particles)
            {
                int count = prefs.ReducedMotion ? 8 : 24;
                for (int i = 0; i < count; i++)
                {
                    float life = Mathf.PosMod(_formTime * 0.45f + i * 0.618034f, 1);
                    float angle = i * 2.399963f - RingAngle * 0.35f;
                    var unit = Vector2.FromAngle(angle);
                    var pos = unit * (Radius + 5 + life * (24 + burst * 30));
                    float alpha = Mathf.Sin(life * Mathf.Pi) * strength;
                    DrawCircle(pos, 4, new Color(warm, alpha * 0.12f));
                    DrawCircle(pos, i % 3 == 0 ? 2 : 1.2f, new Color("fff1bd") { A = alpha * 0.85f });
                    if (!prefs.ReducedMotion && i % 3 == 0)
                        DrawLine(pos - unit * (4 + burst * 8), pos, new Color(warm, alpha * 0.55f), 1.3f, true);
                }
            }
            gold = gold.Lerp(new Color("ffe8ac"), strength * 0.8f);
        }
        if (GlowStrength > 0)
        {
            DrawArc(center, Radius, 0, Mathf.Tau, 160, new Color("ffd98c") { A = GlowStrength * 0.11f }, 14f, true);
            DrawArc(center, Radius, 0, Mathf.Tau, 160, new Color("ffd98c") { A = GlowStrength * 0.36f }, 5f, true);
            DrawArc(center, inner, 0, Mathf.Tau, 160, new Color("ffd98c") { A = GlowStrength * 0.25f }, 4f, true);
            gold = gold.Lerp(new Color("ffe3a4"), GlowStrength * 0.75f);
        }
        DrawArc(center, Radius, 0, Mathf.Tau, 160, shadow, 4f, true);
        DrawArc(center, Radius, 0, Mathf.Tau, 160, gold, 1.5f, true);
        DrawArc(center, inner, 0, Mathf.Tau, 160, faintGold, 1f, true);
        // Twenty-four clock-like marks, deliberately without invented readable glyphs.
        for (int i = 0; i < 24; i++)
        {
            float angle = i * Mathf.Tau / 24f;
            var unit = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            float length = i % 6 == 0 ? 9f : i % 2 == 0 ? 5f : 3f;
            DrawLine(center + unit * (inner - length), center + unit * inner,
                i % 6 == 0 ? gold : faintGold, i % 6 == 0 ? 1.7f : 1f, true);
        }
        for (int i = 0; i < (LibrarianPreferences050.Current.Particles ? 6 : 0); i++)
        {
            float phase = i * Mathf.Tau / 6 + RingAngle * 0.45f;
            float distance = Radius + 7 + 5 * Mathf.Sin(RingAngle * 2 + i * 1.7f);
            var mote = new Vector2(Mathf.Cos(phase), Mathf.Sin(phase)) * distance;
            var tint = new Color("EBA466") { A = 0.16f + 0.12f * (1 + Mathf.Sin(RingAngle * 3 + i)) / 2 };
            DrawCircle(mote, i % 2 == 0 ? 1.6f : 1.1f, tint);
        }
        DrawSetTransform(Vector2.Zero);
        DrawArc(center, 11f, 0, Mathf.Tau, 48, faintGold, 1f, true);
        // Upward hand marks the foreground above the head; the body occludes its base.
        Vector2[] needle = [new(-4.5f, 6), new(4.5f, 6), UpperNeedleTip];
        DrawColoredPolygon(needle, new Color("EBA466") { A = 0.82f });
        DrawPolyline([needle[0], needle[1], needle[2], needle[0]], shadow, 1.5f, true);
        DrawCircle(center, 3.5f, new Color("f6d4a4"));
        DrawArc(center, 4.5f, 0, Mathf.Tau, 24, shadow, 1.5f, true);
    }
}
