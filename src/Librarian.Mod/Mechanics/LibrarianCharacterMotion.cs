using Godot;

namespace Librarian.Mechanics;

/// <summary>Original lightweight animation for the illustrated floating-book character.</summary>
public partial class LibrarianCharacterMotion : Node
{
    [Export] public string VisualPath { get; set; } = "Visuals";
    [Export] public float DisplayHeight { get; set; } = 280f;
    [Export] public float RestingTilt { get; set; }
    private AnimationPlayer _player = null!;
    private Node2D _visual = null!;
    private Sprite2D? _rightHand;
    private Sprite2D? _leftHand;
    private Tween? _handTween;
    private bool _dead;
    private LibrarianCharacterRig050? _rig;
    internal LibrarianCharacterRig050? Rig => _rig;
    internal bool HasSeparateHands => _rightHand is not null && _leftHand is not null;

    public override void _Ready()
    {
        var root = GetParent();
        var body = root.GetNode<Sprite2D>(VisualPath + "/Body");
        _visual = root.GetNode<Node2D>(VisualPath);
        var shadow = new LibrarianGroundShadow { Name = "LibrarianGroundShadow", Radius = DisplayHeight * 0.27f };
        var shadowParent = _visual.GetParent();
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(shadowParent) || !shadowParent.IsInsideTree()) { shadow.Free(); return; }
            shadowParent.AddChild(shadow);
            shadowParent.MoveChild(shadow, 0);
        }).CallDeferred();
        const string layers = "res://Librarian/images/character/v0.4.0/";
        if (ResourceLoader.Exists(layers + "body.png") && ResourceLoader.Exists(layers + "hand_right.png") && ResourceLoader.Exists(layers + "hand_left.png"))
        {
            body.Texture = GD.Load<Texture2D>(layers + "body.png");
            // Source filenames describe the original requested side, not the painted anatomy.
            // The cupped-palm asset is the right hand; the dorsal asset is the left.
            // Keep anatomical right on viewer-left and preserve the original PNGs.
            _rightHand = AddHand(layers + "hand_left.png", "RightHand", new(-DisplayHeight * 0.28f, -DisplayHeight * 0.50f));
            _leftHand = AddHand(layers + "hand_right.png", "LeftHand", new(DisplayHeight * 0.28f, -DisplayHeight * 0.49f));
        }
        body.Scale = Vector2.One * (DisplayHeight / body.Texture.GetHeight());
        body.Position = new Vector2(0, -DisplayHeight * 0.5f);
        if (HasSeparateHands && ResourceLoader.Exists(LibrarianCharacterRig050.AtlasPath))
        {
            _rig = new LibrarianCharacterRig050 { Name = "LayeredRig050" };
            AddChild(_rig);
            _rig.Initialize(_visual, DisplayHeight);
        }
        _player = new AnimationPlayer { Name = "AnimationPlayer", RootNode = new NodePath("../..") };
        AddChild(_player);
        var library = new AnimationLibrary();
        foreach (var name in new[] { "Idle", "Relaxed", "idle", "relaxed_loop", "overgrowth_loop", "hive_loop", "glory_loop" })
            library.AddAnimation(name, Motion(5.2, true, Vector2.Zero, new Vector2(-1, -4), RestingTilt, RestingTilt + 0.008f));
        foreach (var name in new[] { "Attack", "attack", "Cast", "cast" })
            library.AddAnimation(name, Motion(1.05, false, Vector2.Zero, new Vector2(5, -2), RestingTilt, RestingTilt + 0.025f));
        foreach (var name in new[] { "Hit", "hit", "Hurt", "hurt" })
            library.AddAnimation(name, Motion(0.44, false, Vector2.Zero, new Vector2(-9, 2), RestingTilt, RestingTilt - 0.08f));
        foreach (var name in new[] { "Dead", "Die", "die" })
            library.AddAnimation(name, Motion(1.65, false, Vector2.Zero, new Vector2(-12, 30), RestingTilt, -1.12f, true));
        foreach (var name in new[] { "Revive", "revive" })
            library.AddAnimation(name, Motion(1.3, false, new Vector2(-12, 30), Vector2.Zero, -1.12f, RestingTilt, holdPeak: true));
        _player.AddAnimationLibrary("", library);
        _player.AnimationFinished += Finished;
        _player.AnimationStarted += name =>
        {
            _rig?.Begin(LibrarianCharacterRig050.StateFor(name.ToString()));
            if (name == "Dead" || name == "Die" || name == "die") { _dead = true; if (_rig is null) ResetHands(); }
            if (name == "Revive" || name == "revive") { _dead = false; if (_rig is null) ResetHands(); }
        };
        _player.Play("Idle");
    }

    private Animation Motion(double duration, bool loop, Vector2 from, Vector2 peak, float angle, float peakAngle, bool death = false, bool holdPeak = false)
    {
        var animation = new Animation { Length = (float)duration, LoopMode = loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None };
        if (_rig is not null) return animation;
        int pos = animation.AddTrack(Animation.TrackType.Value);
        animation.TrackSetPath(pos, new NodePath(VisualPath + ":position"));
        int rot = animation.AddTrack(Animation.TrackType.Value);
        animation.TrackSetPath(rot, new NodePath(VisualPath + ":rotation"));
        // Sample a smooth curve instead of three linear keys: no sharp idle
        // reversal and no mechanical stop halfway through an action.
        int samples = Math.Max(18, (int)Math.Ceiling(duration * 40));
        for (int i = 0; i <= samples; i++)
        {
            float t = (float)i / samples;
            float weight = death || holdPeak ? t * t * (3 - 2 * t) : 0.5f - 0.5f * Mathf.Cos(t * Mathf.Tau);
            animation.TrackInsertKey(pos, duration * t, from.Lerp(peak, weight));
            animation.TrackInsertKey(rot, duration * t, Mathf.Lerp(angle, peakAngle, weight));
        }
        int alpha = animation.AddTrack(Animation.TrackType.Value);
        animation.TrackSetPath(alpha, new NodePath(VisualPath + ":modulate"));
        animation.TrackInsertKey(alpha, 0, holdPeak ? new Color(0.38f, 0.42f, 0.46f, 1) : Colors.White);
        // Keep an opaque, dimmed fallen book until the native creature removal.
        animation.TrackInsertKey(alpha, duration, death ? new Color(0.38f, 0.42f, 0.46f, 1) : Colors.White);
        return animation;
    }

    private Sprite2D AddHand(string path, string name, Vector2 position)
    {
        var texture = GD.Load<Texture2D>(path);
        var hand = new Sprite2D { Name = name, Texture = texture, Position = position,
            Scale = Vector2.One * (DisplayHeight * 0.23f / texture.GetHeight()) };
        _visual.AddChild(hand);
        hand.AddChild(new LibrarianHandGlow { Name = "SpellGlow", Radius = texture.GetHeight() * 0.50f, ShowBehindParent = true });
        return hand;
    }

    private void ResetHands()
    {
        _handTween?.Kill();
        if (_rightHand is not null) { _rightHand.Position = new(-DisplayHeight * 0.28f, -DisplayHeight * 0.50f); _rightHand.Rotation = 0; _rightHand.Modulate = Colors.White; _rightHand.GetNode<LibrarianHandGlow>("SpellGlow").Clear(); }
        if (_leftHand is not null) { _leftHand.Position = new(DisplayHeight * 0.28f, -DisplayHeight * 0.49f); _leftHand.Rotation = 0; _leftHand.Modulate = Colors.White; _leftHand.GetNode<LibrarianHandGlow>("SpellGlow").Clear(); }
    }

    internal void PlayCardGesture(MegaCrit.Sts2.Core.Entities.Cards.CardType type)
    {
        if (_rig is not null) { _rig.Cast(type); return; }
        if (_dead || !HasSeparateHands) return;
        // Continue from the visible pose if a rapid second card interrupts the
        // first. Resetting to rest here created a conspicuous one-frame snap.
        _handTween?.Kill();
        _handTween = CreateTween().SetParallel();
        if (type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Attack)
            AnimateHand(_leftHand!, new(26, -9), -0.20f, new Color("ffd88a")); // screen-right attacks
        else if (type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Skill)
            AnimateHand(_rightHand!, new(-3, -21), 0.12f, new Color("b8edff")); // screen-left casts skills
        else if (type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Power)
        {
            AnimateHand(_rightHand!, new(DisplayHeight * 0.075f, -12), -0.10f, new Color("ffe9af"));
            AnimateHand(_leftHand!, new(-DisplayHeight * 0.075f, -12), 0.10f, new Color("ffe9af"));
        }
        // A hand not used by the new gesture still eases home after interruption.
        if (type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Attack) EaseHome(_rightHand!);
        if (type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Skill) EaseHome(_leftHand!);
    }

    private void AnimateHand(Sprite2D hand, Vector2 offset, float rotation, Color glow)
    {
        var origin = RestPosition(hand);
        var preparation = origin - offset * 0.15f + new Vector2(0, 2);
        hand.GetNode<LibrarianHandGlow>("SpellGlow").Pulse(glow);
        _handTween!.TweenProperty(hand, "position", preparation, 0.09).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        _handTween.TweenProperty(hand, "rotation", -rotation * 0.2f, 0.09).SetTrans(Tween.TransitionType.Sine);
        _handTween.TweenProperty(hand, "position", origin + offset, 0.16).SetDelay(0.09).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _handTween.TweenProperty(hand, "rotation", rotation, 0.16).SetDelay(0.09).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _handTween.TweenProperty(hand, "position", origin, 0.34).SetDelay(0.25).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        _handTween.TweenProperty(hand, "rotation", 0f, 0.34).SetDelay(0.25).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }

    private Vector2 RestPosition(Sprite2D hand) => ReferenceEquals(hand, _rightHand)
        ? new(-DisplayHeight * 0.28f, -DisplayHeight * 0.50f)
        : new(DisplayHeight * 0.28f, -DisplayHeight * 0.49f);

    private void EaseHome(Sprite2D hand)
    {
        _handTween!.TweenProperty(hand, "position", RestPosition(hand), 0.25).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        _handTween.TweenProperty(hand, "rotation", 0f, 0.25).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
    }

    public override void _ExitTree() => _handTween?.Kill();

    private void Finished(StringName name)
    {
        if (name != "Dead" && name != "Die" && name != "die") _player.Play("Idle");
    }
}
