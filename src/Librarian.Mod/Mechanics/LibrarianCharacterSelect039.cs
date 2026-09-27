using Godot;

namespace Librarian.Mechanics;

/// <summary>A continuous illustration, with bounded ambient motion and no separated body pieces.</summary>
public partial class LibrarianCharacterSelect039 : Control
{
    public const string ScenePath = "res://Librarian/scenes/character_select/v0.3.9/floating_archive.tscn";
    public const string HeroPath = "res://Librarian/images/character_select/v0.3.9/hero.png";
    private TextureRect _hero = null!;
    private TextureRect _library = null!;
    private ShaderMaterial _heroMaterial = null!;
    private ShaderMaterial _dustMaterial = null!;
    private Vector2 _heroRest;
    private Vector2 _libraryRest;
    private float _units;
    private double _elapsed;
    private Transform2D _lastCanvasTransform;
    private Vector2 _lastViewportSize;
    internal bool AuditPaused { get; set; }
    internal TextureRect Hero => _hero;
    internal double AnimationTime => _elapsed;
    internal Rect2 VisibleArtworkRect { get; private set; }

    public override void _Ready()
    {
        _hero = GetNode<TextureRect>("Hero");
        _library = GetNode<TextureRect>("Library");
        // Each instance owns its uniforms when native menus briefly overlap during transitions.
        _heroMaterial = (ShaderMaterial)_hero.Material.Duplicate();
        _hero.Material = _heroMaterial;
        var atmosphere = GetNode<ColorRect>("Atmosphere");
        _dustMaterial = (ShaderMaterial)atmosphere.Material.Duplicate();
        atmosphere.Material = _dustMaterial;
        Resized += LayoutIllustration;
        LayoutIllustration();
    }

    public override void _ExitTree() => Resized -= LayoutIllustration;

    private void LayoutIllustration()
    {
        if (_hero is null || _hero.Texture is null) return;
        // Native AnimatedBg is an oversized 2560x1200 canvas with its own 1.1 scale.
        // Fit against the actual visible window, transformed back into this canvas, rather than
        // against the oversized parent; otherwise a complete illustration would be cropped.
        _lastCanvasTransform = GetGlobalTransformWithCanvas();
        _lastViewportSize = GetViewportRect().Size;
        var inverse = _lastCanvasTransform.AffineInverse();
        Vector2 topLeft = inverse * Vector2.Zero;
        Vector2 bottomRight = inverse * _lastViewportSize;
        VisibleArtworkRect = new Rect2(topLeft, bottomRight - topLeft);
        var view = VisibleArtworkRect.Size;
        _units = view.Y / 1080f;
        var imageSize = _hero.Texture.GetSize();
        float fit = Mathf.Min(view.X * 0.55f / imageSize.X, view.Y * 0.95f / imageSize.Y);
        _hero.Size = imageSize * fit;
        _hero.PivotOffset = _hero.Size * 0.5f;
        _heroRest = topLeft + new Vector2(view.X * 0.735f, view.Y * 0.485f) - _hero.Size * 0.5f;
        _libraryRest = topLeft + view * -0.025f;
        _library.Position = _libraryRest;
        _library.Size = view * 1.05f;
        var atmosphere = GetNode<ColorRect>("Atmosphere");
        atmosphere.Position = topLeft;
        atmosphere.Size = view;
        _dustMaterial.SetShaderParameter("aspect", view.X / view.Y);
        ApplyPose();
    }

    public override void _Process(double delta)
    {
        if (_lastCanvasTransform != GetGlobalTransformWithCanvas() || _lastViewportSize != GetViewportRect().Size)
            LayoutIllustration();
        if (AuditPaused || !IsVisibleInTree()) return;
        _elapsed = (_elapsed + delta) % 240d;
        ApplyPose();
    }

    internal void SampleAt(double seconds)
    {
        _elapsed = seconds % 240d;
        ApplyPose();
    }

    private void ApplyPose()
    {
        if (_hero is null) return;
        float phase = (float)_elapsed * Mathf.Tau / 8f;
        _hero.Position = _heroRest + new Vector2(Mathf.Sin(phase * 0.5f) * 3f, Mathf.Sin(phase) * 7f) * _units;
        _hero.Rotation = Mathf.Sin(phase * 0.5f) * 0.005f;
        _hero.Scale = Vector2.One * (1f + Mathf.Sin(phase) * 0.002f);
        _library.Position = _libraryRest + new Vector2(Mathf.Sin(phase * 0.5f) * -2.5f, Mathf.Cos(phase) * 1.5f) * _units;
        _heroMaterial.SetShaderParameter("motion_time", (float)_elapsed);
        _dustMaterial.SetShaderParameter("motion_time", (float)_elapsed);
    }
}
