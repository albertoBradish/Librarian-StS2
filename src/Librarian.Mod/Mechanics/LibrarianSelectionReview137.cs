using Godot;
using HarmonyLib;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace Librarian.Mechanics;

/// <summary>Only the explicit, private review launcher requests this native UI shortcut.</summary>
internal static class LibrarianSelectionReview137
{
    private static bool Enabled => System.Environment.GetEnvironmentVariable("LIBRARIAN_SELECTION_REVIEW") == "1";
    private static readonly TaskCompletionSource<Task<bool>> Common = new();
    private static bool _opened;
    [HarmonyPatch(typeof(NAssetLoader), nameof(NAssetLoader.LoadInTheBackground))]
    private static class ObserveCommon
    {
        [HarmonyPostfix] private static void Postfix(AssetLoadingSession session)
        {
            if (Enabled && AccessTools.Field(typeof(AssetLoadingSession), "_name")?.GetValue(session) is "Common") Common.TrySetResult(session.Task);
        }
    }
    [HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
    private static class OpenWhenReady
    {
        [HarmonyPostfix] private static void Postfix(NMainMenu __instance)
        {
            if (!Enabled || _opened) return;
            _opened = true;
            Callable.From((Action)(() => { _ = Open(__instance); })).CallDeferred();
        }
    }
    private static IEnumerable<Node> Desc(Node n)
    {
        foreach (var c in n.GetChildren()) { yield return c; foreach (var d in Desc(c)) yield return d; }
    }
    private static async Task Open(NMainMenu menu)
    {
        try
        {
            await (await Common.Task);
            await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(.3), SceneTreeTimer.SignalName.Timeout);
            if (!GodotObject.IsInstanceValid(menu) || menu.SubmenuStack.SubmenusOpen) return;
            var screen = menu.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();
            screen.InitializeSingleplayer(); menu.SubmenuStack.Push(screen);
            await NGame.Instance.ToSignal(NGame.Instance.GetTree().CreateTimer(.3), SceneTreeTimer.SignalName.Timeout);
            Desc(screen).OfType<NCharacterSelectButton>().Single(b => b.Character is LibrarianCharacter).Select();
            MainFile.Logger.Info("SELECTION139_REVIEW_OPEN nativeCharacterSelect=True automaticRunStart=False");
        }
        catch (Exception e) { MainFile.Logger.Error("Selection review could not open: " + e); }
    }
}
