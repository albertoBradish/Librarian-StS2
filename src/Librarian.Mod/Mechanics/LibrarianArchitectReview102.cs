using System.IO;
using System.Text.Json;
using Godot;
using HarmonyLib;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace Librarian.Mechanics;

/// <summary>Shared native popup layout and explicit actions; Back only dismisses.</summary>
internal static class LibrarianNoticePopup102
{
#if LIBRARIAN_STABLE && LIBRARIAN_BETA
#error Select exactly one Librarian release channel.
#elif LIBRARIAN_STABLE
    internal const string WorkshopUrl = "https://steamcommunity.com/sharedfiles/filedetails/?id=3809003723";
#elif LIBRARIAN_BETA
    internal const string WorkshopUrl = "https://steamcommunity.com/sharedfiles/filedetails/?id=3801958367";
#else
#error Define LIBRARIAN_BETA or LIBRARIAN_STABLE so player links match the compiled release channel.
#endif
    internal const string IssuesUrl = "https://github.com/albertoBradish/Librarian-StS2/issues";
    internal const string QqGroup = "1091648383";

    internal static NPopupYesNoButton AddMiddleButton(NVerticalPopup panel, string name, string key, Action action)
    {
        // Instantiate the native scene to retain its unique-name ownership for _Ready.
        // Duplicating a live button loses that scope and breaks its %Label/%Visuals lookups.
        var button = GD.Load<PackedScene>("res://scenes/ui/abandon_run_no_button.tscn").Instantiate<NPopupYesNoButton>();
        button.Name = name;
        foreach (string path in new[] { "%Image", "%Outline" })
        {
            var visual = button.GetNodeOrNull<CanvasItem>(path);
            if (visual?.Material is { } material) visual.Material = (Material)material.Duplicate();
        }
        panel.AddChild(button);
        button.Visible = true;
        button.IsYes = false;
        button.SetText(new LocString("main_menu_ui", key).GetFormattedText());
        button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => action()));
        return button;
    }

    internal static void ConfigureActions(NGenericPopup popup, NVerticalPopup panel, NPopupYesNoButton middle)
    {
        // GenericPopup is centered, but its native VerticalPopup child has zero anchors
        // and an asymmetric (-51,-59)..(522,600) rectangle. Widening only the child
        // moves its center away from the parent's center. Center the complete 720x659
        // popup and let the visible panel fill it; native canvas scaling applies once.
        popup.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        popup.OffsetLeft = -360;
        popup.OffsetRight = 360;
        popup.OffsetTop = -329.5f;
        popup.OffsetBottom = 329.5f;
        popup.PivotOffset = new Vector2(360,329.5f);
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        panel.PivotOffset = popup.PivotOffset;
        // The native TextureRect otherwise keeps the old narrow aspect ratio inside
        // the wider control, leaving text and buttons outside the visible frame.
        panel.Set("stretch_mode", (int)TextureRect.StretchModeEnum.Scale);
        var description = panel.GetNode<MegaCrit.Sts2.addons.mega_text.MegaRichTextLabel>("Description");
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        description.IsHorizontallyBound = true;
        description.ClipContents = true;
        NPopupYesNoButton[] buttons = [panel.NoButton, middle, panel.YesButton];
        for (int i = 0; i < buttons.Length; i++)
        {
            var button = buttons[i];
            button.AnchorLeft = button.AnchorRight = i * .5f;
            button.AnchorTop = button.AnchorBottom = 1;
            button.OffsetLeft = i switch { 0 => 40, 1 => -90, _ => -220 };
            button.OffsetRight = button.OffsetLeft + 180;
            button.OffsetTop = -152;
            button.OffsetBottom = -80;
            button.FocusMode = Control.FocusModeEnum.All;
            button.FocusNeighborLeft = button.GetPathTo(buttons[(i + 2) % 3]);
            button.FocusNeighborRight = button.GetPathTo(buttons[(i + 1) % 3]);
            button.FocusPrevious = button.FocusNeighborLeft;
            button.FocusNext = button.FocusNeighborRight;
        }
        var container = NModalContainer.Instance!;
        var hotkeys = NHotkeyManager.Instance;
        string[] cancelKeys = [MegaInput.cancel, MegaInput.pauseAndBack];
        bool bound = false;
        void IgnorePress() { }
        void Dismiss() { if (ReferenceEquals(container.OpenModal, popup)) container.Clear(); }
        void Select()
        {
            if (!ReferenceEquals(container.OpenModal, popup)) return;
            var focused = popup.GetViewport().GuiGetFocusOwner();
            var selected = buttons.FirstOrDefault(b => ReferenceEquals(b, focused)) ?? panel.YesButton;
            selected.EmitSignal(NClickableControl.SignalName.Released, selected);
        }
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(popup) || !popup.IsInsideTree()) return;
            // Prevent native No/Yes hotkeys from launching a link or suppressing an unread notice.
            foreach (var button in buttons)
            {
                button.DisconnectHotkeys();
                if (button.GetNodeOrNull<CanvasItem>("%HotkeyIcon") is { } icon)
                {
                    // Input-device changes can make the native icon visible again.
                    icon.Modulate = new Color(1,1,1,0);
                    icon.Hide();
                }
            }
            foreach (string key in cancelKeys)
            {
                hotkeys?.PushHotkeyPressedBinding(key, IgnorePress);
                hotkeys?.PushHotkeyReleasedBinding(key, Dismiss);
            }
            hotkeys?.PushHotkeyPressedBinding(MegaInput.select, IgnorePress);
            hotkeys?.PushHotkeyReleasedBinding(MegaInput.select, Select);
            bound = true;
            panel.YesButton.GrabFocus();
        }).CallDeferred();
        popup.TreeExiting += () =>
        {
            if (!bound) return;
            foreach (string key in cancelKeys)
            {
                hotkeys?.RemoveHotkeyPressedBinding(key, IgnorePress);
                hotkeys?.RemoveHotkeyReleasedBinding(key, Dismiss);
            }
            hotkeys?.RemoveHotkeyPressedBinding(MegaInput.select, IgnorePress);
            hotkeys?.RemoveHotkeyReleasedBinding(MegaInput.select, Select);
        };
    }

    internal static void OpenLink(string url, Action<string>? openLink = null)
    {
        try { (openLink ?? PlatformUtil.OpenUrl)(url); }
        catch (Exception e) { MainFile.Logger.Warn("Librarian notice link could not be opened: " + e.GetType().Name); }
    }
}

