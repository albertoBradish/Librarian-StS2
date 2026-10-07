using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.OrbBasics;
using Librarian.LibrarianCode.Cards.OrbAdvancedBasics;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Ftue;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Localization;

namespace Librarian.Mechanics;

/// <summary>Illustrated lessons over the actual practice combat, using the current orb UI and native card renderer.</summary>
public partial class LibrarianPracticeOverlay : NFtue
{
    internal LibrarianPracticeSession Session { get; private set; } = null!;
    internal NPopupYesNoButton ContinueButton { get; private set; } = null!;
    internal NPopupYesNoButton ExitButton { get; private set; } = null!;
    internal int Step => Session.Step;
    internal bool HasIllustration => _orbImage is not null && _preview is not null;
    private Texture2D? _orbImage;
    private NCard? _preview;
    private Control _layout = null!;
    private bool _closing;
    private string _language = "";
    private MegaRichTextLabel _title = null!, _body = null!, _state = null!, _caption = null!;
    private MegaRichTextLabel _explanation = null!, _observation = null!, _instruction = null!, _progress = null!;
    private static string T(string key) => LibrarianOnboarding.Text("practice_" + key);

    internal static LibrarianPracticeOverlay Create(LibrarianPracticeSession session)
    {
        if (NModalContainer.Instance is not { OpenModal: null } modals)
            throw new InvalidOperationException("Practice lesson needs a free native modal slot.");
        if (session.GetNodeOrNull<CanvasLayer>("PracticeHints") is null)
        {
            var hints = new CanvasLayer { Name = "PracticeHints", Layer = 100 };
            session.AddChild(hints);
            hints.AddChild(new LibrarianPracticeTaskHint { Session = session });
        }
        var overlay = new LibrarianPracticeOverlay { Name = "LibrarianPracticeLesson", Session = session };
        // Capture before adding the dark modal. The illustration contains real values,
        // positions and locks; it is never an extracted game asset or a fabricated outcome.
        overlay._orbImage = CaptureOrbs(session);
        modals.Add(overlay);
        return overlay;
    }

    private static Texture2D? CaptureOrbs(LibrarianPracticeSession session)
    {
        var display = LibrarianOrbPanel.GetDisplay(LibrarianRuntime.Get(session.Player));
        if (display is null) return null;
        var viewport = display.GetViewport();
        using var frame = viewport.GetTexture().GetImage();
        var stretch = viewport.GetStretchTransform();
        Rect2? bounds = null;
        foreach (var kind in Enum.GetValues<OrbKind>())
        {
            var slot = display.GetNode<Control>(kind.ToString());
            var transform = stretch * slot.GetGlobalTransformWithCanvas();
            var box = Box(transform, new Rect2(Vector2.Zero, slot.Size));
            bounds = bounds is null ? box : bounds.Value.Merge(box);
        }
        if (bounds is null) return null;
        var area = bounds.Value.Grow(40 * Math.Max(.1f, stretch.Scale.X));
        var clipped = area.Intersection(new Rect2(Vector2.Zero, new Vector2(frame.GetWidth(), frame.GetHeight())));
        var crop = new Rect2I((Vector2I)clipped.Position, (Vector2I)clipped.Size);
        if (crop.Size.X < 8 || crop.Size.Y < 8) return null;
        using var image = frame.GetRegion(crop);
        return ImageTexture.CreateFromImage(image);
    }

