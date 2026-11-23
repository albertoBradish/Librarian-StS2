using Godot;

namespace Librarian.Mechanics;

public partial class LibrarianWaterShield : Node2D
{
    internal LibrarianSession? Session { get; set; }
    private static readonly Color RimColor = new(.35f, .78f, .95f, .20f);
    private static readonly Color SegmentColor = new(.65f, .91f, 1, .28f);
    private readonly Node2D[] _segments = new Node2D[3];
    private float _radius = 110;
    private float _phase;

    internal float Radius
    {
        get => _radius;
        set
        {
            if (_radius == value) return;
            _radius = value;
            QueueRedraw();
            foreach (var segment in _segments) segment?.QueueRedraw();
            UpdateSegments();
        }
    }

    public override void _Ready()
    {
        Scale = new Vector2(.8f, 1);
        for (int i = 0; i < _segments.Length; i++)
        {
            var segment = new Node2D();
            segment.Draw += () => segment.DrawArc(Vector2.Zero, _radius, 0, .65f, 20, SegmentColor, 3, true);
            _segments[i] = segment;
            AddChild(segment);
        }
        UpdateSegments();
    }

    public override void _Process(double delta)
    {
        Visible = Session is { } s && !s.Player.Creature.IsDead && s.Waves.Amount > 0;
        if (!IsVisibleInTree() || LibrarianPreferences050.Current.ReducedMotion) return;
        _phase += (float)delta;
        UpdateSegments();
    }

    private void UpdateSegments()
    {
        if (_segments[0] is null || _radius <= 0) return;
        // Rotate the cached arcs in the elliptical parent's local space.
        for (int i = 0; i < _segments.Length; i++)
        {
            _segments[i].Rotation = _phase * .3f + i * Mathf.Tau / 3;
            _segments[i].Scale = Vector2.One * (1 + Mathf.Sin(_phase + i) * 2 / _radius);
        }
    }

    public override void _Draw() => DrawArc(Vector2.Zero, _radius, 0, Mathf.Tau, 96, RimColor, 2, true);
}
