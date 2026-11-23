using Godot;
using MegaCrit.Sts2.Core.Combat;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Entities.Players;

namespace Librarian.Mechanics;

/// <summary>An independent blue band uses the native Doom texture and HP scale, without touching lethal logic.</summary>
public partial class LibrarianWaveBar : Control
{
    private const double ForecastInterval = 1.0 / 12;
    private LibrarianSession? _session;
    private Player? _player;
    private Control? _hp;
    private Control? _block;
    private NinePatchRect? _fill;
    private Label? _label;
    private float _width;
    private double _refreshIn;
    private LibrarianBlockPreview? _preview;
    private (Rect2 Hp, float LabelX, float LabelWidth, float FillWidth, bool ShowFill)? _layout;
    private bool _hovered;
    private string? _hoverText;

    internal void Initialize(NHealthBar healthBar, Player player)
    {
        _player = player;
        _hp = healthBar.GetNode<Control>("%HpForegroundContainer");
        _block = healthBar.GetNode<Control>("%BlockContainer");
        var native = healthBar.GetNode<NinePatchRect>("%DoomForeground");
        _fill = new NinePatchRect
        {
            Name = "WaveFill",
            Texture = native.Texture, RegionRect = native.RegionRect,
            PatchMarginLeft = native.PatchMarginLeft, PatchMarginRight = native.PatchMarginRight,
            PatchMarginTop = native.PatchMarginTop, PatchMarginBottom = native.PatchMarginBottom,
            Modulate = new Color("63dded"), MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_fill);
        _label = new Label { Name = "WaveAmount", Size = new(220, 32), MouseFilter = MouseFilterEnum.Ignore };
        _label.AddThemeFontSizeOverride("font_size", 24);
        _label.AddThemeColorOverride("font_color", new Color("8beefa"));
        _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _label.AddThemeConstantOverride("outline_size", 4);
        AddChild(_label);
        Visible = false;
        MouseEntered += () => { _hovered = true; RefreshHover(); };
        MouseExited += () => { _hovered = false; _hoverText = null; NHoverTipSet.Remove(this); };
    }

    private void RefreshHover()
    {
        if (!_hovered || _preview is null || !Visible) return;
        string text = _preview.Description;
        if (text == _hoverText) return;
        _hoverText = text;
        NHoverTipSet.Remove(this);
        NHoverTipSet.CreateAndShow(this,
            [new HoverTip(new MegaCrit.Sts2.Core.Localization.LocString("librarian_runtime", "LIBRARIAN_BLOCK_PREVIEW_TITLE"),
                text, null)], HoverTip.GetHoverTipAlignment(this))?.SetFollowOwner();
    }

