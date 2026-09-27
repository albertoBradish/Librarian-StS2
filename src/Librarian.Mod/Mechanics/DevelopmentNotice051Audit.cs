using System.IO;
using Godot;
using HarmonyLib;
using Librarian.LibrarianCode;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Saves;

namespace Librarian.Mechanics;

internal static class DevelopmentNotice051Audit
{
    private static int _checks;
    private static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException("051 notice: " + label);
        _checks++; MainFile.Logger.Info("V051_NOTICE_CHECK_PASS " + label);
    }
    private static async Task Wait(double seconds = 0.12)
        => await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private static async Task Capture(string name)
    {
        string output = System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT") ?? Path.Combine(@"D:\Slay The Spire_Mod Dev\outputs\revision-v1.0.0-stable\audit-history\revision-v0.5.1", "screenshots");
        Directory.CreateDirectory(output);
        await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
        Check(image.SavePng(Path.Combine(output, name + ".png")) == Error.Ok, "native screenshot " + name);
    }

    internal static async Task Run(NMainMenu menu)
    {
        Check(OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase)
            && System.Environment.GetEnvironmentVariable("LIBRARIAN_RUNTIME_AUDIT") == "1", "isolated opt-in profile");
        var scheduler = menu.GetNode<LibrarianUpdateNotice051>("LibrarianUpdateNoticeScheduler");
        string version = LibrarianUpdateNotice051.CurrentVersion ?? throw new InvalidOperationException("Loaded manifest unavailable");
        Check(version == (System.Environment.GetEnvironmentVariable("LIBRARIAN_EXPECTED_VERSION") ?? "0.5.1"), "version resolved from actually loaded manifest");
        string phase = System.Environment.GetEnvironmentVariable("LIBRARIAN_051_NOTICE_PHASE") ?? "first";
        if (phase == "restart")
        {
            Check(LibrarianNoticeHistory051.IsAcknowledged(version), "new process reads confirmed version from disk");
            await Wait(2.6);
            Check(NModalContainer.Instance!.OpenModal is null, "same version does not show on process restart");
            MainFile.Logger.Info("V051_NOTICE_RESTART_PASS");
            return;
        }
        scheduler.SetProcess(false);
        var container = NModalContainer.Instance!;
        Check(container.OpenModal is null, "native main menu initially has no modal");
        var progress = SaveManager.Instance.Progress;
        var epochs = progress.Epochs.Select(e => (e.Id, e.State, e.ObtainDate)).ToArray();
        var markers = progress.FtueCompleted.Order().ToArray();
        string file = LibrarianNoticeHistory051.FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        if (File.Exists(file)) File.Copy(file, file + ".before-051-audit", true);
        File.WriteAllText(file, "{\"AcknowledgedVersions\":[\"0.5.0\"]}");
        LibrarianUpdateNotice051.ResetSessionForAudit();
        Check(!LibrarianNoticeHistory051.IsAcknowledged(version), "prior version does not suppress current update");
        menu.SubmenuStack.PushSubmenuType<NSettingsScreen>();
        scheduler.SetProcess(true);
        await Wait(2.5);
        Check(menu.SubmenuStack.SubmenusOpen && container.OpenModal is null, "settings submenu defers update notice");
        scheduler.SetProcess(false);
        menu.SubmenuStack.Pop();
        await Wait(0.6);
        var blocker = NGenericPopup.Create()!;
        container.Add(blocker);
        blocker.GetNode<NVerticalPopup>("VerticalPopup").SetText("Native modal fixture", "The version notice must wait.");
        scheduler.SetProcess(true);
        await Wait(2.5);
        Check(ReferenceEquals(container.OpenModal, blocker), "existing native modal is not replaced");
        container.Clear(); await Wait(0.5);
        Check(container.OpenModal is NGenericPopup p && p.Name == "LibrarianUpdateNotice", "scheduler shows update at free main-menu slot");
        var popup = (NGenericPopup)container.OpenModal!;
        var panel = popup.GetNode<NVerticalPopup>("VerticalPopup");
        var body = panel.GetNode<RichTextLabel>("Description").GetParsedText();
        Check(body.Contains(version) && body.Contains(LibrarianUpdateNotice051.Email) && body.Contains(LibrarianUpdateNotice051.Qq), "rendered native text contains version email QQ");
        Check(panel.YesButton.Visible && panel.NoButton.Visible, "both action buttons are visible");
        await Capture("update-notice-zhs");
        var beforeSize = DisplayServer.WindowGetSize();
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        await Wait(0.5);
        await Capture("update-notice-zhs-1280");
        DisplayServer.WindowSetSize(beforeSize);
        await Wait(0.3);
        Check(!LibrarianNoticeHistory051.IsAcknowledged(version), "showing alone does not persist acknowledgement");
        container.Clear(); await Wait();
        int opened = 0; string? requestedUrl = null;
        popup = LibrarianUpdateNotice051.Show(version, url => { opened++; requestedUrl = url; })!;
        await Wait(); panel = popup.GetNode<NVerticalPopup>("VerticalPopup");
        panel.NoButton.EmitSignal(NClickableControl.SignalName.Released, panel.NoButton);
        Check(opened == 1 && requestedUrl == LibrarianUpdateNotice051.WorkshopUrl, "workshop button dispatches exact public release URL");
        Check(ReferenceEquals(container.OpenModal, popup) && !LibrarianNoticeHistory051.IsAcknowledged(version), "workshop action leaves dialog open and unconfirmed");
        var bindings = (Dictionary<StringName, List<Action>>)AccessTools.Field(typeof(NHotkeyManager), "_hotkeyReleasedBindings").GetValue(NHotkeyManager.Instance)!;
        bindings[MegaInput.cancel][^1]();
        Check(opened == 1 && container.OpenModal is null && !LibrarianNoticeHistory051.IsAcknowledged(version), "back dismisses without opening URL or confirming");
        await Wait();
        string language = LocManager.Instance.Language;
        try
        {
            LocManager.Instance.SetLanguage("eng");
            popup = LibrarianUpdateNotice051.Show(version, _ => { })!;
            await Wait();
            Check(popup.GetNode<RichTextLabel>("VerticalPopup/Description").GetParsedText().Contains("Current version:"), "English notice localization resolves");
            await Capture("update-notice-eng");
            container.Clear(); await Wait();
        }
        finally { LocManager.Instance.SetLanguage(language); }
        File.WriteAllText(file, "{invalid");
        MainFile.Logger.Info("V051_NOTICE_CORRUPT_INPUT_BEGIN");
        LibrarianUpdateNotice051.ResetSessionForAudit();
        Check(!LibrarianNoticeHistory051.IsAcknowledged(version) && File.ReadAllText(file) == "{invalid", "corrupt history retained and update stays pending");
        popup = LibrarianUpdateNotice051.Show(version, _ => { })!; await Wait();
        panel = popup.GetNode<NVerticalPopup>("VerticalPopup");
        panel.YesButton.EmitSignal(NClickableControl.SignalName.Released, panel.YesButton);
        Check(Directory.GetFiles(Path.GetDirectoryName(file)!, "update-notice.json.invalid-*").Any(), "explicit confirm preserves malformed history backup");
        LibrarianNoticeHistory051.ForgetSessionForAudit();
        Check(LibrarianNoticeHistory051.IsAcknowledged(version), "confirmation persisted and reread from disk");
        MainFile.Logger.Info("V051_NOTICE_CORRUPT_INPUT_RECOVERED");
        await Wait();
        LibrarianPreferences050.Reset();
        Check(LibrarianNoticeHistory051.IsAcknowledged(version), "reset presentation settings keeps update acknowledgement");
        var repeat = new LibrarianUpdateNotice051 { Name = "RepeatNoticeScheduler" };
        menu.AddChild(repeat); await Wait(2.5);
        Check(container.OpenModal is null, "returning to same-version menu does not show again");
        Check(!LibrarianNoticeHistory051.IsAcknowledged(new Version(0, 5, new Version(version).Build + 1).ToString()), "future version remains eligible");
        Check(epochs.SequenceEqual(progress.Epochs.Select(e => (e.Id, e.State, e.ObtainDate)))
            && markers.SequenceEqual(progress.FtueCompleted.Order()), "notice did not alter profile progression");
        MainFile.Logger.Info($"V051_NOTICE_AUDIT_PASS checks={_checks}");
    }
}
