using Godot;
using HarmonyLib;
using Librarian.LibrarianCode;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Saves;

namespace Librarian.Mechanics;

/// <summary>Runs only when explicitly called by the isolated visual harness; never changes saved progression.</summary>
internal static class DevelopmentRevision040UnlockUiAudit
{
    internal static async Task Run(NMainMenu menu)
    {
        static void Require(bool value, string name)
        {
            if (!value) throw new InvalidOperationException("040 unlock UI: " + name);
            MainFile.Logger.Info("REVISION040_UNLOCK_UI_PASS " + name);
        }
        Require(OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase), "isolated profile only");
        Require(NModalContainer.Instance?.OpenModal is null, "no existing modal");
        var progress = SaveManager.Instance.Progress;
        var epochs = progress.Epochs.Select(e => (e.Id, e.State, e.ObtainDate)).ToArray();
        var markers = progress.FtueCompleted.Order().ToArray();

        async Task Frame() => await menu.ToSignal(menu.GetTree(), SceneTree.SignalName.ProcessFrame);
        async Task<NGenericPopup> ReadyPopup()
        {
            await Frame();
            await Frame(); // Native button hotkeys and our replacement are both deferred.
            Require(NModalContainer.Instance?.OpenModal is NGenericPopup, "uses native confirmation popup");
            return (NGenericPopup)NModalContainer.Instance!.OpenModal!;
        }

        await Frame();
        await Frame();
        var menuEntry = menu.GetNodeOrNull<NMainMenuTextButton>("MainMenuTextButtons/LibrarianUnlockButton");
        Require(menuEntry is not null, "native main-menu unlock entry mounted");
        Require(menuEntry!.Visible == !progress.FtueCompleted.Contains(LibrarianUnlocks040.AllMarker), "menu entry reflects current profile choice");
        var upgrade = LibrarianUnlocks040.ShowChoice(menu, upgradeOnly: true);
        var upgradePopup = await ReadyPopup();
        var cancelButton = upgradePopup.GetNode<NVerticalPopup>("VerticalPopup").NoButton;
        cancelButton.EmitSignal(NClickableControl.SignalName.Released, cancelButton);
        Require(await upgrade == false, "upgrade confirmation cancel does not unlock");
        await Frame();

        foreach (string cancelKey in new[] { MegaInput.cancel, MegaInput.pauseAndBack })
        {
            var task = LibrarianUnlocks040.ShowChoice(menu);
            await ReadyPopup();
            // Invoke precisely the top binding native _UnhandledInput dispatches, without platform focus dependence.
            var bindings = (Dictionary<StringName, List<Action>>)AccessTools.Field(typeof(NHotkeyManager), "_hotkeyReleasedBindings").GetValue(NHotkeyManager.Instance)!;
            Require(bindings.TryGetValue(cancelKey, out var stack) && stack.Count > 0, "back binding present " + cancelKey);
            stack![^1]();
            Require(await task is null, "back dismisses without implicit preference " + cancelKey);
            await Frame();
        }

        foreach (bool unlockAll in new[] { false, true })
        {
            var task = LibrarianUnlocks040.ShowChoice(menu);
            var popup = await ReadyPopup();
            var vertical = popup.GetNode<NVerticalPopup>("VerticalPopup");
            var button = unlockAll ? vertical.YesButton : vertical.NoButton;
            button.EmitSignal(NClickableControl.SignalName.Released, button);
            Require(await task == unlockAll, "visible button returns explicit preference " + unlockAll);
            await Frame();
        }
        {
            var task = LibrarianUnlocks040.ShowChoice(menu);
            await ReadyPopup();
            NModalContainer.Instance!.Clear();
            Require(await task is null, "external close does not persist a preference");
            await Frame();
        }
        Require(ReferenceEquals(progress, SaveManager.Instance.Progress), "same isolated profile");
        Require(epochs.SequenceEqual(progress.Epochs.Select(e => (e.Id, e.State, e.ObtainDate))), "UI audit did not modify epochs");
        Require(markers.SequenceEqual(progress.FtueCompleted.Order()), "UI audit did not modify preference markers");
    }
}
