using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Timeline;
using MegaCrit.Sts2.Core.Unlocks;

namespace Librarian.Mechanics;

public static class LibrarianUnlocks040
{
    public const string ChoiceMarker = "Librarian.ProgressionChoice.v040";
    public const string AllMarker = "Librarian.ProgressionAll.v040";
    public static string Placeholder => "res://Librarian/images/timeline/v1.0-beta2/notice-" + (LibrarianLanguage.Selected == "zhs" ? "zhs" : "eng") + ".png";
    public static string PortraitPath(string epochId)
    {
        for (int chapter = 1; chapter <= 7; chapter++)
            if (epochId == Id(chapter)) return $"res://Librarian/images/timeline/v1.0.2-beta2/epoch-{chapter:00}.png";
        throw new ArgumentException("Unknown Librarian epoch", nameof(epochId));
    }
    private static bool _initialized;
    private static bool _prompting;
    public static readonly Type[] Types = [typeof(Librarian1Epoch), typeof(Librarian2Epoch), typeof(Librarian3Epoch),
        typeof(Librarian4Epoch), typeof(Librarian5Epoch), typeof(Librarian6Epoch), typeof(Librarian7Epoch)];
    public static readonly EpochEra[] Eras = [EpochEra.Invitation1, EpochEra.Invitation2, EpochEra.Flourish1,
        EpochEra.Flourish2, EpochEra.Flourish3, EpochEra.Blight2, EpochEra.Invitation6];
    private static readonly int[] Positions = new int[7];
    public static string Id(int number) => $"LIBRARIAN_V040_{number}_EPOCH";
    public static int Position(int number) => Positions[number - 1];
    public static readonly string[][] CardGroups =
    [
        ["LIBRARIAN-BURNING_PAGES", "LIBRARIAN-SCATTERED_FLAMES", "LIBRARIAN-IGNITE"],
        ["LIBRARIAN-EVAPORATION", "LIBRARIAN-TIDAL_GRAVITY", "LIBRARIAN-SPROUTING_BULWARK"],
        ["LIBRARIAN-STEAM_BLAST", "LIBRARIAN-RIDGE_WARD", "LIBRARIAN-SPACETIME_TWIST"]
    ];
    public static readonly string[][] RelicGroups =
    [
        ["LIBRARIAN-LIBRARIAN_COMMON_PLACEHOLDER", "LIBRARIAN-LIBRARIAN_UNCOMMON_PLACEHOLDER_ONE", "LIBRARIAN-LIBRARIAN_UNCOMMON_PLACEHOLDER_TWO"],
        ["LIBRARIAN-LIBRARIAN_RARE_PLACEHOLDER_ONE", "LIBRARIAN-LIBRARIAN_RARE_PLACEHOLDER_TWO", "LIBRARIAN-LIBRARIAN_RARE_PLACEHOLDER_THREE"]
    ];

    /// <summary>Call during mod initialization, before native unlock/network caches are constructed.</summary>
    public static void Initialize()
    {
        if (_initialized) return;
        var existing = EpochModel.AllEpochIds.Select(EpochModel.Get).ToList();
        var registry = STS2RitsuLib.Timeline.ModTimelineRegistry.For("Librarian");
        for (int i = 0; i < Types.Length; i++)
        {
            // Append a unique slot in the existing era; never overwrite a native or another mod's slot.
            Positions[i] = existing.Where(e => e.Era == Eras[i]).Select(e => e.EraPosition).DefaultIfEmpty(-1).Max() + 1;
            registry.RegisterEpoch(Types[i]);
        }
        registry.RegisterStory<LibrarianStory040>();
        _initialized = true;
    }

    public static bool Revealed(UnlockState state, int number) =>
        ReferenceEquals(state, UnlockState.all) || state.ToSerializable().UnlockedEpochs.Contains(Id(number));

    public static IEnumerable<CardModel> FilterCards(UnlockState state, IEnumerable<CardModel> cards)
    {
        int[] epochs = [2, 5, 7];
        var locked = CardGroups.Where((_, i) => !Revealed(state, epochs[i])).SelectMany(g => g).ToHashSet();
        return cards.Where(c => !locked.Contains(c.Id.Entry));
    }

    public static void ApplyChoice(ProgressState progress, bool unlockAll)
    {
        foreach (int number in Enumerable.Range(1, 7))
        {
            var existing = progress.Epochs.FirstOrDefault(e => e.Id == Id(number));
            if (existing is null) progress.UnlockSlot(Id(number));
            // Never downgrade previously earned/revealed content, including migrated profiles.
            if (number == 1 || unlockAll) progress.ObtainEpochOverride(Id(number), EpochState.Revealed);
        }
        progress.MarkFtueAsComplete(ChoiceMarker);
        if (unlockAll) progress.MarkFtueAsComplete(AllMarker);
    }

