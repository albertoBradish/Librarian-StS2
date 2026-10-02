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

namespace Librarian.Mechanics;

/// <summary>Stable 0.107.1 backport of the native welcome-notice input and persistence audit.</summary>
internal static class DevelopmentNoticeBeta6Audit
{
    private static int _checks;
    private static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException("stable111 notice: " + label);
        _checks++; MainFile.Logger.Info("BETA6_NOTICE_CHECK_PASS " + label);
    }
    private static async Task Wait(double seconds = .15) => await NGame.Instance!.ToSignal(
        NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private static bool NativeFocused(NPopupYesNoButton button) => (bool)AccessTools.Property(
        typeof(NClickableControl), "IsFocused").GetValue(button)!;
    private static bool OutlineFocused(NPopupYesNoButton button) =>
        button.GetNode<Control>("%Outline").SelfModulate.IsEqualApprox(new Color("f0b400"));
    private static void ControllerIconsHidden(IEnumerable<NPopupYesNoButton> buttons, string phase)
    {
        foreach (var button in buttons)
        {
            var icon = button.GetNodeOrNull<CanvasItem>("%ControllerIcon");
            Check(icon is not null && icon.Modulate.A == 0 && !icon.Visible,
                "stable controller glyph alpha zero and hidden " + phase + " " + button.Name);
        }
    }
    private static void State(NGenericPopup popup, IEnumerable<NPopupYesNoButton> buttons, string phase)
    {
        var focus = popup.GetViewport().GuiGetFocusOwner();
        string states = string.Join(";", buttons.Select(button => $"{button.Name}:gui={button.HasFocus()},native={NativeFocused(button)},"
            + $"hover={AccessTools.Field(typeof(NClickableControl), "_isHovered").GetValue(button)},"
            + $"controllerFocus={AccessTools.Field(typeof(NClickableControl), "_isControllerFocused").GetValue(button)},"
            + $"outline={button.GetNode<Control>("%Outline").SelfModulate},"
            + $"glyphVisible={button.GetNode<CanvasItem>("%ControllerIcon").Visible},"
            + $"glyphAlpha={button.GetNode<CanvasItem>("%ControllerIcon").Modulate.A}"));
        MainFile.Logger.Info($"BETA6_NOTICE_STATE phase={phase} isUsingController={NControllerManager.Instance?.IsUsingController}"
            + $" windowFocused={NGame.IsGameFocusedWindow()}"
            + $" guiFocus={focus?.Name.ToString() ?? "null"} buttons={states}");
    }
    private static Vector2 PixelPoint(Control node, Vector2 local) =>
        node.GetViewport().GetStretchTransform() * node.GetGlobalTransformWithCanvas() * local;
    private static async Task Mouse(Vector2 pixel)
    {
        // Stable ignores warp-generated motion during two controller-switch frames.
        // Wait for that native guard before sending a real pointer-motion fixture.
        if (NControllerManager.Instance?.IsUsingController == true) await Wait(.1);
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
        Check(version == (System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_VERSION") ?? "1.1.1"), "actual loaded stable111 manifest");
        if (System.Environment.GetEnvironmentVariable("LIBRARIAN_BETA6_NOTICE_PHASE") == "restart")
        {
            Check(LibrarianNoticeHistory051.IsAcknowledged(version), "new process reads stable111 suppression from disk");
            await Wait(2.7);
            Check(container.OpenModal is null, "suppressed stable111 notice remains absent after new process startup");
            MainFile.Logger.Info("BETA6_NOTICE_RESTART_PASS");
            return;
        }
        Check(container.OpenModal is null && !menu.SubmenuStack.SubmenusOpen, "free native main-menu modal slot");
        var schedulers = menu.GetChildren().Where(n => n is LibrarianUpdateNotice051 or LibrarianArchitectReview102)
            .Select(n => (node: n, process: n.IsProcessing())).ToArray();
        foreach (var entry in schedulers) entry.node.SetProcess(false);
        var controller = NControllerManager.Instance ?? throw new InvalidOperationException("Native input manager unavailable");
        string language = LibrarianLanguage.Selected;
        var viewport = NGame.Instance!.GetViewport();
        Vector2 originalMouse = viewport.GetStretchTransform() * viewport.GetMousePosition();
        string history = LibrarianNoticeHistory051.FilePath;
        if (File.Exists(history)) File.Copy(history, history + ".before-stable111-notice-audit", true);
        LibrarianNoticeHistory051.Forget(version);
        LibrarianUpdateNotice051.ResetSessionForAudit();
        int opened = 0; string? openedUrl = null;
        try
        {
            foreach (string lang in new[] { "zhs", "eng" })
            {
                LibrarianLanguage.Select(lang);
                await Mouse(new Vector2(12, 12));
                var popup = LibrarianUpdateNotice051.Show(version, url => { opened++; openedUrl = url; })
                    ?? throw new InvalidOperationException("Native stable111 popup unavailable");
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
                Check(!controller.IsUsingController && popup.GetViewport().GuiGetFocusOwner() is null
                    && buttons.All(b => !NativeFocused(b) && !OutlineFocused(b)), "first mouse-mode draw has no default outline " + lang);
                ControllerIconsHidden(buttons, lang + "-first-draw");
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
                Check(opened == priorOpened + 1 && openedUrl == "https://steamcommunity.com/sharedfiles/filedetails/changelog/3811677053",
                    "real mouse release dispatches exact stable change-log URL " + lang);
                Check(ReferenceEquals(container.OpenModal, popup) && !LibrarianNoticeHistory051.IsAcknowledged(version), "update notes click keeps notice open and unsuppressed " + lang);
                await Mouse(idle);
                Check(NGame.IsGameFocusedWindow(), "native window focused for directional input " + lang);
                // Stable has a controller boolean, without beta's keyboard-only
                // input mode. Establish native GUI focus through controller input,
                // then test Godot arrow-key traversal within that focused modal.
                // Stable names cardinal directions North/South/East/West;
                // dPadEast is the native right-direction action in AllControllerInputs.
                Input.ParseInputEvent(new InputEventAction { Action = Controller.dPadEast, Pressed = true });
                Input.ParseInputEvent(new InputEventAction { Action = Controller.dPadEast, Pressed = false });
                await Wait();
                State(popup, buttons, lang + "-controller-detected");
                Check(controller.IsUsingController && panel.YesButton.HasFocus()
                    && NativeFocused(panel.YesButton) && OutlineFocused(panel.YesButton), "controller detection establishes and retains native OK focus " + lang);
                ControllerIconsHidden(buttons, lang + "-controller-detected");
                await Wait();
                Check(panel.YesButton.HasFocus() && OutlineFocused(panel.YesButton), "controller focus persists without mouse input " + lang);
                await Capture("beta6-notice-" + lang + "-controller-focus");
                await PressKey(Godot.Key.Right);
                State(popup, buttons, lang + "-keyboard-next-with-controller-focus");
                Check(controller.IsUsingController && panel.NoButton.HasFocus() && NativeFocused(panel.NoButton)
                    && OutlineFocused(panel.NoButton) && !NativeFocused(panel.YesButton), "stable native arrow-key traversal moves focused modal to next button " + lang);
                ControllerIconsHidden(buttons, lang + "-keyboard-with-controller-focus");
                await Capture("beta6-notice-" + lang + "-keyboard-focus");
                await PressKey(Godot.Key.Left);
                Check(panel.YesButton.HasFocus() && NativeFocused(panel.YesButton), "stable native arrow-key traversal returns to OK " + lang);
                await Mouse(idle);
                State(popup, buttons, lang + "-controller-to-mouse");
                Check(!controller.IsUsingController && popup.GetViewport().GuiGetFocusOwner() is null
                    && buttons.All(b => !NativeFocused(b) && !OutlineFocused(b)), "controller-to-mouse returns to hover-only outline " + lang);
                ControllerIconsHidden(buttons, lang + "-controller-to-mouse");
                await Click(panel.YesButton);
                Check(container.OpenModal is null && !LibrarianNoticeHistory051.IsAcknowledged(version), "OK closes without suppressing notice " + lang);
                await Wait();
            }
            var finalPopup = LibrarianUpdateNotice051.Show(version, _ => { })!;
            await Wait();
            await Click(finalPopup.GetNode<NPopupYesNoButton>("VerticalPopup/NeverShowButton"));
            Check(container.OpenModal is null, "explicit suppression closes native popup");
            LibrarianNoticeHistory051.ForgetSessionForAudit();
            Check(LibrarianNoticeHistory051.IsAcknowledged(version), "explicit stable111 suppression rereads from disk");
            Check(!LibrarianNoticeHistory051.IsAcknowledged(version + "-future"), "later release remains eligible");
            MainFile.Logger.Info($"BETA6_NOTICE_AUDIT_PASS checks={_checks} languages=2 native_mouse=True keyboard=True keyboard_mode=controller_gui_focus controller_input=True live_controller_hardware=False");
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
            await Mouse(originalMouse);
            foreach (var entry in schedulers)
                if (GodotObject.IsInstanceValid(entry.node)) entry.node.SetProcess(entry.process);
        }
    }
}