/// <summary>Mod-owned first Architect victory receipt; never changes native progression.</summary>
internal static class LibrarianArchitectReviewHistory102
{
    internal sealed class History
    {
        public bool Pending { get; set; }
        public bool Shown { get; set; }
        public long FirstWinRunStartTime { get; set; }
    }
    internal static string FilePath => ProjectSettings.GlobalizePath("user://Librarian/architect-review.json");
    private static History? _session;
    private static History Current => _session ??= Read();
    internal static bool Pending => Current.Pending && !Current.Shown;
    internal static bool Shown => Current.Shown;

    private static History Read()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<History>(File.ReadAllText(FilePath))
                ?? throw new JsonException("Missing review history") : new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            MainFile.Logger.Warn("Architect review history unreadable; original file retained: " + e.GetType().Name);
            return new();
        }
    }

    internal static bool RecordFirstWin(long runStartTime)
    {
        if (Current.Pending || Current.Shown) return false;
        Current.FirstWinRunStartTime = runStartTime;
        Current.Pending = true;
        Save();
        MainFile.Logger.Info("ARCHITECT_REVIEW_ELIGIBLE run_start=" + runStartTime);
        return true;
    }

    internal static void MarkShown()
    {
        Current.Pending = false;
        Current.Shown = true;
        Save();
    }

    private static void Save()
    {
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            if (File.Exists(FilePath))
            {
                try
                {
                    if (JsonSerializer.Deserialize<History>(File.ReadAllText(FilePath)) is null)
                        throw new JsonException("Missing review history");
                }
                catch (JsonException)
                {
                    File.Copy(FilePath, FilePath + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N"));
                }
            }
            File.WriteAllText(temporary, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, FilePath, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            MainFile.Logger.Warn("Architect review history is session-only; storage unavailable: " + e.GetType().Name);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static void ForgetSessionForAudit() => _session = null;
}

/// <summary>Shows after returning to a free visible main menu; previews never record victories.</summary>
public partial class LibrarianArchitectReview102 : Node
{
    private static WeakReference<NMainMenu>? _menu;
    private static bool _previewRequested;
    private bool _unavailable;
    private double _wait = 2.1;
    internal static bool HasPendingMenuNotice => (_previewRequested || LibrarianArchitectReviewHistory102.Pending)
        && !(_menu?.TryGetTarget(out var menu) == true && GodotObject.IsInstanceValid(menu)
            && menu.GetNodeOrNull<LibrarianArchitectReview102>("LibrarianArchitectReviewScheduler")?._unavailable == true);

    internal static void Schedule(NMainMenu menu)
    {
        _menu = new(menu);
        if (menu.GetNodeOrNull<LibrarianArchitectReview102>("LibrarianArchitectReviewScheduler") is { } existing)
        {
            if (!existing.IsQueuedForDeletion()) { existing.SetProcess(true); return; }
            menu.RemoveChild(existing);
        }
        menu.AddChild(new LibrarianArchitectReview102 { Name = "LibrarianArchitectReviewScheduler" });
    }

    internal static bool RequestPreview()
    {
        _previewRequested = true;
        if (_menu?.TryGetTarget(out var menu) == true && GodotObject.IsInstanceValid(menu) && menu.IsInsideTree())
            Schedule(menu);
        return true;
    }

    public override void _Process(double delta)
    {
        _wait -= delta;
        if (_wait > 0) return;
        _wait = .25;
        if (!HasPendingMenuNotice) { QueueFree(); return; }
        if (GetParent() is not NMainMenu menu || !menu.IsVisibleInTree() || menu.SubmenuStack.SubmenusOpen
            || ActiveScreenContext.Instance is null || !ActiveScreenContext.Instance.IsCurrent(menu)) return;
        if (NModalContainer.Instance is null || NModalContainer.Instance.OpenModal is not null) return;
        try
        {
            bool preview = _previewRequested;
            if (Show(preview) is null) return;
            _previewRequested = false;
            // A debug preview must not consume an earned prompt that is still pending.
            if (!LibrarianArchitectReviewHistory102.Pending) QueueFree();
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn("Architect review notice unavailable; main menu remains usable: " + e.GetType().Name);
            _previewRequested = false;
            // Keep a per-menu failure receipt so the welcome scheduler can proceed.
            // Earned history remains pending for the next main menu instance.
            _unavailable = true;
            SetProcess(false);
        }
    }

    internal static NGenericPopup? Show(bool preview = false, Action<string>? openLink = null)
    {
        if (!preview && !LibrarianArchitectReviewHistory102.Pending) return null;
        var container = NModalContainer.Instance;
        if (container is null || container.OpenModal is not null) return null;
        var popup = NGenericPopup.Create();
        if (popup is null) return null;
        try
        {
            popup.Name = "LibrarianArchitectReview";
            container.Add(popup);
            var panel = popup.GetNode<NVerticalPopup>("VerticalPopup");
            var body = new LocString("main_menu_ui", "LIBRARIAN_REVIEW.body");
            body.Add("Version", LibrarianUpdateNotice051.CurrentVersion ?? "");
            body.Add("Email", new LocString("main_menu_ui", "LIBRARIAN_UPDATE.email").GetRawText());
            body.Add("QQGroup", LibrarianNoticePopup102.QqGroup);
            body.Add("Issues", LibrarianNoticePopup102.IssuesUrl);
            panel.SetText(new LocString("main_menu_ui", "LIBRARIAN_REVIEW.header"), body);
            panel.InitYesButton(new LocString("main_menu_ui", "LIBRARIAN_REVIEW.confirm"), _ => { });
            var link = panel.NoButton;
            link.Visible = true;
            link.IsYes = false;
            link.SetText(new LocString("main_menu_ui", "LIBRARIAN_REVIEW.workshop").GetFormattedText());
            link.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ =>
                LibrarianNoticePopup102.OpenLink(LibrarianNoticePopup102.WorkshopUrl, openLink)));
            var feedback = LibrarianNoticePopup102.AddMiddleButton(panel, "FeedbackButton", "LIBRARIAN_REVIEW.feedback", () =>
                LibrarianNoticePopup102.OpenLink(LibrarianNoticePopup102.IssuesUrl, openLink));
            LibrarianNoticePopup102.ConfigureActions(popup, panel, feedback);
            if (!preview) LibrarianArchitectReviewHistory102.MarkShown();
            MainFile.Logger.Info("ARCHITECT_REVIEW_SHOWN preview=" + preview);
            return popup;
        }
        catch
        {
            if (ReferenceEquals(container.OpenModal, popup)) container.Clear();
            if (GodotObject.IsInstanceValid(popup)) popup.QueueFree();
            throw;
        }
    }
}