    internal static void MountMenuEntry(NMainMenu menu)
    {
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(menu) || !menu.IsInsideTree()) return;
            var container = menu.GetNode<Control>("MainMenuTextButtons");
            if (container.GetNodeOrNull<NMainMenuTextButton>("LibrarianUnlockButton") is not null) return;
            var button = (NMainMenuTextButton)container.GetNode<NMainMenuTextButton>("SettingsButton").Duplicate((int)Node.DuplicateFlags.Scripts);
            button.Name = "LibrarianUnlockButton";
            container.AddChild(button);
            container.MoveChild(button, container.GetNode<Node>("QuitButton").GetIndex());
            button.SetLocalization("LIBRARIAN_UNLOCK.menu");
            button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => TaskHelper.RunSafely(Prompt(menu, fromMenu: true))));
            button.Connect(NClickableControl.SignalName.Focused, Callable.From<NMainMenuTextButton>(b =>
                AccessTools.Method(typeof(NMainMenu), "MainMenuButtonFocused").Invoke(menu, [b])));
            button.Connect(NClickableControl.SignalName.Unfocused, Callable.From<NMainMenuTextButton>(b =>
                AccessTools.Method(typeof(NMainMenu), "MainMenuButtonUnfocused").Invoke(menu, [b])));
            RefreshMenuEntry(menu);
        }).CallDeferred();
    }

    internal static void RefreshMenuEntry(NMainMenu menu)
    {
        var button = menu.GetNodeOrNull<NMainMenuTextButton>("MainMenuTextButtons/LibrarianUnlockButton");
        if (button is null) return;
        button.Visible = !SaveManager.Instance.Progress.FtueCompleted.Contains(AllMarker);
        button.SetEnabled(button.Visible);
    }

    internal static async Task Prompt(NMainMenu menu, bool fromMenu = false)
    {
        if (!fromMenu && System.Environment.GetEnvironmentVariable("LIBRARIAN_040_ONLY") == "1"
            && OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase)) return;
        await menu.ToSignal(menu.GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_prompting || !GodotObject.IsInstanceValid(menu) || !menu.IsInsideTree()) return;
        var progress = SaveManager.Instance.Progress;
        if (!fromMenu && progress.FtueCompleted.Contains(ChoiceMarker)) return;
        _prompting = true;
        try
        {
            // Vanilla startup disclaimers/mod confirmation own the modal first. Do not drop our popup behind them.
            while (NModalContainer.Instance?.OpenModal is not null)
            {
                await menu.ToSignal(menu.GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!GodotObject.IsInstanceValid(menu) || !menu.IsInsideTree()) return;
            }
            if (NModalContainer.Instance is null || !ReferenceEquals(progress, SaveManager.Instance.Progress)) return;
            bool? all = await ShowChoice(menu, upgradeOnly: fromMenu);
            if (all is null || (fromMenu && all != true) || !ReferenceEquals(progress, SaveManager.Instance.Progress)) return;
            ApplyChoice(progress, all.Value);
            SaveManager.Instance.SaveProgressFile();
            RefreshMenuEntry(menu);
            if (GodotObject.IsInstanceValid(menu) && menu.IsInsideTree())
                menu.GetNode<Control>("MainMenuTextButtons/TimelineButton").Visible = true;
        }
        finally { _prompting = false; }
    }

    /// <summary>Native dialog only. Nullable cancellation is deliberately distinct from either saved preference.</summary>
    internal static async Task<bool?> ShowChoice(NMainMenu menu, bool upgradeOnly = false)
    {
        if (NModalContainer.Instance is null || NModalContainer.Instance.OpenModal is not null) return null;
        var popup = NGenericPopup.Create();
        if (popup is null) return null;
        NModalContainer.Instance.Add(popup);
        var choice = popup.WaitForConfirmation(new LocString("main_menu_ui", upgradeOnly ? "LIBRARIAN_UNLOCK.upgradeBody" : "LIBRARIAN_UNLOCK.body"),
            new LocString("main_menu_ui", "LIBRARIAN_UNLOCK.header"),
            new LocString("main_menu_ui", upgradeOnly ? "LIBRARIAN_UNLOCK.cancel" : "LIBRARIAN_UNLOCK.progressive"),
            new LocString("main_menu_ui", "LIBRARIAN_UNLOCK.all"));
        var closed = new TaskCompletionSource<bool>();
        void OnClosed() => closed.TrySetResult(true);
        void IgnorePress() { }
        void CancelChoice()
        {
            // Closing is not a preference. Leave ChoiceMarker absent so the next menu asks again.
            closed.TrySetResult(true);
            if (ReferenceEquals(NModalContainer.Instance?.OpenModal, popup)) NModalContainer.Instance!.Clear();
        }
        var hotkeys = NHotkeyManager.Instance;
        string[] cancelKeys = [MegaInput.cancel, MegaInput.pauseAndBack];
        popup.TreeExiting += OnClosed;
        menu.TreeExiting += OnClosed;
        // Native No binds Back. Replace only that shortcut, retaining its visible progressive button.
        // Run after native IsYes's deferred hotkey registration so native cannot overwrite this binding.
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(popup) || closed.Task.IsCompleted || choice.IsCompleted) return;
            popup.GetNode<NVerticalPopup>("VerticalPopup").NoButton.DisconnectHotkeys();
            foreach (string key in cancelKeys)
            {
                hotkeys?.PushHotkeyPressedBinding(key, IgnorePress);
                hotkeys?.PushHotkeyReleasedBinding(key, CancelChoice);
            }
        }).CallDeferred();
        try
        {
            await Task.WhenAny(choice, closed.Task);
            return choice.IsCompletedSuccessfully ? await choice : null;
        }
        finally
        {
            if (GodotObject.IsInstanceValid(popup)) popup.TreeExiting -= OnClosed;
            if (GodotObject.IsInstanceValid(menu)) menu.TreeExiting -= OnClosed;
            foreach (string key in cancelKeys)
            {
                hotkeys?.RemoveHotkeyPressedBinding(key, IgnorePress);
                hotkeys?.RemoveHotkeyReleasedBinding(key, CancelChoice);
            }
            if (!choice.IsCompleted && ReferenceEquals(NModalContainer.Instance?.OpenModal, popup))
                NModalContainer.Instance!.Clear();
        }
    }

    internal static void Award(ProgressSaveManager manager, int number, Player player)
    {
        if (!manager.Progress.FtueCompleted.Contains(ChoiceMarker) || player.RunState.GameMode.AreAchievementsAndEpochsLocked()) return;
        AccessTools.Method(typeof(ProgressSaveManager), "TryObtainEpochMidRun").Invoke(manager, [EpochModel.Get(Id(number)), player]);
    }

    internal static int Wins(ProgressState progress, ModelId character, RoomType type) => progress.EncounterStats.Values
        .Where(s => ModelDb.GetByIdOrNull<EncounterModel>(s.Id)?.RoomType == type)
        .Sum(s => s.FightStats.Where(f => f.Character == character).Sum(f => f.Wins));
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache), "Init")]
internal static class LibrarianEpochNetRegistration040
{
    [HarmonyPrefix] private static void Prefix() => LibrarianUnlocks040.Initialize();
}

