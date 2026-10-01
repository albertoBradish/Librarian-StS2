global using Librarian.Compatibility;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using System.Threading;
using Godot;
using STS2RitsuLib.Settings;
using Librarian.Mechanics;

namespace Librarian.Compatibility;

/// <summary>Game 0.107.1 API differences only; card rules stay in their original classes.</summary>
internal static class LibrarianStableApi
{
    // RitsuLib 0.6.2 can rebuild visible dropdown rows with their option indices
    // still -1 under this native engine. Repair only our own settings page on click.
    [HarmonyPatch(typeof(ModSettingsDropdownChoiceControl<string>), "OnVirtualPoolRowActivated")]
    private static class OwnLanguageDropdown
    {
        private static readonly System.Reflection.FieldInfo Indices = AccessTools.Field(typeof(ModSettingsDropdownChoiceControl<string>), "_slotOptionIndex");
        private static readonly System.Reflection.MethodInfo Sync = AccessTools.Method(typeof(ModSettingsDropdownChoiceControl<string>), "SyncVirtualDropdownRows");
        private static readonly System.Reflection.FieldInfo Mod = AccessTools.Field(typeof(RitsuModSettingsSubmenu), "_selectedModId");
        private static readonly System.Reflection.FieldInfo Page = AccessTools.Field(typeof(RitsuModSettingsSubmenu), "_selectedPageId");
        private static void Prefix(ModSettingsDropdownChoiceControl<string> __instance, int slotIndex)
        {
            if (System.Environment.GetEnvironmentVariable("LIBRARIAN_LANGUAGE_AUDIT") == "1")
                Librarian.LibrarianCode.MainFile.Logger.Info("STABLE_DROPDOWN_CLICK slot=" + slotIndex);
            var indices = (int[])Indices.GetValue(__instance)!;
            if (slotIndex < 0 || slotIndex >= indices.Length || indices[slotIndex] >= 0) return;
            for (Node? node = __instance.GetParent(); node is not null; node = node.GetParent())
                if (node is RitsuModSettingsSubmenu menu)
                {
                    if (System.Environment.GetEnvironmentVariable("LIBRARIAN_LANGUAGE_AUDIT") == "1")
                        Librarian.LibrarianCode.MainFile.Logger.Info("STABLE_DROPDOWN_OWNER " + Mod.GetValue(menu) + "/" + Page.GetValue(menu));
                    if ((string?)Mod.GetValue(menu) == "Librarian" && (string?)Page.GetValue(menu) == LibrarianSettings041.PageId)
                        Sync.Invoke(__instance, null);
                    return;
                }
        }
    }

    // Enum choices create a different closed generic control, so the language
    // dropdown patch does not cover the new lock display selector.
    [HarmonyPatch(typeof(ModSettingsDropdownChoiceControl<LibrarianLockedOrbDisplayMode>), "OnVirtualPoolRowActivated")]
    private static class OwnLockedOrbDisplayDropdown
    {
        private static readonly System.Reflection.FieldInfo Indices = AccessTools.Field(typeof(ModSettingsDropdownChoiceControl<LibrarianLockedOrbDisplayMode>), "_slotOptionIndex");
        private static readonly System.Reflection.MethodInfo Sync = AccessTools.Method(typeof(ModSettingsDropdownChoiceControl<LibrarianLockedOrbDisplayMode>), "SyncVirtualDropdownRows");
        private static readonly System.Reflection.FieldInfo Mod = AccessTools.Field(typeof(RitsuModSettingsSubmenu), "_selectedModId");
        private static readonly System.Reflection.FieldInfo Page = AccessTools.Field(typeof(RitsuModSettingsSubmenu), "_selectedPageId");
        private static void Prefix(ModSettingsDropdownChoiceControl<LibrarianLockedOrbDisplayMode> __instance, int slotIndex)
        {
            var indices = (int[])Indices.GetValue(__instance)!;
            if (slotIndex < 0 || slotIndex >= indices.Length || indices[slotIndex] >= 0) return;
            for (Node? node = __instance.GetParent(); node is not null; node = node.GetParent())
                if (node is RitsuModSettingsSubmenu menu)
                {
                    if ((string?)Mod.GetValue(menu) == "Librarian" && (string?)Page.GetValue(menu) == LibrarianSettings041.PageId)
                        Sync.Invoke(__instance, null);
                    return;
                }
        }
    }
    // Stable derives the attacker and source from the card itself.
    internal static AttackCommand FromCard(this AttackCommand command, CardModel card, CardPlay play)
        => command.FromCard(card);

    internal static async Task DrawAlly(PlayerChoiceContext parent, decimal count, Player ally, CardModel source)
    {
        var choice = new HookPlayerChoiceContext(ally, LocalContext.NetId!.Value, GameActionType.Combat);
        choice.PushModel(source);
        await choice.AssignTaskAndWaitForPauseOrCompletion(CardPileCmd.Draw(choice, count, ally));
    }

    private sealed class ExhaustObservation(CardModel card)
    {
        internal readonly CardModel Card = card;
        internal bool Succeeded;
        internal bool Captured;
    }
    private static readonly AsyncLocal<ExhaustObservation?> ExhaustScope = new();

    internal static async Task<bool> ExhaustSucceeded(PlayerChoiceContext context, CardModel card)
    {
        var parent = ExhaustScope.Value;
        var observation = new ExhaustObservation(card);
        ExhaustScope.Value = observation;
        try { await CardCmd.Exhaust(context, card); return observation.Succeeded; }
        finally { ExhaustScope.Value = parent; }
    }

    // Observe the native move result before after-exhaust effects can move the card again.
    // Stable only redirects a full Hand destination; Exhaust remains the requested pile.
    [HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Add),
        [typeof(CardModel), typeof(PileType), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool)])]
    private static class ObserveExhaustMove
    {
        private static void Postfix(CardModel card, PileType newPileType, ref Task<CardPileAddResult> __result)
        {
            var scope = ExhaustScope.Value;
            if (scope is null || scope.Captured || newPileType != PileType.Exhaust || !ReferenceEquals(scope.Card, card)) return;
            scope.Captured = true;
            __result = Observe(__result, scope);
        }
        private static async Task<CardPileAddResult> Observe(Task<CardPileAddResult> task, ExhaustObservation scope)
        {
            var result = await task;
            scope.Succeeded = result.success;
            return result;
        }
    }
}
