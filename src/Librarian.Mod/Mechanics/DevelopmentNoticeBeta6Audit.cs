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
using MegaCrit.Sts2.Core.Saves;

namespace Librarian.Mechanics;

/// <summary>Opt-in native input and persistence checks for the beta6 welcome notice.</summary>
internal static class DevelopmentNoticeBeta6Audit
{
    private static int _checks;
    private static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException("beta6 notice: " + label);
        _checks++; MainFile.Logger.Info("BETA6_NOTICE_CHECK_PASS " + label);
    }
    private static async Task Wait(double seconds = .15) => await NGame.Instance!.ToSignal(
        NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private static bool NativeFocused(NPopupYesNoButton button) => (bool)AccessTools.Property(
        typeof(NClickableControl), "IsFocused").GetValue(button)!;
    private static bool OutlineFocused(NPopupYesNoButton button) =>
        button.GetNode<Control>("%Outline").SelfModulate.IsEqualApprox(new Color("f0b400"));
    private static void State(NGenericPopup popup, IEnumerable<NPopupYesNoButton> buttons, string phase)
    {
        var focus = popup.GetViewport().GuiGetFocusOwner();
        string states = string.Join(";", buttons.Select(button => $"{button.Name}:gui={button.HasFocus()},native={NativeFocused(button)},"
            + $"hover={AccessTools.Field(typeof(NClickableControl), "_isHovered").GetValue(button)},"
            + $"controllerFocus={AccessTools.Field(typeof(NClickableControl), "_isControllerFocused").GetValue(button)},"
            + $"outline={button.GetNode<Control>("%Outline").SelfModulate}"));
#if LIBRARIAN_API_0111
        string inputMode = $"input={NControllerManager.Instance?.InputType} keyboardMode={SaveManager.Instance.PrefsSave.KeyboardMode}";
#else
        string inputMode = $"controllerMode={NControllerManager.Instance?.IsUsingController} keyboardMode=unsupported";
#endif
        MainFile.Logger.Info($"BETA6_NOTICE_STATE phase={phase} {inputMode} windowFocused={NGame.IsGameFocusedWindow()}"
            + $" guiFocus={focus?.Name.ToString() ?? "null"} buttons={states}");
    }
    private static Vector2 PixelPoint(Control node, Vector2 local) =>
        node.GetViewport().GetStretchTransform() * node.GetGlobalTransformWithCanvas() * local;
    private static async Task Mouse(Vector2 pixel)
    {
        Input.WarpMouse(pixel);
        Input.ParseInputEvent(new InputEventMouseMotion
        {
            Position = pixel, GlobalPosition = pixel, Relative = new Vector2(12, 0), Velocity = new Vector2(240, 0)
        });
        await Wait();
    }
    private static async Task PressKey(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        await Wait();
    }
    private static async Task Click(NPopupYesNoButton button)
    {
        Vector2 point = PixelPoint(button, button.Size * .5f);
        await Mouse(point);
        foreach (bool pressed in new[] { true, false })
            Input.ParseInputEvent(new InputEventMouseButton
            {
                Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = pressed
            });
        await Wait();
    }
    private static async Task Capture(string name)
    {
        string output = System.Environment.GetEnvironmentVariable("LIBRARIAN_TEXT_AUDIT_OUTPUT")
            ?? System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT")
            ?? throw new InvalidOperationException("Beta6 notice audit output is required");
        Directory.CreateDirectory(output);
        await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
        Check(image.SavePng(Path.Combine(output, name + ".png")) == Error.Ok, "native capture " + name);
    }

    internal static async Task Run(NMainMenu menu)
    {
        Check(System.Environment.GetEnvironmentVariable("LIBRARIAN_RUNTIME_AUDIT") == "1"
            && OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase), "isolated opt-in profile");
        var container = NModalContainer.Instance!;
        string version = LibrarianUpdateNotice051.CurrentVersion ?? throw new InvalidOperationException("Loaded manifest unavailable");
        Check(version == (System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_VERSION") ?? "1.1.0-beta6"), "actual loaded beta6 manifest");
        if (System.Environment.GetEnvironmentVariable("LIBRARIAN_BETA6_NOTICE_PHASE") == "restart")
        {
            Check(LibrarianNoticeHistory051.IsAcknowledged(version), "new process reads beta6 suppression from disk");
            await Wait(2.7);
            Check(container.OpenModal is null, "suppressed beta6 notice remains absent after new process startup");
            MainFile.Logger.Info("BETA6_NOTICE_RESTART_PASS");
            return;
        }
        Check(container.OpenModal is null && !menu.SubmenuStack.SubmenusOpen, "free native main-menu modal slot");
        var schedulers = menu.GetChildren().Where(n => n is LibrarianUpdateNotice051 or LibrarianArchitectReview102)
            .Select(n => (node: n, process: n.IsProcessing())).ToArray();
        foreach (var entry in schedulers) entry.node.SetProcess(false);
        var controller = NControllerManager.Instance ?? throw new InvalidOperationException("Native input manager unavailable");
        string language = LibrarianLanguage.Selected;
#if LIBRARIAN_API_0111
        bool originalKeyboardMode = SaveManager.Instance.PrefsSave.KeyboardMode;
#endif
        var viewport = NGame.Instance!.GetViewport();
        Vector2 originalMouse = viewport.GetStretchTransform() * viewport.GetMousePosition();
        string history = LibrarianNoticeHistory051.FilePath;
        if (File.Exists(history)) File.Copy(history, history + ".before-beta6-audit", true);
        LibrarianNoticeHistory051.Forget(version);
        LibrarianUpdateNotice051.ResetSessionForAudit();
        int opened = 0; string? openedUrl = null;
        try
        {
            foreach (string lang in new[] { "zhs", "eng" })
            {
                LibrarianLanguage.Select(lang);
                LibrarianGameApi.ForceMouseMode(controller);
                await Mouse(new Vector2(12, 12));
                var popup = LibrarianUpdateNotice051.Show(version, url => { opened++; openedUrl = url; })
                    ?? throw new InvalidOperationException("Native beta6 popup unavailable");
                await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var panel = popup.GetNode<NVerticalPopup>("VerticalPopup");
                var suppress = panel.GetNode<NPopupYesNoButton>("NeverShowButton");
                NPopupYesNoButton[] buttons = [panel.NoButton, suppress, panel.YesButton];
                Vector2 idle = PixelPoint(panel, new Vector2(30, 30));
                string text = panel.GetNode<RichTextLabel>("Description").GetParsedText();
                string feedbackEmail = new LocString("main_menu_ui", "LIBRARIAN_UPDATE.email").GetRawText();
                Check(feedbackEmail == (lang == "zhs" ? "aery_bradish@163.com" : "lrq1850562239@gmail.com"), "existing localized feedback address retained " + lang);
                Check(text.Contains(version) && text.Contains(feedbackEmail), "version and localized feedback rendered " + lang);
                Check(!text.Contains("仅对本机当前版本生效") && !text.Contains("applies to this version on this computer"), "redundant suppression sentence absent " + lang);
                Check(panel.NoButton.GetNode<Label>("%Label").Text == (lang == "zhs" ? "更新公告" : "Update notes"), "update notes action label " + lang);
                Check(!text.Contains("点击“创意工坊”") && !text.Contains("Use Steam Workshop below"), "obsolete dependency-link instruction absent " + lang);
                State(popup, buttons, lang + "-first-draw");
                Check(!LibrarianGameApi.IsUsingDirectionalNavigation(controller) && popup.GetViewport().GuiGetFocusOwner() is null
                    && buttons.All(b => !NativeFocused(b) && !OutlineFocused(b)), "first mouse-mode draw has no default outline " + lang);
                // Preserve the first-draw state check above, then wait out the
                // native two-second main-menu fade before recording visual evidence.
                await Wait(2.2);
                await Capture("beta6-notice-" + lang + "-initial-mouse");
                foreach (var button in buttons)
                {
                    await Mouse(PixelPoint(button, button.Size * .5f));
                    Check(NativeFocused(button) && OutlineFocused(button)
                        && buttons.Where(b => b != button).All(b => !NativeFocused(b) && !OutlineFocused(b)), "only hovered native button lights " + lang + " " + button.Name);
                    await Capture("beta6-notice-" + lang + "-hover-" + button.Name);
                    await Mouse(idle);
                    Check(buttons.All(b => !NativeFocused(b) && !OutlineFocused(b)), "all outlines clear after mouse exit " + lang + " " + button.Name);
                }
                int priorOpened = opened;
                await Click(panel.NoButton);
                Check(opened == priorOpened + 1 && openedUrl == "https://steamcommunity.com/sharedfiles/filedetails/changelog/3801958367",
                    "real mouse release dispatches exact beta change-log URL " + lang);
                Check(ReferenceEquals(container.OpenModal, popup) && !LibrarianNoticeHistory051.IsAcknowledged(version), "update notes click keeps notice open and unsuppressed " + lang);
                await Mouse(idle);
                Check(NGame.IsGameFocusedWindow(), "native window focused for directional input " + lang);
                // Native arrow-key mode is explicitly opt-in through this preference.
                // Keep the fixture in memory and restore it without saving preferences.
#if LIBRARIAN_API_0111
                SaveManager.Instance.PrefsSave.KeyboardMode = true;
                await PressKey(Godot.Key.Right);
                State(popup, buttons, lang + "-keyboard-detected");
                Check(controller.InputType == InputType.KeyboardOnlyMode && panel.YesButton.HasFocus()
                    && NativeFocused(panel.YesButton) && OutlineFocused(panel.YesButton), "arrow-key mode establishes native OK focus " + lang);
                await Capture("beta6-notice-" + lang + "-keyboard-focus");
                await PressKey(Godot.Key.Right);
                State(popup, buttons, lang + "-keyboard-next");
                Check(panel.NoButton.HasFocus() && NativeFocused(panel.NoButton) && OutlineFocused(panel.NoButton)
                    && !NativeFocused(panel.YesButton), "native directional navigation moves focus to next button " + lang);
                await Mouse(idle);
                State(popup, buttons, lang + "-keyboard-to-mouse");
                Check(controller.InputType == InputType.MouseAndKeyboard && popup.GetViewport().GuiGetFocusOwner() is null
                    && buttons.All(b => !NativeFocused(b) && !OutlineFocused(b)), "mouse motion clears directional focus " + lang);
#endif
#if LIBRARIAN_API_0107
                var direction = Controller.dPadEast;
#else
                var direction = Controller.dPadRight;
#endif
                Input.ParseInputEvent(new InputEventAction { Action = direction, Pressed = true });
                Input.ParseInputEvent(new InputEventAction { Action = direction, Pressed = false });
                await Wait();
                State(popup, buttons, lang + "-controller-detected");
                Check(LibrarianGameApi.IsUsingDirectionalNavigation(controller) && panel.YesButton.HasFocus()
                    && NativeFocused(panel.YesButton) && OutlineFocused(panel.YesButton), "controller detection establishes and retains native OK focus " + lang);
                await Wait();
                Check(panel.YesButton.HasFocus() && OutlineFocused(panel.YesButton), "controller focus persists without mouse input " + lang);
                await Capture("beta6-notice-" + lang + "-controller-focus");
                await Mouse(idle);
                Check(buttons.All(b => !NativeFocused(b) && !OutlineFocused(b)), "controller-to-mouse returns to hover-only outline " + lang);
                await Click(panel.YesButton);
                Check(container.OpenModal is null && !LibrarianNoticeHistory051.IsAcknowledged(version), "OK closes without suppressing notice " + lang);
                await Wait();
            }
            var finalPopup = LibrarianUpdateNotice051.Show(version, _ => { })!;
            await Wait();
            await Click(finalPopup.GetNode<NPopupYesNoButton>("VerticalPopup/NeverShowButton"));
            Check(container.OpenModal is null, "explicit suppression closes native popup");
            LibrarianNoticeHistory051.ForgetSessionForAudit();
            Check(LibrarianNoticeHistory051.IsAcknowledged(version), "explicit beta6 suppression rereads from disk");
            Check(!LibrarianNoticeHistory051.IsAcknowledged(version + "-future"), "later release remains eligible");
            MainFile.Logger.Info($"BETA6_NOTICE_AUDIT_PASS checks={_checks} languages=2 native_mouse=True keyboard=" +
#if LIBRARIAN_API_0111
                "True" +
#else
                "unsupported_on_0107" +
#endif
                " controller_input=True live_controller_hardware=False");
        }
        catch
        {
            if (container.OpenModal is NGenericPopup failed && GodotObject.IsInstanceValid(failed))
            {
                try
                {
                    var panel = failed.GetNode<NVerticalPopup>("VerticalPopup");
                    State(failed, [panel.NoButton, panel.GetNode<NPopupYesNoButton>("NeverShowButton"), panel.YesButton], "failure");
                    await Capture("beta6-notice-failure-" + LibrarianLanguage.Selected);
                }
                catch (Exception captureError)
                {
                    MainFile.Logger.Warn("Beta6 notice failure evidence unavailable: " + captureError);
                }
            }
            throw;
        }
        finally
        {
            container.Clear();
            LibrarianLanguage.Select(language);
#if LIBRARIAN_API_0111
            SaveManager.Instance.PrefsSave.KeyboardMode = originalKeyboardMode;
#endif
            LibrarianGameApi.ForceMouseMode(controller);
            Input.WarpMouse(originalMouse);
            foreach (var entry in schedulers)
                if (GodotObject.IsInstanceValid(entry.node)) entry.node.SetProcess(entry.process);
        }
    }
}
