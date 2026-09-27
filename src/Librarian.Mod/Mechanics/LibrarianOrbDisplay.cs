using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using Godot;
using Librarian.Core;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Context;


namespace Librarian.Mechanics;

public partial class LibrarianOrbDisplay : Control
{
    internal const float OrbSize = 72;
    internal const float ForegroundScale = 1.05f;
    internal const float BackgroundScale = 0.88f;
    internal const float RowSeparation = 104;
    // Align the dark core, not the outer flame/wave/leaf bounding rectangle.
    internal static Vector2 ArtworkOffset(OrbKind kind) => kind switch
    {
        OrbKind.Fire => new(0, -2),
        OrbKind.Tide => new(0, -1),
        OrbKind.Growth => new(2, -1),
        _ => Vector2.Zero
    };
    private sealed record OrbVisual(Control Root, TextureRect Sprite, Label Caption, TextureRect LockIcon, Label LockCaption, LibrarianOrbIdleAura Aura, LibrarianOrbIdleAura FrontAura, Label Change, LibrarianOrbLockAura060 LockAura)
    {
        public Tween? LayoutTween;
        public bool? Active;
        public bool? Foreground;
        public Vector2 Destination;
        public long? PreviousValue;
        public int? PreviousLockTurns;
        public float FeedbackAge = 1;
        public float ValueAge = 1, LockAge = 1;
        public Color ValueColor = Colors.White, LockColor = Colors.White;
    }
    private readonly Dictionary<OrbKind, OrbVisual> _visuals = new();
    private LibrarianSession? _session;
    private OrbKind? _hovered;
    private string? _lastHoverText;
    private Label? _feedback;
    private double _feedbackRemaining;
    private double _idleTime;
    private LibrarianOrbMagicCircle? _circle;
    private LibrarianOrbMagicCircle? _needle;
    private bool _remote;
    private LibrarianWaterShield? _waterShield;

    internal float FormationRadius { get; private set; } = 138f;
    internal LibrarianOrbMagicCircle MagicCircle => _circle!;
    internal LibrarianOrbMagicCircle ForegroundNeedle => _needle!;


