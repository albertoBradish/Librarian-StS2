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

/// <summary>Waits for a free native modal slot on the visible main menu, then retires itself.</summary>
public partial class LibrarianUpdateNotice051 : Node
{
    internal const string WorkshopUrl = LibrarianNoticePopup102.WorkshopUrl;
    internal const string Email = "aery_bradish@163.com";
    internal const string Qq = "1850562239";
    private static readonly HashSet<string> Presented = new(StringComparer.Ordinal);
    private static WeakReference<NMainMenu>? _menu;
    private double _wait = 2.1; // Native main-menu fade lasts two seconds.
    internal static string? CurrentVersion => ModManager.GetLoadedMods()
        .FirstOrDefault(m => m.manifest?.id == MainFile.ModId && m.assembly == typeof(MainFile).Assembly)?.manifest?.version;

    public override void _Process(double delta)
    {
        _wait -= delta;
        if (_wait > 0) return;
        _wait = 0.25;
        if (GetParent() is not NMainMenu menu || !menu.IsVisibleInTree() || menu.SubmenuStack.SubmenusOpen
            || ActiveScreenContext.Instance is null || !ActiveScreenContext.Instance.IsCurrent(menu)) return;
        if (NModalContainer.Instance is null || NModalContainer.Instance.OpenModal is not null) return;
        if (LibrarianArchitectReview102.HasPendingMenuNotice) return;
        string? version = CurrentVersion;
        if (string.IsNullOrWhiteSpace(version)) { QueueFree(); return; }
        if (Presented.Contains(version) || LibrarianNoticeHistory051.IsAcknowledged(version)) { QueueFree(); return; }
        try { if (Show(version) is not null) QueueFree(); }
        catch (Exception e)
        {
            MainFile.Logger.Warn("Update notice unavailable; main menu remains usable: " + e.GetType().Name);
            QueueFree();
        }
    }

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

    internal static void ResetSessionForAudit() { Presented.Clear(); LibrarianNoticeHistory051.ForgetSessionForAudit(); }

    /// <summary>Re-enables this version and queues it until the settings page has been closed.</summary>
    internal static bool RequestRedisplay()
    {
        string? version = CurrentVersion;
        if (string.IsNullOrWhiteSpace(version)) return false;
        LibrarianNoticeHistory051.Forget(version);
        Presented.Remove(version);
        if (_menu?.TryGetTarget(out var menu) == true && GodotObject.IsInstanceValid(menu) && menu.IsInsideTree())
            Schedule(menu);
        return true;
    }

    internal static void Schedule(NMainMenu menu)
    {
        _menu = new(menu);
        if (menu.GetNodeOrNull<LibrarianUpdateNotice051>("LibrarianUpdateNoticeScheduler") is { } existing)
        {
            if (!existing.IsQueuedForDeletion()) { existing.SetProcess(true); return; }
            menu.RemoveChild(existing);
        }
        menu.AddChild(new LibrarianUpdateNotice051 { Name = "LibrarianUpdateNoticeScheduler" });
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
