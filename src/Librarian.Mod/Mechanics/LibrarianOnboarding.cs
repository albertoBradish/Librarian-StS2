using Godot;
using HarmonyLib;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Ftue;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Runs;

namespace Librarian.Mechanics;

/// <summary>Character-local tutorial receipts in the native, profile-scoped progress file.</summary>
internal static class LibrarianOnboarding
{
    internal const string Decision = "librarian_tutorial_choice_v1";
    internal const string Accepted = "librarian_tutorial_accepted_v1";
    internal const string Completed = "librarian_tutorial_completed_v1";
    internal const string CompactDecision = "librarian_compact_hover_choice_v1";
    internal static bool Has(string marker) => SaveManager.Instance.Progress.FtueCompleted.Contains(marker);
    internal static string Text(string key) => LibrarianLanguage.Text("main_menu_ui", "LIBRARIAN_TUTORIAL." + key);
    internal static bool NeedsTutorial(int wins, int losses, bool decided, double playtime = 0)
        => wins == 0 && losses == 0 && playtime <= 0 && !decided;
    internal static bool NeedsCompact(int wins, bool decided) => wins > 0 && !decided;

    internal static void Record(string marker)
    {
        // SeenFtue also returns true when global tutorials are disabled. This character's
        // choices deliberately inspect the receipt directly and leave that switch alone.
        if (!Has(marker)) SaveManager.Instance.MarkFtueAsComplete(marker);
    }

    internal static LibrarianTutorialModal? Show(LibrarianTutorialMode mode, Action<bool>? choice = null)
    {
        var container = NModalContainer.Instance;
        if (container is null || container.OpenModal is not null) return null;
        var modal = new LibrarianTutorialModal { Name = "LibrarianTutorial", Mode = mode, Choice = choice };
        container.Add(modal);
        MainFile.Logger.Info("LIBRARIAN_TUTORIAL_SHOWN mode=" + mode);
        return modal;
    }

    internal static void StartPracticeFromMenu()
    {
        if (NGame.Instance?.MainMenu is null || RunManager.Instance.IsInProgress || LibrarianPracticeSession.Active)
        {
            if (NModalContainer.Instance is { OpenModal: null } modals)
            {
                var popup = NGenericPopup.Create();
                if (popup is not null)
                {
                    modals.Add(popup);
                    var panel = popup.GetNode<NVerticalPopup>("VerticalPopup");
                    panel.SetText(Text("title"), Text("menu_required"));
                    panel.HideNoButton();
                    panel.InitYesButton(new MegaCrit.Sts2.Core.Localization.LocString("main_menu_ui", "LIBRARIAN_TUTORIAL.done"), _ => { });
                }
            }
            return;
        }
        Record(Decision);
        Record(Accepted);
        TaskHelper.RunSafely(LibrarianPracticeSession.StartAsync());
    }
}

public enum LibrarianTutorialMode { Consent, CompactOffer }