    public void Initialize(LibrarianSession session)
    {
        _session = session;
        _remote = !LocalContext.IsMe(session.Player);
        _waterShield = new LibrarianWaterShield { Name = "LibrarianWaterShield", Session = session };
        AddChild(_waterShield);
        // The circle is a sibling before native Visuals, while this display remains after it.
        // Both stay on the combat canvas plane; negative Z would fall behind the room.
        _circle = new LibrarianOrbMagicCircle { Name = "LibrarianRearMagicCircle", ZIndex = 0 };
        GetParent().AddChild(_circle);
        GetParent().MoveChild(_circle, 0);
        _needle = new LibrarianOrbMagicCircle { Name = "ForegroundNeedle", IndicatorOnly = true, ZIndex = 0 };
        AddChild(_needle);
        foreach (var kind in Enum.GetValues<OrbKind>())
        {
            var slot = new Control { Name = kind.ToString(), Size = new(OrbSize, OrbSize), PivotOffset = new(OrbSize / 2, OrbSize / 2), MouseFilter = MouseFilterEnum.Pass, FocusMode = FocusModeEnum.All };
            AddChild(slot);
            var aura = new LibrarianOrbIdleAura { Position = new(36, 36), Kind = kind };
            slot.AddChild(aura);
            // Ignore the source texture's minimum size BEFORE assigning its 1280px image.
            // Otherwise Godot expands the control before the later IgnoreSize assignment.
            var sprite = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                Texture = GD.Load<Texture2D>(LibrarianOrbVfx.TexturePath(kind)), Position = ArtworkOffset(kind), Size = new(OrbSize, OrbSize),
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore, PivotOffset = new(36, 36) };
            slot.AddChild(sprite);
            var frontAura = new LibrarianOrbIdleAura { Position = new(36, 36), Kind = kind, Front = true };
            slot.AddChild(frontAura);
            var caption = NewLabel(Vector2.Zero, new(OrbSize, OrbSize), 24);
            caption.PivotOffset = new(36, 36);
            slot.AddChild(caption);
            var lockAura = new LibrarianOrbLockAura060 { Name = "LockGlow", Position = new(36,36), Visible = false };
            slot.AddChild(lockAura);
            var lockIcon = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                Texture = GD.Load<Texture2D>(LibrarianOrbLockAura060.TexturePath),
                Position = Vector2.Zero, Size = new(OrbSize, OrbSize),
                // Keep the selected thorn arms on the orb rim and soften only the crossing under the numeral.
                Material = new ShaderMaterial { Shader = new Shader { Code = """
                    shader_type canvas_item;
                    void fragment() {
                        vec4 art = texture(TEXTURE, UV) * COLOR;
                        art.a *= mix(0.16, 0.88, smoothstep(0.10, 0.27, length(UV - vec2(0.5))));
                        COLOR = art;
                    }
                    """ } },
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore };
            slot.AddChild(lockIcon);
            var lockCaption = NewLabel(Vector2.Zero, new(OrbSize, OrbSize), 24);
            lockCaption.PivotOffset = new(36, 36);
            slot.AddChild(lockCaption);
            var change = NewLabel(new(-28, 68), new(128, 28), 18);
            change.Visible = false;
            slot.AddChild(change);
            slot.MouseEntered += () => { _hovered = kind; ShowHover(kind); };
            slot.MouseExited += () => { _hovered = null; _lastHoverText = null; NHoverTipSet.Remove(slot); };
            slot.FocusEntered += () => { _hovered = kind; ShowHover(kind); };
            slot.FocusExited += () => { _hovered = null; _lastHoverText = null; NHoverTipSet.Remove(slot); };
            _visuals.Add(kind, new(slot, sprite, caption, lockIcon, lockCaption, aura, frontAura, change, lockAura));
        }
        _feedback = NewLabel(new(-155, 100), new(310, 36), 22);
        _feedback.Name = "TideBlockFeedback";
        _feedback.Visible = false;
        AddChild(_feedback);
        RefreshState();
    }

    public override void _Process(double delta)
    {
        var room = NCombatRoom.Instance;
        Visible = _session is not null && room is not null && room.IsVisibleInTree()
            && !_session.Player.Creature.IsDead;
        bool interactive = room is not null && ActiveScreenContext.Instance.IsCurrent(room);
        foreach (var v in _visuals.Values) v.Root.MouseFilter = interactive ? MouseFilterEnum.Pass : MouseFilterEnum.Ignore;
        if (!interactive && _hovered is { } previousHover)
        {
            NHoverTipSet.Remove(_visuals[previousHover].Root);
            _hovered = null; _lastHoverText = null;
        }
        if (_circle is not null) _circle.Visible = Visible && LibrarianPreferences050.Current.MagicCircle;
        if (_circle is not null)
            _circle.OverlimitActive = Visible && _session is not null
                && _session.Player.Creature.GetPower<Librarian.LibrarianCode.Powers.Implemented.OverlimitFormPower>() is not null
                && (LibrarianPreferences050.Current.TeammateEffects || LocalContext.IsMe(_session.Player));

        if (!Visible || _session is null)
        {
            if (_hovered is { } hovered) NHoverTipSet.Remove(_visuals[hovered].Root);
            _hovered = null; _lastHoverText = null;
            return;
        }
        var creature = room?.GetCreatureNode(_session.Player.Creature);
        if (creature?.Hitbox is not { } hitbox) { Visible = false; if (_circle is not null) _circle.Visible = false; return; }
        GlobalPosition = hitbox.GetGlobalTransform() * new Vector2(hitbox.Size.X / 2, hitbox.Size.Y * 0.30f);
        if (_waterShield is not null)
        {
            _waterShield.Position = new Vector2(0, hitbox.Size.Y * 0.20f);
            _waterShield.Radius = hitbox.Size.Y * 0.56f;
        }
        FormationRadius = Math.Max(hitbox.Size.X / 2 + 28, hitbox.Size.Y * 0.30f + 48);
        if (_remote) FormationRadius *= 0.75f;
        if (_circle is not null) { _circle.GlobalPosition = GlobalPosition; _circle.Configure(FormationRadius); }
        _circle?.Advance(delta);
        if (_remote && _circle is not null) _circle.Modulate = new Color(1, 1, 1, _hovered is null ? 0.28f : 0.65f);
        _needle?.Configure(FormationRadius);
        RefreshState();
        bool idle = LibrarianPreferences050.Current.OrbIdle && !LibrarianPreferences050.Current.ReducedMotion;
        _idleTime += idle ? delta : 0;
        foreach (var (kind, v) in _visuals)
        {
            float phase = (float)_idleTime * (1.25f + (int)kind * 0.13f) + (int)kind * 2.1f;
            // Keep the optical center fixed; only independent elemental layers move.
            v.Sprite.Position = ArtworkOffset(kind);
            v.Sprite.Rotation = 0;
            v.Aura.SetAppearance(phase, v.Active == true, sessionLocked: v.LockIcon.Visible);
            v.FrontAura.SetAppearance(phase, v.Active == true, sessionLocked: v.LockIcon.Visible);
            v.Aura.Visible = v.FrontAura.Visible = idle && (!_remote || _hovered == kind);
            AdvanceFeedback(v, (float)delta);
            v.LockAura.Advance(phase, idle && (!_remote || _hovered == kind), Math.Max(0, 1 - v.LockAge / .65f));
        }
        _feedbackRemaining = Math.Max(0, _feedbackRemaining - delta);
        if (!LibrarianPreferences050.Current.TideBlockFeedback) _feedbackRemaining = 0;
        if (_feedback is not null) _feedback.Visible = _feedbackRemaining > 0;
    }

    public void RefreshState()
    {
        if (_session is null) return;
        var state = _session.Orbs.Snapshot();
        int background = 0;
        foreach (var orb in state.Orbs)
        {
            var v = _visuals[orb.Kind];
            var destination = orb.IsForeground ? new Vector2(-36, -FormationRadius - 36)
                : new Vector2((background++ == 0 ? -FormationRadius : FormationRadius) - 36, -36);
            float size = orb.IsForeground ? ForegroundScale : BackgroundScale;
            if (_remote) size *= 0.75f;
            if (v.Foreground is null) { v.Root.Position = destination; v.Root.Scale = Vector2.One * size; }
            else if (v.Destination != destination || v.Foreground != orb.IsForeground)
            {
                v.LayoutTween?.Kill();
                v.LayoutTween = CreateTween().SetParallel();
                Vector2 start = v.Root.Position;
                // Side-to-side travel curves below the body instead of crossing its face.
                bool around = start.X < -100 && destination.X > 0;
                v.LayoutTween.TweenMethod(Callable.From<float>(t =>
                    v.Root.Position = start.Lerp(destination, t) + new Vector2(0, around ? FormationRadius * Mathf.Sin(Mathf.Pi * t) : 0)),
                    0f, 1f, 0.35).SetTrans(Tween.TransitionType.Sine);
                v.LayoutTween.TweenProperty(v.Root, "scale", Vector2.One * size, 0.25);
            }
            v.Destination = destination; v.Foreground = orb.IsForeground;
            // Native SceneContainer is -10 and relic VFX are -9. Raising an orb
            // locally to +2 incorrectly paints it over relic activation ghosts.
            v.Root.ZIndex = 0;
            if (v.Active != orb.IsActivated)
            {
                v.Sprite.Modulate = orb.IsActivated ? Colors.White : new Color(0.48f, 0.51f, 0.55f, 0.9f);
                v.Active = orb.IsActivated;
            }
            string centerText = OrbPresentation.CenterText(orb);
            if (v.Caption.Text != centerText)
            {
                v.Caption.Text = centerText;
                // Fit the actual native glyph widths, including large retained values.
                int fontSize = 24;
                var font = v.Caption.GetThemeFont("font");
                while (fontSize > 8 && font.GetStringSize(centerText, fontSize: fontSize).X > 64) fontSize--;
                v.Caption.AddThemeFontSizeOverride("font_size", fontSize);
                v.Caption.Size = new(OrbSize, OrbSize);
            }
            bool showValues = !_remote || _hovered == orb.Kind;
            v.Root.Modulate = new Color(1, 1, 1, showValues ? 1 : 0.55f);
            v.Caption.Visible = !orb.IsLocked && showValues;
            v.LockIcon.Visible = orb.IsLocked;
            v.LockAura.Visible = orb.IsLocked;
            v.LockCaption.Visible = orb.IsLocked && showValues;
            v.LockCaption.Text = OrbPresentation.CenterText(orb);
            v.LockCaption.AddThemeFontSizeOverride("font_size", v.Caption.GetThemeFontSize("font_size"));
            if (v.PreviousValue is { } before && before != orb.Value)
            {
                long difference = (long)orb.Value - before;
                string deltaText = difference > 0 ? "+" + difference : "−" + -difference;
                StartFeedback(v, deltaText,
                    new Color(difference > 0 ? "bcefa0" : "ffad91"));
            }
            if (v.PreviousLockTurns is { } priorLock && priorLock != orb.LockedTurns)
            {
                string change = !orb.IsLocked || orb.IsPermanentlyLocked || priorLock == 0 ? ""
                    : (orb.LockedTurns > priorLock ? "+" : "−") + Math.Abs((long)orb.LockedTurns - priorLock);
                StartFeedback(v, change, new Color(orb.IsLocked ? "d9b1ff" : "bcefa0"), lockNumber: orb.IsLocked);
            }
            v.PreviousValue = orb.Value;
            v.PreviousLockTurns = orb.LockedTurns;
            v.Root.TooltipText = "";
        }
        if (_hovered is { } hovered) ShowHover(hovered);
    }

    private void ShowHover(OrbKind kind)
    {
        if (_session is null || !Visible) return;
        var orb = _session.Orbs.Snapshot()[kind];
        var description = new LocString("static_hover_tips", $"LIBRARIAN_{LibrarianHoverTips.OrbKey(kind)}.description");
        description.Add("Value", orb.Value);
        string text = description.GetFormattedText();
        if (orb.IsLocked)
            text += LibrarianLanguage.Format("LOCK_VALUE", ("Value", orb.Value))
                + (orb.IsPermanentlyLocked ? LibrarianLanguage.Format("LOCK_PERMANENT") : LibrarianLanguage.Format("LOCK_TURNS", ("Turns", orb.LockedTurns)));
        if (_lastHoverText == text) return;
        _lastHoverText = text;
        var root = _visuals[kind].Root;
        NHoverTipSet.Remove(root);
        var tip = new HoverTip(new LocString("cards", $"LIBRARIAN-ORB_CHOICE_{kind.ToString().ToUpperInvariant()}.title"), text);
        NHoverTipSet.CreateAndShow(root, tip, HoverTip.GetHoverTipAlignment(root))?.SetFollowOwner();
    }

    public void Pulse(OrbKind kind)
    {
        if (!_visuals.TryGetValue(kind, out var v)) return;
        _circle?.PulseGold();
        StartFeedback(v, "", new Color("f5d897"));
    }

    private static void StartFeedback(OrbVisual v, string text, Color color, bool lockNumber = false)
    {
        v.FeedbackAge = 0;
        if (lockNumber) { v.LockAge = 0; v.LockColor = color; }
        else { v.ValueAge = 0; v.ValueColor = color; }
        v.Change.Text = text;
        int size = 18;
        var font = v.Change.GetThemeFont("font");
        while (size > 8 && font.GetStringSize(text, fontSize: size).X > 124) size--;
        v.Change.AddThemeFontSizeOverride("font_size", size);
        v.Change.AddThemeColorOverride("font_color", color);
    }

    private void AdvanceFeedback(OrbVisual v, float delta)
    {
        const float duration = .65f;
        v.FeedbackAge = Math.Min(duration, v.FeedbackAge + delta);
        float t = v.FeedbackAge / duration;
        bool reduced = LibrarianPreferences050.Current.ReducedMotion;
        v.ValueAge = Math.Min(duration, v.ValueAge + delta);
        v.LockAge = Math.Min(duration, v.LockAge + delta);
        AnimateNumber(v.Caption, v.ValueAge / duration, v.ValueColor, reduced);
        AnimateNumber(v.LockCaption, v.LockAge / duration, v.LockColor, reduced);
        v.Change.Position = new(-28, 68 - (reduced ? 0 : t * 9));
        v.Change.Modulate = new Color(1, 1, 1, 1 - t * t);
        v.Change.Visible = v.Change.Text.Length > 0 && t < 1 && (!_remote || _hovered is { } hovered && _visuals[hovered] == v);
    }

    // Orb values and lock countdowns share exactly the same curve and restoration.
    private static void AnimateNumber(Label label, float t, Color color, bool reduced)
    {
        float pulse = reduced ? 0 : Mathf.Sin(Math.Min(1, t * 2) * Mathf.Pi) * .2f;
        label.Scale = Vector2.One * (1 + pulse);
        label.AddThemeColorOverride("font_color", color.Lerp(new Color("eedfc5"), t));
    }

    public void ShowTideChange(long amount, bool expired)
    {
        if (_feedback is null || amount <= 0 || !LibrarianPreferences050.Current.TideBlockFeedback) return;
        _feedback.Text = LibrarianLanguage.Format(expired ? "TIDE_EXPIRED" : "TIDE_GAIN", ("Amount", amount));
        _feedback.AddThemeColorOverride("font_color", new Color(expired ? "e4b187" : "82e6f5"));
        _feedbackRemaining = 1.8;
        _feedback.Visible = true;
    }

    public override void _ExitTree()
    {
        foreach (var v in _visuals.Values) { NHoverTipSet.Remove(v.Root); v.LayoutTween?.Kill(); }
        if (GodotObject.IsInstanceValid(_circle)) _circle!.QueueFree();

        _session = null;
    }

    private static Label NewLabel(Vector2 position, Vector2 size, int fontSize)
    {
        var label = new Label { Position = position, Size = size, ClipText = true, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", new Color("eedfc5"));
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 2);
        return label;
    }
    public bool TryGetOrbCenter(OrbKind kind, out Vector2 center)
    {
        center = default;
        if (!IsInsideTree() || !IsVisibleInTree() || !_visuals.TryGetValue(kind, out var visual)) return false;
        center = visual.Root.GetGlobalTransform() * new Vector2(OrbSize / 2, OrbSize / 2);
        return true;
    }
}