    internal static Rect2 Box(Transform2D transform, Rect2 rectangle)
    {
        Vector2[] points = [rectangle.Position, rectangle.Position + new Vector2(rectangle.Size.X, 0),
            rectangle.End, rectangle.Position + new Vector2(0, rectangle.Size.Y)];
        var first = transform * points[0];
        var result = new Rect2(first, Vector2.Zero);
        foreach (var point in points.Skip(1)) result = result.Expand(transform * point);
        return result;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        _layout = new Control { Name = "IllustratedLesson", Size = new Vector2(1920, 1080), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_layout);
        Backdrop(_layout, "LessonPanel", new(80, 88), new(1760, 904));
        _title = Rich(_layout, new(128, 118), new(1330, 76), 46);
        _title.Name = "LessonTitle";
        _progress = Rich(_layout, new(1500, 135), new(292, 48), 27);
        _progress.Name = "LessonProgress";
        _progress.HorizontalAlignment = HorizontalAlignment.Right;
        Divider(_layout, new(128, 210), new(1664, 1));
        Divider(_layout, new(726, 234), new(1, 572));
        Divider(_layout, new(1164, 234), new(1, 572));
        _explanation = Rich(_layout, new(128, 232), new(570, 44), 27);
        _body = Rich(_layout, new(128, 292), new(570, 514), 32);
        _body.Name = "LessonBody";
        _observation = Rich(_layout, new(1192, 232), new(600, 44), 27);
        _state = Rich(_layout, new(1192, 292), new(600, 514), 32);
        _state.Name = "LessonState";
        _caption = Rich(_layout, new(754, 232), new(382, 44), 27);
        _caption.HorizontalAlignment = HorizontalAlignment.Center;
        foreach (var heading in new[] { _explanation, _observation, _caption })
            heading.AddThemeColorOverride("default_color", new Color("eba466"));
        if (_orbImage is not null)
            _layout.AddChild(new TextureRect { Name = "ActualOrbState", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                Texture = _orbImage, Position = new(760, 290), Size = new(370, 185),
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore });
        var model = Session.CurrentCard ?? ReferenceCard(Step);
        // This standalone preview belongs to the overlay, outside the hand's node pool.
        // Freeing a parent modal must never leave a deleted card in that pool.
        _preview = GD.Load<PackedScene>("res://scenes/cards/card.tscn").Instantiate<NCard>();
        _preview.OnInstantiated();
        _preview.Model = model.IsMutable ? (CardModel)model.ClonePreservingMutability() : model.ToMutable();
        if (_preview is not null)
        {
            _preview.Name = "TeachingCard";
            _layout.AddChild(_preview);
            _preview.Position = new(945, 662); _preview.Scale = Vector2.One * .75f;
            _preview.MouseFilter = MouseFilterEnum.Ignore;
            _preview.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
        }
        Divider(_layout, new(128, 834), new(1664, 1));
        _instruction = Rich(_layout, new(414, 862), new(950, 84), 28);
        _instruction.Name = "LessonInstruction";
        _instruction.VerticalAlignment = VerticalAlignment.Center;
        ContinueButton = Button(_layout, true, "ContinueButton", new(1410, 858), new(350, 88), Continue);
        ExitButton = Button(_layout, false, "ExitButton", new(128, 858), new(250, 88), Exit);
        ContinueButton.FocusNeighborLeft = ContinueButton.FocusPrevious = ContinueButton.GetPathTo(ExitButton);
        ExitButton.FocusNeighborRight = ExitButton.FocusNext = ExitButton.GetPathTo(ContinueButton);
        RefreshText();
        Callable.From(() => { Unbind(ContinueButton); Unbind(ExitButton); ContinueButton.GrabFocus(); }).CallDeferred();
    }

    internal static void Backdrop(Node parent, string name, Vector2 position, Vector2 size)
    {
        var panel = new Panel { Name = name, Position = position, Size = size, MouseFilter = MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat {
            BgColor = new Color(.055f, .065f, .085f, .97f), BorderColor = new Color(.7f, .48f, .28f, .65f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14,
            ShadowColor = new Color(0, 0, 0, .5f), ShadowSize = 12 });
        parent.AddChild(panel);
    }

    private static void Divider(Node parent, Vector2 position, Vector2 size)
        => parent.AddChild(new ColorRect { Position = position, Size = size,
            Color = new Color(.7f, .48f, .28f, .3f), MouseFilter = MouseFilterEnum.Ignore });

    internal static MegaRichTextLabel Rich(Node parent, Vector2 position, Vector2 size, int fontSize)
    {
        // Copy the native popup's font/theme properties without its fixed popup layout.
        var template = GD.Load<PackedScene>("res://scenes/ui/vertical_popup.tscn").Instantiate<NVerticalPopup>();
        var source = template.GetNode<MegaRichTextLabel>("Description");
        var label = new MegaRichTextLabel { Position = position, Size = size,
            MouseFilter = MouseFilterEnum.Ignore, BbcodeEnabled = true, ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, IsHorizontallyBound = true, IsVerticallyBound = true,
            MinFontSize = Math.Max(22, fontSize - 4), MaxFontSize = fontSize, ClipContents = true };
        foreach (string font in new[] { "normal_font", "bold_font", "italics_font", "bold_italics_font" })
            label.AddThemeFontOverride(font, source.GetThemeFont(font));
        foreach (string font in new[] { "normal_font_size", "bold_font_size", "italics_font_size", "bold_italics_font_size" })
            label.AddThemeFontSizeOverride(font, fontSize);
        label.AddThemeColorOverride("default_color", new Color("eedfc5"));
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 2);
        label.AddThemeConstantOverride("line_separation", 4);
        template.Free();
        parent.AddChild(label);
        return label;
    }

