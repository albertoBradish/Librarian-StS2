using System.IO;
using System.Text.Json;
using Godot;
using HarmonyLib;
using Librarian.LibrarianCode;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Platform;
using STS2RitsuLib.Ui.Toast;

namespace Librarian.Mechanics;

/// <summary>Explicit local release suppression; closing a notice does not suppress it.</summary>
internal static class LibrarianNoticeHistory051
{
    internal sealed class History { public List<string> AcknowledgedVersions { get; set; } = []; }
    internal static string FilePath => ProjectSettings.GlobalizePath("user://Librarian/update-notice.json");
    private static readonly HashSet<string> SessionAcknowledged = new(StringComparer.Ordinal);
    private static readonly HashSet<string> SessionForgotten = new(StringComparer.Ordinal);

    internal static bool IsAcknowledged(string version)
        => SessionAcknowledged.Contains(version) || (!SessionForgotten.Contains(version)
            && Read().AcknowledgedVersions.Contains(version, StringComparer.Ordinal));

    private static History Read()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var history = JsonSerializer.Deserialize<History>(File.ReadAllText(FilePath));
            if (history?.AcknowledgedVersions is null) throw new JsonException("Missing version history");
            history.AcknowledgedVersions = history.AcknowledgedVersions
                .Where(v => !string.IsNullOrWhiteSpace(v) && v.Length <= 80).Distinct(StringComparer.Ordinal).TakeLast(32).ToList();
            return history;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            MainFile.Logger.Warn("Update notice history could not be read; original file retained: " + e.GetType().Name);
            return new();
        }
    }

    internal static bool Acknowledge(string version)
    {
        SessionForgotten.Remove(version);
        SessionAcknowledged.Add(version); // A full/unwritable disk must not trap the player in the dialog.
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var history = Read();
            if (!history.AcknowledgedVersions.Contains(version, StringComparer.Ordinal)) history.AcknowledgedVersions.Add(version);
            history.AcknowledgedVersions = history.AcknowledgedVersions.TakeLast(32).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            // Preserve a malformed input before replacing it with an explicit user acknowledgement.
            if (File.Exists(FilePath))
            {
                try
                {
                    if (JsonSerializer.Deserialize<History>(File.ReadAllText(FilePath))?.AcknowledgedVersions is null)
                        throw new JsonException("Missing version history");
                }
                catch (JsonException)
                {
                    File.Copy(FilePath, FilePath + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N"));
                }
            }
            File.WriteAllText(temporary, JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, FilePath, true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            MainFile.Logger.Warn("Update acknowledgement is session-only; storage unavailable: " + e.GetType().Name);
            return false;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static bool Forget(string version)
    {
        SessionAcknowledged.Remove(version);
        SessionForgotten.Add(version);
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var history = Read();
            history.AcknowledgedVersions.RemoveAll(v => v == version);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            if (File.Exists(FilePath))
            {
                try
                {
                    if (JsonSerializer.Deserialize<History>(File.ReadAllText(FilePath))?.AcknowledgedVersions is null)
                        throw new JsonException("Missing version history");
                }
                catch (JsonException)
                {
                    File.Copy(FilePath, FilePath + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N"));
                }
            }
            File.WriteAllText(temporary, JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, FilePath, true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            MainFile.Logger.Warn("Update notice reset is session-only; storage unavailable: " + e.GetType().Name);
            return false;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static void ForgetSessionForAudit() { SessionAcknowledged.Clear(); SessionForgotten.Clear(); }
}

/// <summary>Offers a timed toast; only an explicit click may queue the native details dialog.</summary>
public partial class LibrarianUpdateNotice051 : Node
{
#if LIBRARIAN_BETA
    internal const string WorkshopUrl = "https://steamcommunity.com/sharedfiles/filedetails/changelog/3801958367";
#else
    internal const string WorkshopUrl = LibrarianNoticePopup102.WorkshopUrl;
#endif
    internal const string Email = "aery_bradish@163.com";
    internal const string Qq = "1850562239";
    private static readonly HashSet<string> Presented = new(StringComparer.Ordinal);
    private static WeakReference<NMainMenu>? _menu;
    private double _wait = 2.1; // Native main-menu fade lasts two seconds.
    internal const double ToastDurationSeconds = 8;
    private RitsuToastHandle? _toast;
    private bool _openRequested;
    internal static string? CurrentVersion => ModManager.GetLoadedMods()
        .FirstOrDefault(m => m.manifest?.id == MainFile.ModId && m.assemblies.Contains(typeof(MainFile).Assembly))?.manifest?.version;

    public override void _Process(double delta)
    {
        _wait -= delta;
        if (_wait > 0) return;
        _wait = 0.25;
        if (_toast is not null && !_openRequested && !_toast.IsAlive()) { QueueFree(); return; }
        if (GetParent() is not NMainMenu menu || !menu.IsVisibleInTree() || menu.SubmenuStack.SubmenusOpen
            || ActiveScreenContext.Instance is null || !ActiveScreenContext.Instance.IsCurrent(menu)) return;
        if (NModalContainer.Instance is null || NModalContainer.Instance.OpenModal is not null) return;
        if (LibrarianArchitectReview102.HasPendingMenuNotice) return;
        string? version = CurrentVersion;
        if (string.IsNullOrWhiteSpace(version)) { QueueFree(); return; }
        try
        {
            if (_openRequested)
            {
                if (Show(version) is not null) QueueFree();
                return;
            }
            if (_toast is not null) return;
            if (Presented.Contains(version) || LibrarianNoticeHistory051.IsAcknowledged(version)) { QueueFree(); return; }
            var text = new LocString("main_menu_ui", "LIBRARIAN_UPDATE.toast");
            text.Add("Version", version);
            _toast = RitsuToastService.ShowTracked(new RitsuToastRequest(text.GetFormattedText(),
                durationSeconds: ToastDurationSeconds, onClick: () =>
                {
                    if (GodotObject.IsInstanceValid(this) && IsInsideTree() && !IsQueuedForDeletion())
                        _openRequested = true;
                }));
            Presented.Add(version);
            MainFile.Logger.Info("UPDATE_NOTICE_TOAST_QUEUED version=" + version);
            if (!_toast.IsAlive()) QueueFree(); // Respect the player's RitsuLib notification settings.
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn("Update notice unavailable; main menu remains usable: " + e.GetType().Name);
            QueueFree();
        }
    }

    public override void _ExitTree() => _toast?.Close(true);

    internal static NGenericPopup? Show(string version, Action<string>? openLink = null)
    {
        var container = NModalContainer.Instance;
        if (container is null || container.OpenModal is not null) return null;
        var popup = NGenericPopup.Create();
        if (popup is null) return null;
        try
        {
            popup.Name = "LibrarianUpdateNotice";
            container.Add(popup);
            var panel = popup.GetNode<NVerticalPopup>("VerticalPopup");
            var body = new LocString("main_menu_ui", "LIBRARIAN_UPDATE.body");
            body.Add("Version", version);
            body.Add("Email", new LocString("main_menu_ui", "LIBRARIAN_UPDATE.email").GetRawText());
            body.Add("QQ", Qq);
            panel.SetText(new LocString("main_menu_ui", "LIBRARIAN_UPDATE.header"), body);
            panel.InitYesButton(new LocString("main_menu_ui", "LIBRARIAN_UPDATE.confirm"), _ => { });
            var link = panel.NoButton;
            link.Visible = true;
            link.IsYes = false;
            link.SetText(new LocString("main_menu_ui", "LIBRARIAN_UPDATE.workshop").GetFormattedText());
            link.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ =>
            {
                try { (openLink ?? PlatformUtil.OpenUrl)(WorkshopUrl); }
                catch (Exception e) { MainFile.Logger.Warn("Workshop link could not be opened: " + e.GetType().Name); }
            }));
            var suppress = LibrarianNoticePopup102.AddMiddleButton(panel, "NeverShowButton", "LIBRARIAN_UPDATE.never_show", () =>
            {
                LibrarianNoticeHistory051.Acknowledge(version);
                if (ReferenceEquals(container.OpenModal, popup)) container.Clear();
            });
            LibrarianNoticePopup102.ConfigureActions(popup, panel, suppress);
#if LIBRARIAN_BETA
            ConfigureNativeInputFocus(popup, panel);
#endif
            Presented.Add(version);
            MainFile.Logger.Info("UPDATE_NOTICE_SHOWN version=" + version);
            return popup;
        }
        catch
        {
            // Button/localization setup errors must never leave a blank modal blocking the game.
            if (ReferenceEquals(container.OpenModal, popup)) container.Clear();
            if (GodotObject.IsInstanceValid(popup)) popup.QueueFree();
            throw;
        }
    }

    private static void ConfigureNativeInputFocus(NGenericPopup popup, NVerticalPopup panel)
    {
        var controller = NControllerManager.Instance;
        void Refresh()
        {
            if (!GodotObject.IsInstanceValid(popup) || !popup.IsInsideTree()
                || !ReferenceEquals(NModalContainer.Instance?.OpenModal, popup)) return;
            var focused = popup.GetViewport().GuiGetFocusOwner();
            if (controller?.IsUsingDirectionalNavigation == true)
            {
                // GenericPopup has no native DefaultFocusedControl. Establish the
                // default only when arrow-key/controller navigation is active.
                if (focused is null || !popup.IsAncestorOf(focused)) panel.YesButton.GrabFocus();
            }
            else if (focused is not null && popup.IsAncestorOf(focused))
            {
                // NClickableControl uses hover OR GUI focus to light its outline.
                // Mouse mode must let native MouseEntered/MouseExited own that state.
                focused.ReleaseFocus();
            }
        }
        Callable refresh = Callable.From(Refresh);
        controller?.Connect(NControllerManager.SignalName.ControllerDetected, refresh);
        controller?.Connect(NControllerManager.SignalName.MouseDetected, refresh);
        // Run after ConfigureActions' deferred hotkey setup; both deferred calls
        // complete before the first draw, so mouse mode never draws the default focus.
        refresh.CallDeferred();
        popup.TreeExiting += () =>
        {
            if (controller is null || !GodotObject.IsInstanceValid(controller)) return;
            controller.Disconnect(NControllerManager.SignalName.ControllerDetected, refresh);
            controller.Disconnect(NControllerManager.SignalName.MouseDetected, refresh);
        };
    }

    internal static void ResetSessionForAudit() { Presented.Clear(); LibrarianNoticeHistory051.ForgetSessionForAudit(); }

    /// <summary>Re-enables this version and queues it until the settings page has been closed.</summary>
    internal static bool RequestRedisplay()
    {
        string? version = CurrentVersion;
        if (string.IsNullOrWhiteSpace(version)) return false;
        LibrarianNoticeHistory051.Forget(version);
        Presented.Remove(version);
        if (_menu?.TryGetTarget(out var menu) == true && GodotObject.IsInstanceValid(menu) && menu.IsInsideTree())
            Schedule(menu, openDetails: true);
        return true;
    }

    internal static void Schedule(NMainMenu menu, bool openDetails = false)
    {
        _menu = new(menu);
        if (menu.GetNodeOrNull<LibrarianUpdateNotice051>("LibrarianUpdateNoticeScheduler") is { } existing)
        {
            if (!existing.IsQueuedForDeletion())
            {
                existing._openRequested |= openDetails;
                if (openDetails) existing._toast?.Close(true);
                existing.SetProcess(true);
                return;
            }
            menu.RemoveChild(existing);
        }
        menu.AddChild(new LibrarianUpdateNotice051 { Name = "LibrarianUpdateNoticeScheduler", _openRequested = openDetails });
    }
}

[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class LibrarianUpdateNoticeMenu051
{
    [HarmonyPostfix]
    private static void Postfix(NMainMenu __instance)
    {
        LibrarianUpdateNotice051.Schedule(__instance);
    }
}