/// <summary>Uses native FTUE input isolation, popup scene, font and buttons; no BaseLib UI.</summary>
public partial class LibrarianTutorialModal : NFtue
{
    internal LibrarianTutorialMode Mode { get; set; }
    internal Action<bool>? Choice { get; set; }
    internal NVerticalPopup Panel { get; private set; } = null!;
    internal int Page { get; private set; }
    internal double Elapsed { get; private set; }
    internal bool SkipReady => Mode != LibrarianTutorialMode.Consent || Elapsed >= 5;
    private bool _finished;
    private int _lastSeconds = -1;
    private readonly string[] _cancelKeys = [MegaInput.cancel, MegaInput.pauseAndBack, MegaInput.back];
    // beta 0.111.0 removed MegaInput.accept; retain the optional Godot action by name.
    private readonly string[] _selectKeys = [MegaInput.select, "ui_accept"];

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Panel = GD.Load<PackedScene>("res://scenes/ui/vertical_popup.tscn").Instantiate<NVerticalPopup>();
        Panel.Name = "VerticalPopup";
        // Native buttons animate their materials. Give each instance its own copy.
        foreach (var name in new[] { "YesButton", "NoButton" })
            foreach (var path in new[] { "%Image", "%Outline" })
                if (Panel.GetNodeOrNull<CanvasItem>(name + "/" + path)?.Material is { } material)
                    Panel.GetNode<CanvasItem>(name + "/" + path).Material = (Material)material.Duplicate();
        AddChild(Panel);
        float width = 840;
        Panel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        Panel.OffsetLeft = -width / 2; Panel.OffsetRight = width / 2;
        Panel.OffsetTop = -360; Panel.OffsetBottom = 360;
        Panel.Set("stretch_mode", (int)TextureRect.StretchModeEnum.Scale);
        var body = Panel.GetNode<MegaCrit.Sts2.addons.mega_text.MegaRichTextLabel>("Description");
        body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.IsHorizontallyBound = true;
        body.IsVerticallyBound = true;
        body.HorizontalAlignment = HorizontalAlignment.Left;
        body.VerticalAlignment = VerticalAlignment.Top;
        body.MinFontSize = 28; body.MaxFontSize = 34;
        body.AddThemeConstantOverride("line_separation", 4);
        body.ClipContents = true;
        body.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        body.OffsetLeft = 56; body.OffsetRight = -56;
        body.OffsetTop = 142; body.OffsetBottom = -150;
        var header = Panel.GetNode<Control>("Header");
        header.AnchorLeft = 0; header.AnchorRight = 1;
        header.OffsetLeft = 30; header.OffsetRight = -30;
        Panel.YesButton.IsYes = true; Panel.NoButton.IsYes = false;
        Panel.YesButton.Show(); Panel.NoButton.Show();
        NPopupYesNoButton[] buttons = [Panel.NoButton, Panel.YesButton];
        for (int i = 0; i < buttons.Length; i++)
        {
            var button = buttons[i];
            button.AnchorLeft = button.AnchorRight = i;
            button.AnchorTop = button.AnchorBottom = 1;
            button.OffsetLeft = i == 0 ? 56 : -296;
            button.OffsetRight = button.OffsetLeft + 240;
            button.OffsetTop = -138; button.OffsetBottom = -62;
            button.FocusMode = FocusModeEnum.All;
            button.FocusNeighborLeft = button.FocusNeighborRight = button.GetPathTo(buttons[1 - i]);
            button.FocusPrevious = button.FocusNext = button.FocusNeighborLeft;
        }
        // Do not use InitYes/No: they automatically Clear after Released, including
        // a blocked synthetic signal, and would destroy a lesson opened by a callback.
        Panel.YesButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => Yes()));
        Panel.NoButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => No()));
        Refresh();
        Callable.From(() => { DisableNativeHotkeys(); Panel.YesButton.GrabFocus(); }).CallDeferred();
    }

    private void DisableNativeHotkeys()
    {
        if (!IsInsideTree() || _finished) return;
        foreach (var button in new[] { Panel.YesButton, Panel.NoButton })
        {
            button.DisconnectHotkeys();
            button.FocusMode = button.IsEnabled ? FocusModeEnum.All : FocusModeEnum.None;
            foreach (var path in new[] { "%ControllerIcon", "%HotkeyIcon" })
                if (button.GetNodeOrNull<CanvasItem>(path) is { } icon) { icon.Modulate = new Color(1, 1, 1, 0); icon.Hide(); }
        }
    }
    private void Select()
    {
        if (_finished || !ReferenceEquals(NModalContainer.Instance?.OpenModal, this)) return;
        if (ReferenceEquals(GetViewport().GuiGetFocusOwner(), Panel.NoButton)) No(); else Yes();
    }
    private void Cancel()
    {
        No();
    }
    public override void _Input(InputEvent input)
    {
        if (_finished || !ReferenceEquals(NModalContainer.Instance?.OpenModal, this)) return;
        var keyboard = input as InputEventKey;
        Key keycode = keyboard is null ? Key.None : keyboard.Keycode != Key.None ? keyboard.Keycode : keyboard.PhysicalKeycode;
        bool select = _selectKeys.Any(key => InputMap.HasAction(key) && input.IsAction(key)) || keycode is Key.Enter or Key.KpEnter or Key.Space;
        bool cancel = _cancelKeys.Any(key => InputMap.HasAction(key) && input.IsAction(key)) || keycode == Key.Escape;
        bool left = input.IsAction(MegaInput.left) || keycode == Key.Left;
        bool right = input.IsAction(MegaInput.right) || keycode == Key.Right;
        if (!select && !cancel && !left && !right) return;
        // _Input precedes GUI dispatch. Consume this one path so a focused native
        // button and the modal cannot both react to the same release.
        GetViewport().SetInputAsHandled();
        if (input.IsEcho()) return;
        bool keyReleased = keyboard is { Pressed: false };
        if (select && (keyReleased || _selectKeys.Any(key => InputMap.HasAction(key) && input.IsActionReleased(key)))) Select();
        else if (cancel && (keyReleased || _cancelKeys.Any(key => InputMap.HasAction(key) && input.IsActionReleased(key)))) Cancel();
        else if ((left && (keyboard is { Pressed: true } || input.IsActionPressed(MegaInput.left)))
            || (right && (keyboard is { Pressed: true } || input.IsActionPressed(MegaInput.right))))
        {
            var target = ReferenceEquals(GetViewport().GuiGetFocusOwner(), Panel.YesButton) ? Panel.NoButton : Panel.YesButton;
            if (!ReferenceEquals(target, Panel.NoButton) || SkipReady)
                target.GrabFocus();
        }
    }
    internal void Yes()
    {
        if (_finished) return;
        Finish(true);
    }
    internal void No()
    {
        if (_finished || !SkipReady) return;
        Finish(false);
    }
    private void Finish(bool answer)
    {
        if (_finished) return;
        _finished = true;
        var callback = Choice;
        TreeExited += () => Callable.From(() => callback?.Invoke(answer)).CallDeferred();
        CloseFtue();
    }
    public override void _Process(double delta)
    {
        if (Mode != LibrarianTutorialMode.Consent || _finished) return;
        Elapsed += Math.Max(0, delta);
        int seconds = Math.Max(0, (int)Math.Ceiling(5 - Elapsed));
        if (seconds != _lastSeconds) RefreshCountdown(seconds);
    }
    private void RefreshCountdown(int seconds)
    {
        _lastSeconds = seconds;
        Panel.NoButton.SetText(LibrarianOnboarding.Text("skip") + (seconds > 0 ? "（" + seconds + "）" : ""));
        if (SkipReady) Panel.NoButton.Enable(); else Panel.NoButton.Disable();
        DisableNativeHotkeys();
    }
    private void Refresh()
    {
        string Key(string key) => LibrarianOnboarding.Text(key);
        switch (Mode)
        {
            case LibrarianTutorialMode.Consent:
                Panel.SetText(Key("title"), Key("ask"));
                Panel.YesButton.SetText(Key("yes"));
                RefreshCountdown(Math.Max(0, (int)Math.Ceiling(5 - Elapsed)));
                break;
            case LibrarianTutorialMode.CompactOffer:
                Panel.SetText(Key("compact_title"), Key("compact_ask"));
                Panel.YesButton.SetText(Key("compact_yes")); Panel.NoButton.SetText(Key("compact_no"));
                break;
        }
        DisableNativeHotkeys();
    }
}