    internal static NPopupYesNoButton Button(Node parent, bool yes, string name, Vector2 position, Vector2 size, Action action)
    {
        var button = GD.Load<PackedScene>(yes ? "res://scenes/ui/abandon_run_yes_button.tscn" : "res://scenes/ui/abandon_run_no_button.tscn").Instantiate<NPopupYesNoButton>();
        button.Name = name;
        foreach (string path in new[] { "%Image", "%Outline" })
            if (button.GetNodeOrNull<CanvasItem>(path)?.Material is { } material)
                button.GetNode<CanvasItem>(path).Material = (Material)material.Duplicate();
        parent.AddChild(button);
        button.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        button.Position = position; button.Size = size; button.IsYes = yes; button.Show(); button.Enable();
        Unbind(button);
        button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => action()));
        return button;
    }

    private static void Unbind(NPopupYesNoButton button)
    {
        button.DisconnectHotkeys(); button.FocusMode = FocusModeEnum.All;
        foreach (string path in new[] { "%ControllerIcon", "%HotkeyIcon" })
            if (button.GetNodeOrNull<CanvasItem>(path) is { } icon) { icon.Modulate = new Color(1, 1, 1, 0); icon.Hide(); }
    }

    private static CardModel ReferenceCard(int step) => step switch
    {
        0 or 1 or 6 => ModelDb.Card<Spark>(),
        2 or 3 => ModelDb.Card<Trickle>(),
        4 or 5 => ModelDb.Card<Springwater>(),
        7 or 8 or 9 => ModelDb.Card<TidalGravity>(),
        _ => ModelDb.Card<Renewal>()
    };

    internal void RefreshText()
    {
        _language = LibrarianLanguage.Selected;
        _title.SetTextAutoSize(T("step" + Step + "_title"));
        var body = new LocString("main_menu_ui", "LIBRARIAN_TUTORIAL.practice_step" + Step);
        body.Add("GrowthGain", ModelDb.Card<Renewal>().DynamicVars["Growth"].BaseValue);
        _body.SetTextAutoSize(body.GetFormattedText());
        var session = LibrarianRuntime.Get(Session.Player);
        var result = new LocString("main_menu_ui", "LIBRARIAN_TUTORIAL.practice_step" + Step + "_result");
        string State(OrbKind kind) => LibrarianOnboarding.Text("practice_" + (session.Orbs.IsLocked(kind) ? "locked" : session.Orbs.IsActivated(kind) ? "active" : "inactive"));
        foreach (var kind in Enum.GetValues<OrbKind>())
        {
            string key = kind.ToString();
            result.Add(key, session.Orbs.Value(kind));
            result.Add(key + "Half", session.Orbs.Value(kind) / 2);
            result.Add(key + "State", State(kind));
            result.Add(key + "Locks", session.Orbs.LockedTurns(kind));
        }
        result.Add("Waves", session.Waves.Amount);
        result.Add("Turn", Session.Player.PlayerCombatState!.TurnNumber);
        _state.SetTextAutoSize(result.GetFormattedText());
        _caption.SetTextAutoSize(T("actual_state"));
        _explanation.SetTextAutoSize(T("explanation"));
        _observation.SetTextAutoSize(T("observation"));
        _instruction.SetTextAutoSize(T("task" + Step));
        var progress = new LocString("main_menu_ui", "LIBRARIAN_TUTORIAL.practice_progress");
        progress.Add("Current", Step + 1); progress.Add("Total", 12);
        _progress.SetTextAutoSize(progress.GetFormattedText());
        ContinueButton.SetText(T(Step == 11 ? "finish" : "try"));
        ExitButton.SetText(T("exit"));
    }

    internal void Continue()
    {
        if (_closing || !ReferenceEquals(NModalContainer.Instance?.OpenModal, this)) return;
        _closing = true;
        TreeExited += () => Callable.From(Session.Continue).CallDeferred();
        CloseFtue();
    }

    internal void Exit()
    {
        if (_closing) return;
        _closing = true;
        // The exit owns modal cleanup and waits for combat actions. Starting it now
        // also handles a user leaving during an overlay's first frame.
        TaskHelper.RunSafely(Session.ExitAsync());
    }

    public override void _Input(InputEvent input)
    {
        if (_closing || !ReferenceEquals(NModalContainer.Instance?.OpenModal, this)) return;
        var key = input as InputEventKey;
        Key code = key is null ? Key.None : key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        bool Action(string name) => InputMap.HasAction(name) && input.IsAction(name);
        bool select = Action(MegaInput.select) || Action(MegaInput.accept) || code is Key.Enter or Key.KpEnter or Key.Space;
        bool cancel = Action(MegaInput.cancel) || Action(MegaInput.pauseAndBack) || Action(MegaInput.back) || code == Key.Escape;
        bool direction = Action(MegaInput.left) || Action(MegaInput.right) || code is Key.Left or Key.Right;
        if (!select && !cancel && !direction) return;
        GetViewport().SetInputAsHandled();
        if (input.IsEcho()) return;
        bool released = key is { Pressed: false } || input.IsActionReleased(MegaInput.select)
            || input.IsActionReleased(MegaInput.accept) || input.IsActionReleased(MegaInput.cancel)
            || input.IsActionReleased(MegaInput.pauseAndBack) || input.IsActionReleased(MegaInput.back);
        if (released && cancel) Exit();
        else if (released && select) { if (ReferenceEquals(GetViewport().GuiGetFocusOwner(), ExitButton)) Exit(); else Continue(); }
        else if (direction && (key is { Pressed: true } || input.IsActionPressed(MegaInput.left) || input.IsActionPressed(MegaInput.right)))
            (ReferenceEquals(GetViewport().GuiGetFocusOwner(), ContinueButton) ? ExitButton : ContinueButton).GrabFocus();
    }

    public override void _Process(double delta)
    {
        float scale = Math.Min(Size.X / 1920, Size.Y / 1080);
        _layout.Scale = Vector2.One * scale;
        _layout.Position = (Size - new Vector2(1920, 1080) * scale) * .5f;
        if (_language != LibrarianLanguage.Selected) RefreshText();
    }
}

