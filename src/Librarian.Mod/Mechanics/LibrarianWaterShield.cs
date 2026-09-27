using Godot;

namespace Librarian.Mechanics;

public partial class LibrarianWaterShield : Node2D
{
    internal LibrarianSession? Session { get; set; }
    internal float Radius { get; set; } = 110;
    private float _phase;
    public override void _Process(double delta)
    {
        Visible = Session is { } s && !s.Player.Creature.IsDead && s.Waves.Amount > 0;
        if (!Visible) return;
        _phase += (float)delta;
        QueueRedraw();
    }
    public override void _Draw()
    {
        DrawSetTransform(Vector2.Zero, 0, new Vector2(0.8f, 1));
        DrawArc(Vector2.Zero, Radius, 0, Mathf.Tau, 96, new Color(0.35f, 0.78f, 0.95f, 0.20f), 2, true);
        for (int i = 0; i < 3; i++)
        {
            float angle = _phase * 0.3f + i * Mathf.Tau / 3;
            DrawArc(Vector2.Zero, Radius + Mathf.Sin(_phase + i) * 2, angle, angle + 0.65f, 20,
                new Color(0.65f, 0.91f, 1, 0.28f), 3, true);
        }
    }
}
