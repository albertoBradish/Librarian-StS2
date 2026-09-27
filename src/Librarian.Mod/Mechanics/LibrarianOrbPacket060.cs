using Godot;
using Librarian.Core;

namespace Librarian.Mechanics;

/// <summary>Bounded procedural settlement packet; lifetime owned by Travel's finally block.</summary>
public partial class LibrarianOrbPacket060 : Node2D
{
    internal OrbKind Kind { get; init; }
    internal Vector2 Direction { get; set; } = Vector2.Right;
    internal float Progress { get; set; }
    public override void _Draw()
    {
        var tint = new Color(Kind switch { OrbKind.Fire => "ffad62", OrbKind.Tide => "8be5f4", _ => "c1e18b" });
        float envelope = Math.Min(1, Math.Min(Progress * 8 + .25f, (1 - Progress) * 8 + .25f));
        var normal = new Vector2(-Direction.Y, Direction.X);
        if (LibrarianPreferences050.Current.Particles)
            for (int i = 6; i >= 1; i--)
            {
                float w = 1 - i / 7f;
                var p = -Direction * (i * 5) + normal * Mathf.Sin(Progress * 18 - i) * i * .5f;
                DrawCircle(p, 1 + w * 3, new Color(tint, w * .5f * envelope));
            }
        DrawCircle(Vector2.Zero, 7, new Color(tint, .22f * envelope));
        if (Kind == OrbKind.Growth)
            DrawColoredPolygon([-Direction * 7, normal * 4, Direction * 8, -normal * 4], new Color(tint, envelope));
        else
            DrawCircle(Vector2.Zero, 3.5f, new Color(tint, envelope));
    }
}