public abstract class LibrarianEpoch040(int number) : EpochModel
{
    public override string Id => LibrarianUnlocks040.Id(number);
    public override EpochEra Era => LibrarianUnlocks040.Eras[number - 1];
    public override int EraPosition => LibrarianUnlocks040.Position(number);
    public override string StoryId => "LibrarianV040";
    public override void QueueUnlocks()
    {
        int cardGroup = number switch { 2 => 0, 5 => 1, 7 => 2, _ => -1 };
        if (cardGroup >= 0)
            NTimelineScreen.Instance.QueueCardUnlock(ModelDb.CardPool<LibrarianCardPool>().AllCards
                .Where(c => LibrarianUnlocks040.CardGroups[cardGroup].Contains(c.Id.Entry)).ToList());
        else if (number is 3 or 6)
            NTimelineScreen.Instance.QueueRelicUnlock(ModelDb.RelicPool<LibrarianRelicPool>().AllRelics
                .Where(c => LibrarianUnlocks040.RelicGroups[number == 3 ? 0 : 1].Contains(c.Id.Entry)).ToList());
        else if (number == 4)
            NTimelineScreen.Instance.QueuePotionUnlock(ModelDb.PotionPool<LibrarianPotionPool>().AllPotions.ToList());
        else NTimelineScreen.Instance.QueueMiscUnlock(UnlockText);
    }
}
public sealed class Librarian1Epoch() : LibrarianEpoch040(1);
public sealed class Librarian2Epoch() : LibrarianEpoch040(2);
public sealed class Librarian3Epoch() : LibrarianEpoch040(3);
public sealed class Librarian4Epoch() : LibrarianEpoch040(4);
public sealed class Librarian5Epoch() : LibrarianEpoch040(5);
public sealed class Librarian6Epoch() : LibrarianEpoch040(6);
public sealed class Librarian7Epoch() : LibrarianEpoch040(7);
public sealed class LibrarianStory040 : StoryModel
{
    protected override string Id => StringHelper.Slugify("LibrarianV040");
    public override EpochModel[] Epochs => Enumerable.Range(1, 7).Select(n => EpochModel.Get(LibrarianUnlocks040.Id(n))).ToArray();
}

