using Godot;

namespace Librarian.Mechanics;

/// <summary>Approved continuous painting inside the native character selection flow.</summary>
public partial class LibrarianCharacterSelect040 : Control
{
    public const string ScenePath = "res://Librarian/scenes/character_select/v0.4.0/floating_archive.tscn";
    public const string IllustrationPath = "res://Librarian/images/character_select/v0.4.0/selection.png";
    private TextureRect _illustration = null!;
    private Transform2D _lastCanvas;
    private Vector2 _lastViewport;
    private Vector2 _restPosition;
    private double _elapsed;
    private LibrarianSelectionMotes101 _motes = null!;
    internal TextureRect Illustration => _illustration;
    internal Rect2 VisibleArtworkRect { get; private set; }

    public override void _Ready()
    {
        _illustration = GetNode<TextureRect>("Illustration");
        _illustration.Texture = GD.Load<Texture2D>(IllustrationPath);
        _motes = new LibrarianSelectionMotes101 { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_motes);
        // Selection feedback belongs to NCharacterSelectScreen. Keep the painting
        // unmodified: UV warping also displaced the architecture behind the hands.
        Resized += Layout;
        Layout();
    }

    public override void _ExitTree() => Resized -= Layout;

    private void Layout()
    {
        if (_illustration is null || _illustration.Texture is null) return;
        _lastCanvas = GetGlobalTransformWithCanvas();
        _lastViewport = GetViewportRect().Size;
        var inverse = _lastCanvas.AffineInverse();
        var topLeft = inverse * Vector2.Zero;
        var bottomRight = inverse * _lastViewport;
        VisibleArtworkRect = new Rect2(topLeft, bottomRight - topLeft);
        var view = VisibleArtworkRect.Size;
        var textureSize = _illustration.Texture.GetSize();
        float cover = Math.Max(view.X / textureSize.X, view.Y / textureSize.Y) * 1.012f;
        _illustration.Size = textureSize * cover;
        _illustration.Position = topLeft + (view - _illustration.Size) / 2;
        _restPosition = _illustration.Position;
        _motes.Position = topLeft;
        _motes.Size = view;
    }

    public override void _Process(double delta)
    {
        if (_lastCanvas != GetGlobalTransformWithCanvas() || _lastViewport != GetViewportRect().Size) Layout();
        _elapsed += delta;
        var settings = LibrarianPreferences050.Current;
        _illustration.Position = _restPosition + (settings.ReducedMotion || !settings.OrbIdle
            ? Vector2.Zero : new Vector2(0, (float)Math.Sin(_elapsed * Math.Tau / 10) * VisibleArtworkRect.Size.Y * .003f));
    }
}

/// <summary>Bounded, deterministic decoration; never consumes gameplay RNG or captures input.</summary>
public partial class LibrarianSelectionMotes101 : Control
{
    private double _time;
    public override void _Process(double delta)
    {
        if (!LibrarianPreferences050.Current.ReducedMotion) _time += delta;
        QueueRedraw();
    }
    public override void _Draw()
    {
        var settings = LibrarianPreferences050.Current;
        if (!settings.Particles || settings.ReducedMotion) return;
        for (int i = 0; i < 28; i++)
        {
            float phase = (float)((i * .61803398875 + _time * (.012 + i % 4 * .002)) % 1);
            float x = (float)((i * .38196601125) % 1) * Size.X;
            x += (float)Math.Sin(_time * .35 + i * 2.4) * Size.X * .006f;
            float alpha = MathF.Sin(phase * MathF.PI) * .42f * settings.EffectOpacity / 100f;
            float radius = (1.1f + i % 3 * .45f) * Size.Y / 1080;
            var color = i % 4 == 0 ? new Color(.45f,.83f,.84f,alpha) : new Color(.92f,.76f,.45f,alpha);
            DrawCircle(new Vector2(x, Size.Y * (1 - phase)), radius * 3, new Color(color, alpha * .12f));
            DrawCircle(new Vector2(x, Size.Y * (1 - phase)), radius, color);
        }
    }
}
