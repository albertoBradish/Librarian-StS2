using Godot;

namespace Librarian.Mechanics;

/// <summary>Ground anchored, layered soft ellipse; independent of the floating body.</summary>
public partial class LibrarianGroundShadow : Node2D
{
    internal float Radius { get; init; } = 75;
    public override void _Draw()
    {
        DrawSetTransform(new Vector2(0, -2), 0, new Vector2(1, 0.20f));
        for (int i = 8; i >= 1; i--)
            DrawCircle(Vector2.Zero, Radius * (0.50f + i * 0.065f), new Color(0.02f, 0.03f, 0.04f, 0.032f));
    }
}
