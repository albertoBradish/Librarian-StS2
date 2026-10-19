using Godot;
using MegaCrit.Sts2.Core.Nodes.Animation;

namespace Librarian.Mechanics;

/// <summary>Owned selection-only Spine illustration; the game's UI and auto-player remain native.</summary>
public partial class LibrarianCharacterSelect137 : Control
{
    public const string ScenePath = "res://Librarian/scenes/character_select/v1.3.7-selection/floating_archive.tscn";
    public const string BankPath = "res://Librarian/spine/v1.3.7-selection/bank.res";
    public const string LoopName = "selection_loop";
    internal static readonly Vector2 CanvasSize = new(1672, 941);
    private Node2D _spine = null!;
    private CpuParticles2D _gold = null!, _cyan = null!;
    private LibrarianSelection137Glimmer _glimmer = null!;
    private Transform2D _lastCanvas;
    private Vector2 _lastViewport;
    private bool? _moving;
    private double _time;
    private bool _hidden;
    internal Node2D Spine => _spine;
    internal Rect2 VisibleArtworkRect { get; private set; }
    internal Rect2 ArtworkRect => new(_spine.Position, CanvasSize * _spine.Scale);
    internal bool Moving => _moving == true;
    internal bool ParticlesEmitting => _gold.Emitting || _cyan.Emitting;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _spine = GetNode<Node2D>("SpineSprite");
        if (_spine.GetClass() != "SpineSprite") throw new InvalidOperationException("Selection requires the game's actual SpineSprite.");
        _spine.AddChild(new NSpineAutoPlayer { Name = "NativeAutoPlayer" });
        _gold = Motes("OldGoldDust", new Color(.95f, .73f, .38f), 17);
        _cyan = Motes("CyanDust", new Color(.40f, .83f, .84f), 5);
        _glimmer = new LibrarianSelection137Glimmer { Name = "SelectionGlimmer", OwnerController = this };
        AddChild(_glimmer);
        Resized += Layout;
        Layout();
    }

    private CpuParticles2D Motes(string name, Color color, int amount)
    {
        var gradient = new Gradient
        {
            Offsets = [0, .18f, .75f, 1],
            Colors = [new Color(color, 0), new Color(color, .30f), new Color(color, .17f), new Color(color, 0)]
        };
        var particles = new CpuParticles2D
        {
            Name = name, Amount = amount, Lifetime = 11, Preprocess = 11,
            Texture = GD.Load<Texture2D>("res://Librarian/images/character_select/v1.3.7-selection/mote.svg"),
            EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle,
            Direction = Vector2.Up, Spread = 24,
            Gravity = new Vector2(1.5f, -1.8f),
            InitialVelocityMin = 5, InitialVelocityMax = 11,
            ScaleAmountMin = .16f, ScaleAmountMax = .36f,
            ColorRamp = gradient, LocalCoords = true,
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add }
        };
        AddChild(particles);
        return particles;
    }

    private void Layout()
    {
        if (_spine is null) return;
        _lastCanvas = GetGlobalTransformWithCanvas();
        _lastViewport = GetViewportRect().Size;
        var inverse = _lastCanvas.AffineInverse();
        var top = inverse * Vector2.Zero;
        var bottom = inverse * _lastViewport;
        VisibleArtworkRect = new Rect2(top, bottom - top);
        float cover = Math.Max(VisibleArtworkRect.Size.X / CanvasSize.X, VisibleArtworkRect.Size.Y / CanvasSize.Y) * 1.024f;
        _spine.Scale = Vector2.One * cover;
        _spine.Position = top + (VisibleArtworkRect.Size - CanvasSize * cover) / 2;
        foreach (var particles in new[] { _gold, _cyan })
        {
            particles.Position = top + VisibleArtworkRect.Size * new Vector2(.76f, .54f);
            particles.EmissionRectExtents = VisibleArtworkRect.Size * new Vector2(.29f, .47f);
            particles.Scale = Vector2.One * (VisibleArtworkRect.Size.Y / 941f);
        }
        _glimmer.Position = _spine.Position;
        _glimmer.Scale = _spine.Scale;
    }

    internal Vector2 BonePosition(string bone)
    {
        using var value = _spine.Call("get_global_bone_transform", bone);
        return value.AsTransform2D().Origin;
    }

    internal Vector2 LocalBonePosition(string bone) => _spine.GlobalTransform.AffineInverse() * BonePosition(bone);

    private void Motion(bool moving)
    {
        _spine.Set("time_scale", moving ? 1f : 0f);
        if (_moving == moving) return;
        _moving = moving;
        // The one native selection loop is resumed from its setup frame. No second
        // animator or combat action bank is installed on this sprite.
        using var stateValue = _spine.Call("get_animation_state");
        var state = stateValue.AsGodotObject();
        if (state is null) return;
        if (state.HasMethod("set_time_scale")) state.Call("set_time_scale", moving ? 1f : 0f);
        using var entryValue = state.Call("get_current", 0);
        var entry = entryValue.AsGodotObject();
        if (entry is not null) entry.Call("set_track_time", 0f);
        GC.KeepAlive(entry); GC.KeepAlive(state);
    }

    public override void _Process(double delta)
    {
        if (_spine is null) return;
        bool hidden = !IsVisibleInTree();
        if (hidden != _hidden)
        {
            _hidden = hidden;
            foreach (var child in GetChildren()) child.ProcessMode = hidden ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
            _moving = null;
        }
        if (hidden) { _gold.Emitting = _cyan.Emitting = false; return; }
        if (_lastCanvas != GetGlobalTransformWithCanvas() || _lastViewport != GetViewportRect().Size) Layout();
        var settings = LibrarianPreferences050.Current;
        bool moving = settings.OrbIdle && !settings.ReducedMotion;
        Motion(moving);
        bool particles = settings.Particles && !settings.ReducedMotion;
        _gold.Emitting = _cyan.Emitting = particles;
        int total = Math.Clamp(settings.EffectLimit, 4, 40);
        int cyan = Math.Max(1, total / 5);
        if (_gold.Amount != total - cyan) _gold.Amount = total - cyan;
        if (_cyan.Amount != cyan) _cyan.Amount = cyan;
        _gold.Visible = _cyan.Visible = particles;
        _gold.Modulate = _cyan.Modulate = new Color(1, 1, 1, settings.EffectOpacity / 100f);
        if (moving) _time += delta;
        _glimmer.Time = _time;
        _glimmer.QueueRedraw();
    }

    public override void _ExitTree() => Resized -= Layout;
}
