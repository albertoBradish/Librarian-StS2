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
    internal TextureRect Illustration => _illustration;
    internal Rect2 VisibleArtworkRect { get; private set; }

    public override void _Ready()
    {
        _illustration = GetNode<TextureRect>("Illustration");
        _illustration.Texture = GD.Load<Texture2D>(IllustrationPath);
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
    }

    public override void _Process(double delta)
    {
        if (_lastCanvas != GetGlobalTransformWithCanvas() || _lastViewport != GetViewportRect().Size) Layout();
    }
}
