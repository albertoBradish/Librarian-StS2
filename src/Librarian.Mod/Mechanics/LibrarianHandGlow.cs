using Godot;

namespace Librarian.Mechanics;

public partial class LibrarianHandGlow : Node2D
{
    internal float Radius { get; init; } = 100;
    private float _remaining;
    private Color _color;
    internal void Pulse(Color color) { _color = color; _remaining = 0.5f; QueueRedraw(); }
    internal void Clear() { _remaining = 0; QueueRedraw(); }
    public override void _Process(double delta)
    {
        if (_remaining <= 0) return;
        _remaining = Math.Max(0, _remaining - (float)delta);
        QueueRedraw();
    }
    public override void _Draw()
    {
        float strength = Mathf.Sin(_remaining / 0.5f * Mathf.Pi);
        if (strength <= 0) return;
        for (int i = 5; i >= 1; i--)
            DrawCircle(Vector2.Zero, Radius * i / 5, new Color(_color.R, _color.G, _color.B, strength * 0.025f));
    }
}
