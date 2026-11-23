using System.Security.Cryptography;
using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace Librarian.Loader;

internal static class CompatibilityNotice
{
    private const string Title = "你来的真早！";
    private const string Message = "模组还未完全适配此版本，请耐心等候更新。在此期间，无需取消此模组订阅，此模组不会在此版本加载！";

    internal static void Queue(string? label, bool unsupportedGame)
    {
        Log.Info($"[Librarian.Loader] Content skipped; reason={(unsupportedGame ? "unsupported-game" : "ritsulib-unavailable")}; host={label ?? "unknown"}.");
        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree)
                return;
            var marker = unsupportedGame ? AcknowledgementPath(label) : null;
            if (marker is not null && File.Exists(marker))
                return;

            ulong availableSince = 0;
            void OnFrame()
            {
                try
                {
                    var menu = NGame.Instance?.MainMenu;
                    var container = NModalContainer.Instance;
                    if (menu is null || !menu.IsVisibleInTree() || menu.SubmenuStack.SubmenusOpen
                        || ActiveScreenContext.Instance?.IsCurrent(menu) != true
                        || container is null || container.OpenModal is not null)
                    {
                        availableSince = 0;
                        return;
                    }
                    ulong now = Time.GetTicksMsec();
                    if (availableSince == 0) availableSince = now;
                    if (now - availableSince < 2100) return;

                    tree.ProcessFrame -= OnFrame;
                    Show(container, marker, unsupportedGame);
                }
                catch (Exception exception)
                {
                    tree.ProcessFrame -= OnFrame;
                    Log.Warn($"[Librarian.Loader] Compatibility notice unavailable: {exception.GetType().Name}.");
                }
            }
            tree.ProcessFrame += OnFrame;
        }
        catch (Exception exception)
        {
            Log.Warn($"[Librarian.Loader] Compatibility notice unavailable: {exception.GetType().Name}.");
        }
    }

    private static void Show(NModalContainer container, string? marker, bool unsupportedGame)
    {
        var popup = NGenericPopup.Create();
        if (popup is null) return;
        try
        {
            popup.Name = "LibrarianCompatibilityNotice";
            container.Add(popup);
            var panel = popup.GetNode<NVerticalPopup>("VerticalPopup");
            panel.SetText(unsupportedGame ? Title : "图书管理员暂未加载",
                unsupportedGame ? Message : $"RitsuLib 未能加载或版本不符合要求。请检查 RitsuLib（需 {BundleManifest.MinimumRitsuVersion} 或更新版本），然后重启游戏。");
            panel.InitYesButton(new LocString("main_menu_ui", "GENERIC_POPUP.confirm"),
                _ => { if (marker is not null) Acknowledge(marker); });
            panel.HideNoButton();
            panel.YesButton.FocusMode = Control.FocusModeEnum.All;
            Callable.From(() =>
            {
                if (ReferenceEquals(container.OpenModal, popup))
                    panel.YesButton.GrabFocus();
            }).CallDeferred();
            Log.Info("[Librarian.Loader] COMPATIBILITY_NOTICE_SHOWN");
        }
        catch
        {
            if (ReferenceEquals(container.OpenModal, popup)) container.Clear();
            if (GodotObject.IsInstanceValid(popup) && !popup.IsQueuedForDeletion()) popup.QueueFree();
            throw;
        }
    }

    private static string AcknowledgementPath(string? label)
    {
        var version = label?.StartsWith('v') == true ? label[1..] : label;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(version ?? "")));
        return ProjectSettings.GlobalizePath($"user://Librarian/compatibility-notices/{key}.seen");
    }

    private static void Acknowledge(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var marker = new FileStream(path, FileMode.OpenOrCreate, System.IO.FileAccess.Write);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"[Librarian.Loader] Compatibility notice acknowledgement could not be saved: {exception.GetType().Name}.");
        }
    }
}
