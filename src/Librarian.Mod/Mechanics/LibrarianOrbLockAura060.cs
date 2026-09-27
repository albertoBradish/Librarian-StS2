using Godot;

namespace Librarian.Mechanics;

/// <summary>Glow behind the chosen thorn X and sparse outward motes; no movement of the icon or counter.</summary>
public partial class LibrarianOrbLockAura060 : Node2D
{
    internal const string TexturePath = "res://Librarian/images/orbs/v0.6.0/lock_b.png";
    private Texture2D? _texture;
    private float _phase;
    private bool _animate;
    private float _impulse;
    internal float Phase => _phase;
    internal bool ParticlesVisible => _animate && LibrarianPreferences050.Current.Particles;
    public override void _Ready() => _texture = GD.Load<Texture2D>(TexturePath);
    internal void Advance(float phase, bool animate, float impulse)
    {
        _phase = phase; _animate = animate; _impulse = impulse;
        QueueRedraw();
    }
    public override void _Draw()
    {
        float opacity = LibrarianPreferences050.Current.EffectOpacity / 100f;
        float breath = _animate ? .75f + .25f * Mathf.Sin(_phase * 1.8f) : .75f;
        // Layered, very low-alpha copies form a soft silhouette halo. The selected art stays crisp above.
        if (_texture is not null)
            for (int i = 3; i >= 1; i--)
            {
                float extent = 36 + i * 1.5f;
                DrawTextureRect(_texture, new Rect2(new Vector2(-extent,-extent),Vector2.One * extent * 2),
                    false,new Color(.75f,.32f,1,(.035f + _impulse * .025f) * breath * opacity));
            }
        if (!ParticlesVisible) return;
        for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < 4; i++)
            {
                float t = Mathf.PosMod(_phase * .32f + i * .243f,1);
                // Paired rim paths keep the shared central number unobstructed.
                float angle = -.95f + i * .63f - t * .2f;
                float radius = 27 + t * 10;
                var p = new Vector2(side * Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                float fade = Mathf.Sin(t * Mathf.Pi) * .7f * opacity;
                var tint = new Color(.79f,.54f,1,fade);
                float r = 1 + (1-t) * .7f;
                DrawColoredPolygon([p+new Vector2(0,-r),p+new Vector2(r,0),p+new Vector2(0,r),p+new Vector2(-r,0)],tint);
            }
    }
}