    public override void _Process(double delta)
    {
        if (_player is null || _hp is null || _block is null || _fill is null || _label is null) return;
        var preferences = LibrarianPreferences050.Current;
        // Creature UI can be built just before the new PlayerCombatState replaces the old one.
        if (!preferences.WaveBar || !_hp.IsVisibleInTree() || _player.Creature.IsDead
            || !LibrarianRuntime.TryGet(_player, out var current) || current is null
            || current.ResolvingEndTurn || current.Orbs.EndTurnResolved
            || _player.PlayerCombatState?.Phase != PlayerTurnPhase.Play)
        {
            HideForecast();
            _preview = null;
            _width = 0;
            return;
        }
        if (_session != current)
        {
            _session = current;
            _preview = null;
        }

        // The projection enumerates native hooks and copies combat state. Sample live
        // inputs at a bounded rate so changes from other mods do not need invalidation hooks.
        _refreshIn -= delta;
        bool refreshed = _preview is null || _refreshIn <= 0;
        if (refreshed)
        {
            _preview = LibrarianEndTurnPreview.Read(current);
            _refreshIn = ForecastInterval;
        }
        var preview = _preview!;
        int amount = preview.Maximum ?? 0;
        if (preview.Maximum == 0 && preview.Minimum == 0 && preview.ExpiringBlock <= 0)
        {
            HideForecast();
            return;
        }
        if (refreshed)
        {
            var text = preview.AmountText;
            if (_label.Text != text) _label.Text = text;
        }
        Visible = true;
        // Keep every edge in this parent's space; creature UI and the native HP foreground
        // may have different scales. The native shield draws over this band, so start at
        // the HP edge and let the shield mask the overlap instead of reserving a visible gap.
        var toLocal = GetParent<CanvasItem>().GetGlobalTransform().AffineInverse();
        var hpBounds = BoundsInParent(_hp, toLocal);
        float available = Math.Max(1, hpBounds.Size.X);
        float target = available * Math.Clamp((float)amount / Math.Max(1, _session.Player.Creature.MaxHp), 0, 1);
        _width = preferences.ReducedMotion || Mathf.IsEqualApprox(_width, target) ? target
            : Mathf.Lerp(Math.Min(_width, available), target, Math.Min(1f, (float)delta * 12));
        // A small blue superscript to the upper right of the native Block number.
        // A hidden BlockContainer can retain an earlier animated position; it must not
        // shift the amount when there is no native shield on screen.
        float labelX = _block.IsVisibleInTree() ? BoundsInParent(_block, toLocal).End.X - 9 : hpBounds.Position.X;
        float labelWidth = _label.GetMinimumSize().X;
        float fillWidth = Math.Max(1, _width);
        bool showFill = preview.Maximum.HasValue && amount > 0;
        var layout = (hpBounds, labelX, labelWidth, fillWidth, showFill);
        if (_layout != layout)
        {
            _layout = layout;
            _fill.Visible = showFill;
            _fill.Size = new(fillWidth, 5);
            float left = Math.Min(labelX, hpBounds.Position.X);
            Position = new(left, hpBounds.Position.Y - 35);
            _fill.Position = new(hpBounds.Position.X - left, 28);
            _label.Position = new(labelX - left, 0);
            Size = new(Math.Max(hpBounds.End.X - left, labelX - left + labelWidth), 33);
        }
        if (refreshed) RefreshHover();
    }

    private void HideForecast()
    {
        Visible = false;
        if (_hoverText is null) return;
        _hoverText = null;
        NHoverTipSet.Remove(this);
    }

    private static Rect2 BoundsInParent(Control control, Transform2D toLocal)
    {
        var transform = toLocal * control.GetGlobalTransform();
        var bounds = new Rect2(transform * Vector2.Zero, Vector2.Zero);
        bounds = bounds.Expand(transform * new Vector2(control.Size.X, 0));
        bounds = bounds.Expand(transform * new Vector2(0, control.Size.Y));
        return bounds.Expand(transform * control.Size);
    }

    public override void _ExitTree() => NHoverTipSet.Remove(this);
}

[HarmonyPatch(typeof(NHealthBar), nameof(NHealthBar.SetCreature))]
internal static class AddLibrarianWaveBar
{
    [HarmonyPostfix] private static void Postfix(NHealthBar __instance, Creature creature)
    {
        if (creature.Player is not { } player) return;
        // The party HUD also owns NHealthBars. Only a battlefield creature may
        // receive this presentation; do not duplicate it in the upper-left HUD.
        var combatNode = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (combatNode is null || !combatNode.IsAncestorOf(__instance)) return;
        var parent = __instance.GetNode<Control>("HpBarContainer");
        if (parent.GetNodeOrNull("LibrarianWaves") is not null) return;
        var band = new LibrarianWaveBar { Name = "LibrarianWaves", ZIndex = 0, MouseFilter = Control.MouseFilterEnum.Pass };
        parent.AddChild(band);
        // Native text draws after the band, independent of slash glyphs or outline overlap.
        parent.MoveChild(band, parent.GetNode<Label>("HpLabel").GetIndex());
        band.Initialize(__instance, player);
    }
}