/// <summary>During the player's operation, use the target card's native gold glow and leave combat unobstructed.</summary>
public partial class LibrarianPracticeTaskHint : Control
{
    internal LibrarianPracticeSession Session { get; set; } = null!;
    private MegaRichTextLabel _task = null!;
    private Control _layout = null!;
    private NPopupYesNoButton _exit = null!;
    private int _step = -1;
    private string _language = "";
    private NCard? _guidedCard;
    private NCardHolder? _guidedHolder;
    private CardModel? _guidedModel;
    private NCardHighlight? _guidedHighlight;
    private Color _previousGlowColor;
    private ShaderMaterial? _glowMaterial;
    private float _previousGlowWidth;
    internal NCard? GuidedCardNode => _guidedCard;
    internal NCardHighlight? GuidedNativeHighlight => _guidedHighlight;
    internal bool NativeGoldGuidanceActive => _guidedHighlight is not null
        && GodotObject.IsInstanceValid(_guidedHighlight) && _guidedHighlight.Modulate == NCardHighlight.gold;
    internal float NativeGoldGuidanceWidth => _guidedHighlight is not null
        && GodotObject.IsInstanceValid(_guidedHighlight) && _guidedHighlight.Material is ShaderMaterial shader
        ? shader.GetShaderParameter("width").AsSingle() : 0;
    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _layout = new Control { Name = "TaskLayout", Size = new(1920, 1080), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_layout);
        LibrarianPracticeOverlay.Backdrop(_layout, "TaskPanel", new(180, 100), new(1560, 108));
        _task = LibrarianPracticeOverlay.Rich(_layout, new(222, 121), new(1180, 70), 28);
        _task.Name = "TaskText";
        _task.VerticalAlignment = VerticalAlignment.Center;
        _exit = LibrarianPracticeOverlay.Button(_layout, false, "LeavePractice", new(1460, 116), new(250, 78),
            () => TaskHelper.RunSafely(Session.ExitAsync()));
        _exit.SetText(LibrarianOnboarding.Text("practice_exit"));
    }
    public override void _Process(double delta)
    {
        Visible = Session.AwaitingAction && NModalContainer.Instance?.OpenModal is null;
        float scale = Math.Min(Size.X / 1920, Size.Y / 1080);
        _layout.Scale = Vector2.One * scale;
        _layout.Position = (Size - new Vector2(1920, 1080) * scale) * .5f;
        UpdateNativeCardGuidance();
        if (!Visible) return;
        if (_step != Session.Step || _language != LibrarianLanguage.Selected)
        {
            _step = Session.Step;
            _language = LibrarianLanguage.Selected;
            _task.SetTextAutoSize(LibrarianOnboarding.Text("practice_task" + _step));
            _exit.SetText(LibrarianOnboarding.Text("practice_exit"));
        }
        QueueRedraw();
    }
    private void UpdateNativeCardGuidance()
    {
        // This control exists only under the isolated practice session. Never change
        // hand preferences, card rules, or highlights in an ordinary run.
        var model = Visible && !Session.GuidanceActionPending
            && ReferenceEquals(LibrarianPracticeSession.Instance, Session) ? Session.CurrentCard : null;
        var holder = model?.Pile?.Type == PileType.Hand ? NPlayerHand.Instance?.GetCardHolder(model) : null;
        var card = holder?.CardNode;
        if (card is not null && (!GodotObject.IsInstanceValid(card) || !card.IsNodeReady())) card = null;
        if (!ReferenceEquals(card, _guidedCard) || !ReferenceEquals(model, _guidedModel))
        {
            RestoreNativeCardGuidance();
            if (card?.CardHighlight is not { } highlight || !GodotObject.IsInstanceValid(highlight)) return;
            _guidedCard = card;
            _guidedHolder = holder;
            _guidedModel = model;
            _guidedHighlight = highlight;
            _previousGlowColor = highlight.Modulate;
            _glowMaterial = highlight.Material as ShaderMaterial;
            _previousGlowWidth = _glowMaterial?.GetShaderParameter("width").AsSingle() ?? 0;
            // The same control, shader animation, and gold color used by the game's
            // NHandCardHolder.UpdateCard; the border follows the card while dragging.
            highlight.Modulate = NCardHighlight.gold;
            highlight.AnimShow();
        }
        if (_guidedHighlight is not null && GodotObject.IsInstanceValid(_guidedHighlight))
            _guidedHighlight.Modulate = NCardHighlight.gold;
    }
    internal void RestoreNativeCardGuidance()
    {
        var card = _guidedCard;
        var holder = _guidedHolder;
        var model = _guidedModel;
        var highlight = _guidedHighlight;
        var material = _glowMaterial;
        _guidedCard = null;
        _guidedHolder = null;
        _guidedModel = null;
        _guidedHighlight = null;
        _glowMaterial = null;
        // NCard is pooled. A recycled node belongs to its new model and must not be
        // touched by a delayed release from this lesson.
        if (card is null || highlight is null || !GodotObject.IsInstanceValid(card)
            || !GodotObject.IsInstanceValid(highlight) || !ReferenceEquals(card.Model, model)) return;
        highlight.AnimHideInstantly();
        highlight.Modulate = _previousGlowColor;
        if (model?.Pile?.Type != PileType.Hand) return;
        if (material is not null && GodotObject.IsInstanceValid(material)
            && ReferenceEquals(highlight.Material, material))
            material.SetShaderParameter("width", _previousGlowWidth);
        if (holder is NHandCardHolder handHolder && GodotObject.IsInstanceValid(handHolder)
            && handHolder.IsInsideTree() && ReferenceEquals(handHolder.CardNode, card))
            handHolder.UpdateCard();
    }
    public override void _ExitTree() => RestoreNativeCardGuidance();
    public override void _Draw()
    {
        if (!Visible || Session.CurrentCard is not null || !Session.AllowEndTurn) return;
        Control? target = FindEndTurn(NCombatRoom.Instance);
        if (target is null) return;
        var box = LibrarianPracticeOverlay.Box(GetGlobalTransformWithCanvas().AffineInverse() * target.GetGlobalTransformWithCanvas(), new Rect2(Vector2.Zero, target.Size)).Grow(8);
        var color = new Color("efc75e");
        DrawRect(box, color, false, 4);
        var tip = new Vector2(box.GetCenter().X, box.Position.Y - 12);
        DrawLine(tip - new Vector2(0, 55), tip, color, 5);
        DrawColoredPolygon([tip, tip + new Vector2(-14, -22), tip + new Vector2(14, -22)], color);
    }
    private static Control? FindEndTurn(Node? node)
    {
        if (node is MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton button) return button;
        if (node is null) return null;
        foreach (var child in node.GetChildren()) if (FindEndTurn(child) is { } found) return found;
        return null;
    }
}
