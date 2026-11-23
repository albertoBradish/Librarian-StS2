using System.Text;
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
    private const float TextureExtent = 320f;
    private static readonly Color RingGold = new("eba466");
    private static readonly Color Shadow = new("18232a") { A = .75f };
    private static readonly Color FormGold = new("ffe8ac");
    private static RingArtwork? _artwork;
    private Node2D _ring = null!, _motes = null!, _hand = null!;
    private Sprite2D _breathingHalo = null!, _halo = null!, _burst = null!, _pulse = null!, _accent = null!;
    private float _moteRefresh;
    private (bool Reduced, float Strength, float Burst) _moteState;

    private sealed record RingArtwork(Texture2D Ring, Texture2D Accent, Texture2D BreathingHalo,
        Texture2D Halo, Texture2D Burst, Texture2D Pulse, Texture2D Dot, Texture2D FormMote);

    internal void BurstOverlimit() { if (OverlimitActive) OverlimitBurst = 1.2f; }
    internal void CastRevolution() { _castRemaining = 0.6f; _glowHold = 0.5f; }
    public override void _Ready()
    {
        if (IndicatorOnly) return;
        _artwork ??= CreateArtwork();
        _ring = new Node2D { Scale = Vector2.One * (Radius / OuterRadius) };
        AddChild(_ring);
        _breathingHalo = AddLayer(_artwork.BreathingHalo, visible: false);
        _halo = AddLayer(_artwork.Halo, visible: false);
        _burst = AddLayer(_artwork.Burst, visible: false);
        _pulse = AddLayer(_artwork.Pulse, visible: false);
        AddLayer(_artwork.Ring);
        _accent = AddLayer(_artwork.Accent);
        _motes = new Node2D { TextureFilter = TextureFilterEnum.Linear };
        _motes.Draw += DrawMotes;
        _ring.AddChild(_motes);
        _hand = new Node2D();
        _hand.Draw += DrawHand;
        AddChild(_hand);
        Scale = Vector2.One * 0.16f;
        Modulate = new Color(1, 1, 1, 0);
    }

    private Sprite2D AddLayer(Texture2D texture, bool visible = true)
    {
        var layer = new Sprite2D { Texture = texture, Scale = Vector2.One * .5f,
            TextureFilter = TextureFilterEnum.Linear, Visible = visible };
        _ring.AddChild(layer);
        return layer;
    }

    internal void PulseGold() => _glowHold = 0.10f;
    internal void Advance(double delta)
    {
        float dt = (float)delta;
        var prefs = LibrarianPreferences050.Current;
        OverlimitStrength = Mathf.MoveToward(OverlimitStrength, OverlimitActive ? 1 : 0, dt * 2.5f);
        if (!OverlimitActive) OverlimitBurst = 0;
        else OverlimitBurst = Math.Max(0, OverlimitBurst - dt);
        if (prefs.ReducedMotion)
        {
            _entranceElapsed = .85f;
            EntranceProgress = 1;
            Scale = Vector2.One;
            Modulate = Colors.White;
            GlowStrength = _castRemaining = 0;
            UpdateLayers(prefs, dt);
            return;
        }
        _formTime += dt;
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
        UpdateLayers(prefs, dt);
    }
    internal void Configure(float radius)
    {
        if (Mathf.IsEqualApprox(Radius, radius)) return;
        Radius = radius;
        if (IndicatorOnly) QueueRedraw();
        else if (_ring is not null)
        {
            _ring.Scale = Vector2.One * (Radius / OuterRadius);
            _hand.QueueRedraw();
        }
    }

    private void UpdateLayers(LibrarianPreferences050 prefs, float dt)
    {
        if (!IsVisibleInTree()) return;
        _ring.Rotation = RingAngle;
        float strength = OverlimitStrength * prefs.EffectOpacity / 100f;
        float burst = prefs.ReducedMotion ? 0 : OverlimitBurst / 1.2f;
        float breathe = prefs.ReducedMotion ? 1 : .88f + .12f * Mathf.Sin(_formTime * 2);
        SetOpacity(_breathingHalo, strength * (.11f + burst * .10f) * breathe);
        SetOpacity(_halo, strength);
        SetOpacity(_burst, burst * strength * .70f);
        if (_burst.Visible) _burst.Scale = Vector2.One * (.5f * (1 + (1 - burst) * 35 / (OuterRadius + 10)));
        SetOpacity(_pulse, GlowStrength);
        _accent.Modulate = new Color(RingGold, .52f)
            .Lerp(FormGold, strength * .8f).Lerp(new Color("ffe3a4"), GlowStrength * .75f);

        // Decorations update at 30 Hz. The ring itself still rotates at frame rate.
        _motes.Visible = prefs.Particles;
        if (!_motes.Visible) return;
        _moteRefresh -= dt;
        bool styleChanged = _moteState.Reduced != prefs.ReducedMotion
            || (_moteState.Strength > 0) != (strength > 0);
        bool needsUpdate = !prefs.ReducedMotion || strength != _moteState.Strength;
        if (!styleChanged && (!needsUpdate || _moteRefresh > 0)) return;
        _moteRefresh = 1f / 30;
        _moteState = (prefs.ReducedMotion, strength, burst);
        _motes.QueueRedraw();
    }

    private static void SetOpacity(Sprite2D layer, float opacity)
    {
        layer.Visible = opacity > 0;
        if (layer.Visible) layer.Modulate = new Color(1, 1, 1, opacity);
    }

    private void DrawMotes()
    {
        if (_artwork is not { } art) return;
        var (reduced, strength, burst) = _moteState;
        for (int i = 0; i < (strength <= 0 ? 0 : reduced ? 8 : 24); i++)
        {
            float life = Mathf.PosMod(_formTime * .45f + i * .618034f, 1);
            var unit = Vector2.FromAngle(i * 2.399963f - RingAngle * .35f);
            var pos = unit * (OuterRadius + 5 + life * (24 + burst * 30));
            float alpha = Mathf.Sin(life * Mathf.Pi) * strength;
            float size = i % 3 == 0 ? 10 : 6;
            _motes.DrawTextureRect(art.FormMote, new Rect2(pos - Vector2.One * (size / 2), Vector2.One * size),
                false, new Color(1, 1, 1, alpha));
            if (!reduced && i % 3 == 0)
                _motes.DrawLine(pos - unit * (4 + burst * 8), pos, new Color("ffcf64") { A = alpha * .55f }, 1.3f, true);
        }
        for (int i = 0; i < 6; i++)
        {
            var mote = Vector2.FromAngle(i * Mathf.Tau / 6 + RingAngle * .45f)
                * (OuterRadius + 7 + 5 * Mathf.Sin(RingAngle * 2 + i * 1.7f));
            var tint = new Color(RingGold, .16f + .06f * (1 + Mathf.Sin(RingAngle * 3 + i)));
            float size = i % 2 == 0 ? 4 : 2.75f;
            _motes.DrawTextureRect(art.Dot, new Rect2(mote - Vector2.One * (size / 2), Vector2.One * size), false, tint);
        }
    }

    public override void _Draw()
    {
        if (!IndicatorOnly) return;
        // Only the 22px tip rises in front of the crown.
        Vector2[] tip = [VisibleNeedleBase + new Vector2(-3.5f, 0),
            VisibleNeedleBase + new Vector2(3.5f, 0), UpperNeedleTip];
        DrawColoredPolygon(tip, RingGold);
        DrawPolyline([tip[0], tip[1], tip[2], tip[0]], Shadow, .8f, true);
        Vector2[] marker = [TopMarkerTip + new Vector2(-9, -7), TopMarkerTip + new Vector2(9, -7), TopMarkerTip];
        DrawColoredPolygon(marker, RingGold);
        DrawPolyline([marker[0], marker[1], marker[2], marker[0]], Shadow, 1f, true);
    }

    private void DrawHand()
    {
        _hand.DrawArc(Vector2.Zero, 11, 0, Mathf.Tau, 48, new Color(RingGold, .22f), 1, true);
        Vector2[] needle = [new(-4.5f, 6), new(4.5f, 6), UpperNeedleTip];
        _hand.DrawColoredPolygon(needle, new Color(RingGold, .82f));
        _hand.DrawPolyline([needle[0], needle[1], needle[2], needle[0]], Shadow, 1.5f, true);
        _hand.DrawCircle(Vector2.Zero, 3.5f, new Color("f6d4a4"));
        _hand.DrawArc(Vector2.Zero, 4.5f, 0, Mathf.Tau, 24, Shadow, 1.5f, true);
    }

    private static RingArtwork CreateArtwork()
    {
        const float inner = OuterRadius - 8;
        var ring = new StringBuilder(Circle(OuterRadius, 4, "#18232a", .75f) + Circle(inner, 1, "#eba466", .22f));
        var accent = new StringBuilder(Circle(OuterRadius, 1.5f, "white"));
        for (int i = 0; i < 24; i++)
        {
            bool major = i % 6 == 0;
            float length = major ? 9 : i % 2 == 0 ? 5 : 3;
            var unit = Vector2.FromAngle(i * Mathf.Tau / 24);
            var from = unit * (inner - length);
            var to = unit * inner;
            (major ? accent : ring).Append(FormattableString.Invariant(
                $"<line x1='{from.X}' y1='{from.Y}' x2='{to.X}' y2='{to.Y}' stroke='{(major ? "white" : "#eba466")}' stroke-width='{(major ? 1.7f : 1f)}' opacity='{(major ? 1f : .22f)}'/>"));
        }

        const float arcRadius = OuterRadius + 11;
        var halo = new StringBuilder(Circle(OuterRadius + 3, 6, "#ffcf64", .32f));
        for (int i = 0; i < 6; i++)
        {
            float angle = i * Mathf.Tau / 6;
            var from = Vector2.FromAngle(angle) * arcRadius;
            var to = Vector2.FromAngle(angle + .55f) * arcRadius;
            halo.Append(FormattableString.Invariant(
                $"<path d='M {from.X} {from.Y} A {arcRadius} {arcRadius} 0 0 1 {to.X} {to.Y}' fill='none' stroke='#ffe8ac' stroke-width='2.2' opacity='.8'/>"));
        }

        return new(
            Rasterize(ring.ToString()), Rasterize(accent.ToString()),
            Rasterize(Circle(OuterRadius + 3, 22, "#ffcf64")), Rasterize(halo.ToString()),
            Rasterize(Circle(OuterRadius + 10, 3, "#ffe8ac")),
            Rasterize(Circle(OuterRadius, 14, "#ffd98c", .11f) + Circle(OuterRadius, 5, "#ffd98c", .36f)
                + Circle(inner, 4, "#ffd98c", .25f)),
            Rasterize("<circle r='1.6' fill='white'/>", 4),
            Rasterize("<circle r='4' fill='#ffcf64' opacity='.12'/><circle r='2' fill='#fff1bd' opacity='.85'/>", 10));
    }

    private static string Circle(float radius, float width, string color, float alpha = 1) =>
        FormattableString.Invariant($"<circle r='{radius}' fill='none' stroke='{color}' stroke-width='{width}' opacity='{alpha}'/>");

    private static Texture2D Rasterize(string geometry, float extent = TextureExtent)
    {
        var svg = FormattableString.Invariant(
            $"<svg xmlns='http://www.w3.org/2000/svg' width='{extent}' height='{extent}' viewBox='{-extent / 2} {-extent / 2} {extent} {extent}'>{geometry}</svg>");
        using var image = new Image();
        var result = image.LoadSvgFromString(svg, 2);
        if (result != Error.Ok) throw new InvalidOperationException($"Could not rasterize orb circle artwork: {result}");
        return ImageTexture.CreateFromImage(image);
    }
}
