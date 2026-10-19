using Godot;
using MegaCrit.Sts2.Core.Nodes.Animation;

namespace Librarian.Mechanics;

/// <summary>Owned selection-only Spine illustration; the game's UI and auto-player remain native.</summary>
public partial class LibrarianCharacterSelect138 : Control
{
    public const string ScenePath = "res://Librarian/scenes/character_select/v1.3.12-selection/floating_archive.tscn";
    public const string BankPath = "res://Librarian/spine/v1.3.12-selection/bank.res";
    public const string LoopName = "selection_loop";
    internal static readonly Vector2 CanvasSize = new(1672, 941);
    private Node2D _spine = null!;
    private Sprite2D _background = null!;
    private Node2D _rear = null!;
    private CpuParticles2D _beamLeft = null!, _beamRight = null!;
    private CpuParticles2D _gold = null!, _cyan = null!;
    private LibrarianSelection138Glimmer _glimmer = null!;
    private Transform2D _lastCanvas;
    private Vector2 _lastViewport;
    private bool? _moving;
    private double _time;
    private bool _hidden;
    internal Node2D Spine => _spine;
    internal Rect2 VisibleArtworkRect { get; private set; }
    internal Rect2 ArtworkRect => new(_spine.Position, CanvasSize * _spine.Scale);
    internal bool Moving => _moving == true;
    internal bool ParticlesEmitting => _gold.Emitting || _cyan.Emitting || BeamsEmitting;
    internal bool BeamsEmitting => _beamLeft.Emitting && _beamRight.Emitting;
    internal CpuParticles2D PrimaryBeam => _beamLeft;
    internal Node2D RearLayer => _rear;
    internal int BeamCount => _beamLeft.Amount + _beamRight.Amount;
    internal int DecorationCount => _gold.Amount + _cyan.Amount + _beamLeft.Amount + _beamRight.Amount;
    internal bool RearLayerOrder => _background.GetIndex() < _rear.GetIndex() && _rear.GetIndex() < _spine.GetIndex()
        && _background.ZIndex == 0 && _rear.ZIndex == 0 && _spine.ZIndex == 0;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _spine = GetNode<Node2D>("SpineSprite");
        _background = GetNode<Sprite2D>("CleanBackground");
        if (_spine.GetClass() != "SpineSprite") throw new InvalidOperationException("Selection requires the game's actual SpineSprite.");
        _spine.AddChild(new NSpineAutoPlayer { Name = "NativeAutoPlayer" });
        // The opaque backdrop is separate from the transparent actor bank so
        // native emitters can genuinely pass behind the book, hands and ribbons.
        _rear = new Node2D { Name = "RearSegmentedLight" };
        AddChild(_rear);
        MoveChild(_rear, _spine.GetIndex());
        _beamLeft = Beams("RisingGoldSegments", new Vector2(620, 800), new Vector2(300, 220), new Color(1, .82f, .48f), 11);
        _beamRight = Beams("RisingPaleSegments", new Vector2(1520, 780), new Vector2(120, 240), new Color(.78f, .91f, .81f), 2);
        _gold = Motes("OldGoldDust", new Color(.95f, .73f, .38f), 17);
        _cyan = Motes("CyanDust", new Color(.40f, .83f, .84f), 5);
        _glimmer = new LibrarianSelection138Glimmer { Name = "SelectionGlimmer", OwnerController = this };
        AddChild(_glimmer);
        Resized += Layout;
        Layout();
    }

    private CpuParticles2D Beams(string name, Vector2 position, Vector2 area, Color tint, int amount)
    {
        var particles = new CpuParticles2D
        {
            Name = name, Position = position, Amount = amount, Lifetime = 8.5,
            Preprocess = 8.5, Randomness = .55f, LocalCoords = true,
            Texture = GD.Load<Texture2D>("res://Librarian/images/character_select/v1.3.8-selection/beam-segments.svg"),
            EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle,
            EmissionRectExtents = area, Direction = Vector2.Up, Spread = 0,
            Gravity = new Vector2(0, -3), InitialVelocityMin = 34, InitialVelocityMax = 56,
            ScaleAmountMin = .68f, ScaleAmountMax = 1.05f,
            ColorRamp = new Gradient
            {
                Offsets = [0, .16f, .58f, .85f, 1],
                Colors = [new Color(tint, 0), new Color(tint, .95f), new Color(tint, .78f), new Color(tint, .32f), new Color(tint, 0)]
            },
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add }
        };
        _rear.AddChild(particles);
        return particles;
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
            Texture = GD.Load<Texture2D>("res://Librarian/images/character_select/v1.3.8-selection/mote.svg"),
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
        _background.Position = _rear.Position = _spine.Position;
        _background.Scale = _rear.Scale = _spine.Scale;
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
        if (hidden) { _gold.Emitting = _cyan.Emitting = _beamLeft.Emitting = _beamRight.Emitting = false; return; }
        if (_lastCanvas != GetGlobalTransformWithCanvas() || _lastViewport != GetViewportRect().Size) Layout();
        var settings = LibrarianPreferences050.Current;
        bool moving = settings.OrbIdle && !settings.ReducedMotion;
        Motion(moving);
        bool particles = settings.Particles && !settings.ReducedMotion;
        _gold.Emitting = _cyan.Emitting = particles;
        _beamLeft.Emitting = _beamRight.Emitting = particles;
        int total = Math.Clamp(settings.EffectLimit, 4, 40);
        int beams = Math.Clamp(total * 2 / 3, 2, 28);
        int right = Math.Max(1, beams / 3);
        int cyan = Math.Max(1, (total - beams) / 5);
        if (_beamLeft.Amount != beams - right) _beamLeft.Amount = beams - right;
        if (_beamRight.Amount != right) _beamRight.Amount = right;
        if (_gold.Amount != total - beams - cyan) _gold.Amount = total - beams - cyan;
        if (_cyan.Amount != cyan) _cyan.Amount = cyan;
        _rear.Visible = _gold.Visible = _cyan.Visible = particles;
        _rear.Modulate = _gold.Modulate = _cyan.Modulate = new Color(1, 1, 1, settings.EffectOpacity / 100f);
        if (moving) _time += delta;
        _glimmer.Time = _time;
        _glimmer.QueueRedraw();
    }

    public override void _ExitTree() => Resized -= Layout;
}
