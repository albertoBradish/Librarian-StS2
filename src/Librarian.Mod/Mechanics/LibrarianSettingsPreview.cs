using Godot;

namespace Librarian.Mechanics;

/// <summary>On-demand local examples. Never reads or changes combat state.</summary>
public partial class LibrarianSettingsPreview : VBoxContainer
{
    internal static readonly string[] Keys = ["compact_hover_tips", "circle", "idle", "locked_orb_display", "tide_block_feedback", "wave", "card_effects", "orb_effects", "particles", "reduced_motion", "opacity", "limit"];
    private string _key = "";
    private VBoxContainer? _details;
    private HBoxContainer? _actions;
    private Button? _expand;
    private Button? _play;
    private LibrarianSettingsPreviewImage? _image;
    internal bool Expanded => _details?.Visible == true;
    internal bool Playing => _image?.Playing == true;
    internal bool Loaded => _image?.Loaded == true;
    internal string SettingKey => _key;
    private static string Text(string key) => LibrarianLanguage.Text("main_menu_ui", "LIBRARIAN_SETTINGS." + key);

    internal static Control Create(string key) => new LibrarianSettingsPreview
    {
        Name = "LibrarianPreview_" + key, _key = key,
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
        MouseFilter = MouseFilterEnum.Pass
    };

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 8);
        _actions = new HBoxContainer { Name = "PreviewActions", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _actions.AddThemeConstantOverride("separation", 8);
        AddChild(_actions);
        _expand = MakeButton("ExpandPreview", Text("preview_open"));
        _expand.ToggleMode = true;
        _expand.TooltipText = Text(_key);
        _actions.AddChild(_expand);
        _details = new VBoxContainer { Name = "PreviewDetails", Visible = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _details.AddThemeConstantOverride("separation", 8);
        AddChild(_details);
        _expand.Toggled += Open;
    }

    internal void Open(bool expanded)
    {
        if (_details is null || _expand is null) return;
        if (expanded && _image is null)
        {
            _image = new LibrarianSettingsPreviewImage { Name = "PreviewImage", CustomMinimumSize = new(0, 220), SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
            _image.Load(_key);
            _details.AddChild(_image);
            var caption = new Label { Name = "PreviewCaption", Text = Text("preview_" + _key), AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
            caption.AddThemeFontSizeOverride("font_size", 20);
            _details.AddChild(caption);
            if (_image.Animated)
            {
                _play = MakeButton("PlayPreview", Text("preview_play"));
                _play.Pressed += () => { _image.SetPlaying(!_image.Playing); _play.Text = Text(_image.Playing ? "preview_pause" : "preview_play"); };
                _actions!.AddChild(_play);
            }
        }
        _details.Visible = expanded;
        _expand.SetPressedNoSignal(expanded);
        _expand.Text = Text(expanded ? "preview_close" : "preview_open");
        if (_play is not null) _play.Visible = expanded;
        if (!expanded)
        {
            _image?.SetPlaying(false);
            if (_play is not null) _play.Text = Text("preview_play");
        }
        for (Node? ancestor = this; ancestor is not null; ancestor = ancestor.GetParent())
            if (ancestor is STS2RitsuLib.Ui.Layout.RitsuFixedWidthScrollContent scrollContent)
                scrollContent.RequestLayout();
    }

    private static Button MakeButton(string name, string text)
    {
        var button = new Button { Name = name, Text = text, CustomMinimumSize = new(176, 34), SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        button.AddThemeFontSizeOverride("font_size", 20);
        foreach (var (state, color) in new[] { ("normal", "233447"), ("hover", "31516c"), ("pressed", "294962"), ("focus", "31516c") })
        {
            var style = new StyleBoxFlat { BgColor = new Color(color), BorderColor = new Color("658092"), ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 5, ContentMarginBottom = 5 };
            style.SetBorderWidthAll(1);
            button.AddThemeStyleboxOverride(state, style);
        }
        return button;
    }
}

public partial class LibrarianSettingsPreviewImage : Control
{
    private Texture2D? _still;
    private Texture2D? _atlas;
    private double _clock;
    private int _frame;
    internal bool Playing { get; private set; }
    internal bool Loaded => _still is not null;
    internal bool Animated => _atlas is not null;
    internal int Frame => _frame;
    internal void Load(string key)
    {
        string root = "res://Librarian/images/settings/" + key;
        if (ResourceLoader.Exists(root + "-compare.png")) _still = ResourceLoader.Load<Texture2D>(root + "-compare.png");
        if (ResourceLoader.Exists(root + "-atlas.webp")) _atlas = ResourceLoader.Load<Texture2D>(root + "-atlas.webp");
        Resized += QueueRedraw;
        SetProcess(false);
    }
    internal void SetPlaying(bool playing)
    {
        Playing = playing && Animated;
        _clock = 0;
        _frame = 0;
        SetProcess(Playing);
        QueueRedraw();
    }
    public override void _Process(double delta)
    {
        if (!Playing || !IsVisibleInTree()) return;
        _clock += delta;
        int frame = (int)(_clock * 8) % 24;
        if (frame != _frame) { _frame = frame; QueueRedraw(); }
    }
    public override void _Draw()
    {
        var texture = Playing ? _atlas : _still;
        if (texture is null) return;
        Vector2 sourceSize = Playing ? new Vector2(480, 240) : texture.GetSize();
        float scale = Mathf.Min(Size.X / sourceSize.X, Size.Y / sourceSize.Y);
        var target = new Rect2((Size - sourceSize * scale) / 2, sourceSize * scale);
        if (Playing) DrawTextureRectRegion(texture, target, new Rect2(new Vector2(_frame % 6 * 480, _frame / 6 * 240), sourceSize));
        else DrawTextureRect(texture, target, false);
    }
}
