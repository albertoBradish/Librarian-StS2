using System.IO;
using Godot;
using Librarian.LibrarianCode;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using STS2RitsuLib.Ui.Toast;

namespace Librarian.Mechanics;

/// <summary>Opt-in native toast, click, timeout and cross-process suppression checks.</summary>
internal static class DevelopmentUpdateToastAudit
{
    private static int _checks;
    private static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException("Update toast: " + label);
        _checks++;
        MainFile.Logger.Info("UPDATE_TOAST_CHECK_PASS " + label);
    }
    private static async Task Wait(double seconds = .4) => await NGame.Instance!.ToSignal(
        NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static Control? Toast(string version) => Descendants(NGame.Instance!).OfType<Control>()
        .FirstOrDefault(n => n.GetType().FullName == "STS2RitsuLib.Ui.Toast.RitsuToastEntry"
            && n.IsVisibleInTree() && n.Modulate.A > .5f
            && Descendants(n).OfType<Label>().Any(l => l.Text.Contains(version)));
    private static string Text(Control toast) => string.Join("", Descendants(toast).OfType<Label>().Select(l => l.Text));
    private static async Task Capture(string name)
    {
        string output = System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT")
            ?? throw new InvalidOperationException("Toast capture directory required");
        Directory.CreateDirectory(output);
        await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
        Check(image.SavePng(Path.Combine(output, name + ".png")) == Error.Ok, "native capture " + name);
    }
    private static async Task Click(Control node)
    {
        var point = node.GetViewport().GetStretchTransform() * node.GetGlobalTransformWithCanvas() * (node.Size * .5f);
#if LIBRARIAN_BETA
        NControllerManager.Instance!.ForceMouseMode();
#endif
        Input.WarpMouse(point);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = point, GlobalPosition = point });
        await Wait(.15);
        foreach (bool pressed in new[] { true, false })
            Input.ParseInputEvent(new InputEventMouseButton
            {
                Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = pressed
            });
        await Wait(.65);
        Input.WarpMouse(new Vector2(12, 12));
        Input.ParseInputEvent(new InputEventMouseMotion { Position = new(12, 12), GlobalPosition = new(12, 12) });
    }
    private static async Task Requeue(NMainMenu menu, string version)
    {
        LibrarianNoticeHistory051.Forget(version);
        LibrarianUpdateNotice051.ResetSessionForAudit();
        LibrarianUpdateNotice051.Schedule(menu);
        await Wait(2.8);
        Check(Toast(version) is not null && NModalContainer.Instance!.OpenModal is null, "automatic entry offers toast only");
    }
    internal static async Task Run(NMainMenu menu)
    {
        Check(OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase)
            && System.Environment.GetEnvironmentVariable("LIBRARIAN_RUNTIME_AUDIT") == "1", "isolated opt-in profile");
        string version = LibrarianUpdateNotice051.CurrentVersion ?? throw new InvalidOperationException("Loaded manifest required");
        Check(version == System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_VERSION"), "actual channel manifest version");
        var container = NModalContainer.Instance!;
        string phase = System.Environment.GetEnvironmentVariable("LIBRARIAN_TOAST_PHASE") ?? "first";
        if (phase == "suppressed-restart")
        {
            Check(LibrarianNoticeHistory051.IsAcknowledged(version), "new process reads explicit suppression");
            await Wait(3);
            Check(Toast(version) is null && container.OpenModal is null, "suppressed restart has neither toast nor dialog");
            MainFile.Logger.Info("UPDATE_TOAST_SUPPRESSED_RESTART_PASS");
            return;
        }
        if (phase == "timeout-restart")
        {
            Check(!LibrarianNoticeHistory051.IsAcknowledged(version), "expired toast did not persist suppression");
            await Wait(3);
            Check(Toast(version) is not null && container.OpenModal is null, "new process offers unacknowledged version as toast");
            await Wait(LibrarianUpdateNotice051.ToastDurationSeconds + 1);
            Check(Toast(version) is null && container.OpenModal is null, "restart timeout still never opens dialog");
            MainFile.Logger.Info("UPDATE_TOAST_TIMEOUT_RESTART_PASS");
            return;
        }
        foreach (var scheduler in menu.GetChildren().Where(n => n is LibrarianUpdateNotice051 or LibrarianArchitectReview102))
            scheduler.SetProcess(false);
        string history = LibrarianNoticeHistory051.FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(history)!);
        if (File.Exists(history)) File.Copy(history, history + ".before-toast-audit", true);
        LibrarianNoticeHistory051.Forget(version);
        LibrarianUpdateNotice051.ResetSessionForAudit();
        string language = LibrarianLanguage.Selected;
        try
        {
            LibrarianLanguage.Select("zhs");
            menu.SubmenuStack.PushSubmenuType<NSettingsScreen>();
            LibrarianUpdateNotice051.Schedule(menu);
            await Wait(2.8);
            Check(Toast(version) is null && container.OpenModal is null, "settings defer automatic toast");
            menu.SubmenuStack.Pop();
            var blocker = NGenericPopup.Create()!;
            container.Add(blocker);
            blocker.GetNode<NVerticalPopup>("VerticalPopup").SetText("Toast audit", "Existing modal must remain intact.");
            await Wait(.6);
            Check(Toast(version) is null && ReferenceEquals(container.OpenModal, blocker), "existing modal is preserved");
            container.Clear();
            await Wait(.9);
            var toast = Toast(version) ?? throw new InvalidOperationException("Scheduled toast not visible");
            Check(container.OpenModal is null, "startup never auto-opens details");
            Check(Text(toast) == $"图书管理员已更新至【{version}】，点此查看更多", "exact requested Chinese text");
            var rect = toast.GetGlobalRect();
            var viewport = toast.GetViewportRect().Size;
            Check(rect.Position.X > viewport.X * .5f && rect.Position.Y < viewport.Y * .25f
                && rect.End.X <= viewport.X && rect.End.Y <= viewport.Y, "native toast lies inside top-right viewport");
            Check(toast.FocusMode == Control.FocusModeEnum.None, "toast does not steal menu focus");
            await Capture("toast-zhs-1280");
            var originalWindowSize = DisplayServer.WindowGetSize();
            try
            {
                foreach (var windowSize in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
                {
                    DisplayServer.WindowSetSize(windowSize);
                    await Wait(.6);
                    Check(DisplayServer.WindowGetSize() == windowSize, "native window size applied " + windowSize);
                    rect = toast.GetGlobalRect();
                    viewport = toast.GetViewportRect().Size;
                    Check(rect.Position.X > viewport.X * .5f && rect.Position.Y < viewport.Y * .25f
                        && rect.End.X <= viewport.X && rect.End.Y <= viewport.Y, "toast remains within top-right bounds " + windowSize);
                    await Capture($"toast-zhs-window-{windowSize.X}x{windowSize.Y}");
                }
            }
            finally { DisplayServer.WindowSetSize(originalWindowSize); }
            await Wait(LibrarianUpdateNotice051.ToastDurationSeconds + 1);
            Check(Toast(version) is null && container.OpenModal is null, "eight-second timeout opens no dialog");
            Check(!LibrarianNoticeHistory051.IsAcknowledged(version), "timeout is not permanent suppression");
            LibrarianUpdateNotice051.Schedule(menu);
            await Wait(2.8);
            Check(Toast(version) is null && container.OpenModal is null, "same-session menu revisit does not repeat toast");
            MainFile.Logger.Info("UPDATE_TOAST_TIMEOUT_PASS");
            if (phase == "timeout") return;

            foreach (string lang in new[] { "zhs", "eng" })
            {
                LibrarianLanguage.Select(lang);
                await Requeue(menu, version);
                toast = Toast(version)!;
                Check(Text(toast) == (lang == "zhs" ? $"图书管理员已更新至【{version}】，点此查看更多"
                    : $"The Librarian has been updated to [{version}]. Click here to learn more."), "localized toast " + lang);
                if (lang == "eng") await Capture("toast-eng-1280");
                await Click(toast);
                Check(container.OpenModal is NGenericPopup popup && popup.Name == "LibrarianUpdateNotice", "real mouse click opens existing details " + lang);
                var panel = ((NGenericPopup)container.OpenModal!).GetNode<NVerticalPopup>("VerticalPopup");
                Check(panel.GetNode<RichTextLabel>("Description").GetParsedText().Contains(version), "details retain loaded version " + lang);
                await Capture("clicked-details-" + lang);
                await Click(panel.YesButton);
                Check(container.OpenModal is null && !LibrarianNoticeHistory051.IsAcknowledged(version), "normal close preserves suppression choice " + lang);
            }

            // A click is retained while another modal owns the native slot.
            await Requeue(menu, version);
            toast = Toast(version)!;
            blocker = NGenericPopup.Create()!;
            container.Add(blocker);
            blocker.GetNode<NVerticalPopup>("VerticalPopup").SetText("Toast audit", "Click must wait for this modal.");
            await Click(toast);
            Check(ReferenceEquals(container.OpenModal, blocker), "toast click cannot replace another modal");
            container.Clear();
            await Wait(.8);
            Check(container.OpenModal is NGenericPopup { Name: var deferredPopupName } && deferredPopupName == "LibrarianUpdateNotice", "explicit click opens after native modal slot becomes free");
            var suppress = ((NGenericPopup)container.OpenModal!).GetNode<NPopupYesNoButton>("VerticalPopup/NeverShowButton");
            await Click(suppress);
            LibrarianNoticeHistory051.ForgetSessionForAudit();
            Check(container.OpenModal is null && LibrarianNoticeHistory051.IsAcknowledged(version), "never-show persisted to isolated disk");
            LibrarianUpdateNotice051.Schedule(menu);
            await Wait(2.8);
            Check(Toast(version) is null && container.OpenModal is null, "suppressed version offers no toast");

            menu.SubmenuStack.PushSubmenuType<NSettingsScreen>();
            Check(LibrarianUpdateNotice051.RequestRedisplay(), "settings explicit redisplay accepted");
            await Wait(2.8);
            Check(container.OpenModal is null && Toast(version) is null, "explicit settings action waits for main menu");
            menu.SubmenuStack.Pop();
            await Wait(.8);
            Check(container.OpenModal is NGenericPopup { Name: var popupName } && popupName == "LibrarianUpdateNotice", "explicit settings redisplay opens details directly");
            await Click(((NGenericPopup)container.OpenModal!).GetNode<NPopupYesNoButton>("VerticalPopup/NeverShowButton"));
            Check(!LibrarianNoticeHistory051.IsAcknowledged(version + ".future"), "future versions remain eligible");
            MainFile.Logger.Info($"UPDATE_TOAST_AUDIT_PASS checks={_checks}");
        }
        finally { LibrarianLanguage.Select(language); }
    }
}