[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class LibrarianArchitectReviewMenu102
{
    [HarmonyPostfix] private static void Postfix(NMainMenu __instance) => LibrarianArchitectReview102.Schedule(__instance);
}

[HarmonyPatch(typeof(ProgressSaveManager), nameof(ProgressSaveManager.UpdateWithRunData))]
internal static class LibrarianArchitectVictoryReview102
{
    private static readonly System.Reflection.PropertyInfo NativeRunState = AccessTools.Property(typeof(RunManager), "State");
    internal static bool IsEligible(bool victory, bool architectEvent, ModelId characterId, int totalWins)
        => victory && architectEvent && characterId == ModelDb.Character<LibrarianCharacter>().Id && totalWins == 1;

    [HarmonyPostfix]
    private static void Postfix(ProgressSaveManager __instance, SerializableRun serializableRun, bool victory)
    {
        try
        {
            if (!victory || serializableRun.GameMode != GameMode.Standard
                || (NativeRunState.GetValue(RunManager.Instance) as RunState)?.CurrentRoom is not EventRoom { CanonicalEvent: TheArchitect }) return;
            // Match native local-player selection: a teammate using Librarian cannot qualify this player.
            var player = serializableRun.Players.Count == 1 ? serializableRun.Players[0]
                : serializableRun.Players.FirstOrDefault(p => p.NetId == PlatformUtil.GetLocalPlayerId(serializableRun.PlatformType));
            if (player is null || !IsEligible(victory, true, player.CharacterId,
                __instance.Progress.GetStatsForCharacter(player.CharacterId)?.TotalWins ?? 0)) return;
            LibrarianArchitectReviewHistory102.RecordFirstWin(serializableRun.StartTime);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn("Architect review receipt unavailable; native victory unaffected: " + e.GetType().Name);
        }
    }
}