[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class LibrarianUnlockChoice040
{
    [HarmonyPostfix] private static void Postfix(NMainMenu __instance)
    {
        LibrarianSettings041.EnsureProgressiveProfile();
    }
}

[HarmonyPatch(typeof(ProgressSaveManager), nameof(ProgressSaveManager.UpdateAfterCombatWon))]
internal static class LibrarianProgressCombat040
{
    [HarmonyPostfix] private static void Postfix(ProgressSaveManager __instance, Player localPlayer, CombatRoom room)
    {
        if (localPlayer.Character is not LibrarianCharacter) return;
        if (room.RoomType == RoomType.Boss && localPlayer.RunState.CurrentActIndex is >= 0 and <= 2)
            LibrarianUnlocks040.Award(__instance, localPlayer.RunState.CurrentActIndex + 2, localPlayer);
        if (LibrarianUnlocks040.Wins(__instance.Progress, localPlayer.Character.Id, RoomType.Elite) >= 15)
            LibrarianUnlocks040.Award(__instance, 5, localPlayer);
        if (LibrarianUnlocks040.Wins(__instance.Progress, localPlayer.Character.Id, RoomType.Boss) >= 15)
            LibrarianUnlocks040.Award(__instance, 6, localPlayer);
    }
}

[HarmonyPatch(typeof(ProgressSaveManager), "PostRunCharacterEpochChecks")]
internal static class LibrarianProgressVictory040
{
    [HarmonyPostfix] private static void Postfix(ProgressSaveManager __instance, SerializablePlayer serializablePlayer,
        SerializableRun serializableRun, bool victory)
    {
        if (!victory || serializableRun.Ascension != 1 || serializablePlayer.CharacterId != ModelDb.Character<LibrarianCharacter>().Id ||
            !__instance.Progress.FtueCompleted.Contains(LibrarianUnlocks040.ChoiceMarker)) return;
        AccessTools.Method(typeof(ProgressSaveManager), "TryObtainEpochPostRun").Invoke(__instance,
            [EpochModel.Get(LibrarianUnlocks040.Id(7)), serializablePlayer, serializableRun]);
    }
}

[HarmonyPatch(typeof(RelicPoolModel), nameof(RelicPoolModel.GetUnlockedRelics))]
internal static class LibrarianRelicUnlock040
{
    [HarmonyPostfix] private static void Postfix(RelicPoolModel __instance, UnlockState unlockState, ref IEnumerable<RelicModel> __result)
    {
        if (__instance is not LibrarianRelicPool) return;
        int[] epochs = [3, 6];
        var locked = LibrarianUnlocks040.RelicGroups.Where((_, i) => !LibrarianUnlocks040.Revealed(unlockState, epochs[i])).SelectMany(g => g).ToHashSet();
        __result = __result.Where(c => !locked.Contains(c.Id.Entry)).ToArray();
    }
}

[HarmonyPatch(typeof(PotionPoolModel), nameof(PotionPoolModel.GetUnlockedPotions))]
internal static class LibrarianPotionUnlock040
{
    [HarmonyPostfix] private static void Postfix(PotionPoolModel __instance, UnlockState unlockState, ref IEnumerable<PotionModel> __result)
    {
        if (__instance is LibrarianPotionPool && !LibrarianUnlocks040.Revealed(unlockState, 4)) __result = [];
    }
}

[HarmonyPatch(typeof(EpochModel), "get_Portrait")]
internal static class LibrarianEpochPortrait040
{
    [HarmonyPrefix] private static bool Prefix(EpochModel __instance, ref Texture2D __result)
    {
        if (__instance is not LibrarianEpoch040) return true;
        __result = ResourceLoader.Load<Texture2D>(LibrarianUnlocks040.PortraitPath(__instance.Id));
        return false;
    }
}

[HarmonyPatch(typeof(EpochModel), "get_ResolvedPortraitPath")]
internal static class LibrarianEpochRealPortrait040
{
    [HarmonyPrefix] private static bool Prefix(EpochModel __instance, ref string __result)
    {
        if (__instance is not LibrarianEpoch040) return true;
        __result = LibrarianUnlocks040.PortraitPath(__instance.Id);
        return false;
    }
}

[HarmonyPatch(typeof(EpochModel), "get_RealPortraitPath")]
internal static class LibrarianEpochSourcePortrait102Beta2
{
    [HarmonyPrefix] private static bool Prefix(EpochModel __instance, ref string __result)
    {
        if (__instance is not LibrarianEpoch040) return true;
        __result = LibrarianUnlocks040.PortraitPath(__instance.Id);
        return false;
    }
}

internal static class LibrarianUnlockMenuRefresh040
{
    [HarmonyPostfix] private static void Postfix(NMainMenu __instance) => LibrarianUnlocks040.RefreshMenuEntry(__instance);
}