/// <summary>Wait for the selected local character and a free native modal slot.</summary>
public partial class LibrarianOnboardingScheduler : Node
{
    private double _wait = .35;
    internal static void Schedule(NCharacterSelectScreen screen)
    {
        if (screen.GetNodeOrNull<LibrarianOnboardingScheduler>("LibrarianOnboardingScheduler") is null)
            screen.AddChild(new LibrarianOnboardingScheduler { Name = "LibrarianOnboardingScheduler" });
    }
    public override void _Process(double delta)
    {
        _wait -= delta;
        if (_wait > 0) return;
        _wait = .15;
        if (GetParent() is not NCharacterSelectScreen screen || !screen.IsVisibleInTree()
            || screen.Lobby?.LocalPlayer.character is not LibrarianCharacter
            || ActiveScreenContext.Instance is null || !ActiveScreenContext.Instance.IsCurrent(screen)
            || NModalContainer.Instance is not { OpenModal: null }) return;
        try
        {
            var stats = SaveManager.Instance.Progress.GetStatsForCharacter(ModelDb.Character<LibrarianCharacter>().Id);
            if (LibrarianOnboarding.NeedsTutorial(stats?.TotalWins ?? 0, stats?.TotalLosses ?? 0,
                LibrarianOnboarding.Has(LibrarianOnboarding.Decision), stats?.Playtime ?? 0)
                && screen.Lobby.NetService.Type == MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Singleplayer)
            {
                LibrarianOnboarding.Show(LibrarianTutorialMode.Consent, answer =>
                {
                    LibrarianOnboarding.Record(LibrarianOnboarding.Decision);
                    if (answer)
                    {
                        LibrarianOnboarding.Record(LibrarianOnboarding.Accepted);
                        if (GodotObject.IsInstanceValid(screen) && screen.IsVisibleInTree()
                            && screen.Lobby?.LocalPlayer.character is LibrarianCharacter)
                            LibrarianOnboarding.StartPracticeFromMenu();
                    }
                });
            }
            else if (LibrarianOnboarding.NeedsCompact(stats?.TotalWins ?? 0, LibrarianOnboarding.Has(LibrarianOnboarding.CompactDecision)))
                LibrarianOnboarding.Show(LibrarianTutorialMode.CompactOffer, answer =>
                {
                    LibrarianPreferences050.Current.CompactHoverTips = answer;
                    LibrarianPreferences050.Save();
                    LibrarianOnboarding.Record(LibrarianOnboarding.CompactDecision);
                });
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn("Librarian onboarding unavailable: " + e.GetType().Name);
            SetProcess(false);
        }
    }
}

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.SelectCharacter))]
internal static class LibrarianOnboardingSelectionPatch
{
    [HarmonyPostfix] private static void Postfix(NCharacterSelectScreen __instance, NCharacterSelectButton charSelectButton, CharacterModel characterModel)
    {
        if (characterModel is LibrarianCharacter && !charSelectButton.IsLocked)
            LibrarianOnboardingScheduler.Schedule(__instance);
    }
}
