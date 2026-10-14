global using Librarian.Compatibility;
using System.Runtime.CompilerServices;
using System.Threading;
using Godot;
using HarmonyLib;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Timeline;

namespace Librarian.Compatibility;

/// <summary>Fixed game API differences; both builds use the same gameplay and model identities.</summary>
internal static class LibrarianGameApi
{
#if LIBRARIAN_API_0107 && LIBRARIAN_API_0111
#error Select exactly one fixed Librarian game API target.
#elif !LIBRARIAN_API_0107 && !LIBRARIAN_API_0111
#error Define LIBRARIAN_API_0107 or LIBRARIAN_API_0111.
#endif

    internal static string GameVersion =>
#if LIBRARIAN_API_0107
        MegaCrit.Sts2.Core.Debug.ReleaseInfoManager.Instance.ReleaseInfo?.Version ?? "UNKNOWN";
#else
        MegaCrit.Sts2.Core.Nodes.NGame.GetGameVersion();
#endif

    internal static IEnumerable<EpochModel> AllEpochs => EpochModel.AllEpochIds.Select(EpochModel.Get);

    internal static bool IsUsingDirectionalNavigation(NControllerManager? controller)
#if LIBRARIAN_API_0107
        => controller?.IsUsingController == true;
#else
        => controller?.IsUsingDirectionalNavigation == true;
#endif

    internal static void ForceMouseMode(NControllerManager controller)
    {
#if LIBRARIAN_API_0107
        // The stable controller manager has no public mode setter. Its native
        // mouse handler releases focus, emits MouseDetected and refreshes glyphs.
        AccessTools.Method(typeof(NControllerManager), "CheckForMouseInput")
            .Invoke(controller, [new InputEventMouseButton { ButtonIndex = MouseButton.Left }]);
#else
        controller.ForceMouseMode();
#endif
    }

    internal static Task DrawAlly(PlayerChoiceContext parent, decimal count, Player ally, CardModel source)
    {
#if LIBRARIAN_API_0107
        return DrawOwned();
        async Task DrawOwned()
        {
            var choice = new HookPlayerChoiceContext(ally, LocalContext.NetId!.Value, GameActionType.Combat);
            choice.PushModel(source);
            await choice.AssignTaskAndWaitForPauseOrCompletion(CardPileCmd.Draw(choice, count, ally));
        }
#else
        return CardPileCmd.DrawWithoutBlockingOnOtherPlayers(parent, count, ally, source);
#endif
    }

    internal static async Task AutoPlay(PlayerChoiceContext context, Player player, CardModel card)
    {
#if LIBRARIAN_API_0107
        // A stable singleton hook has no owner. Only hook-origin plays need a
        // new native action; nested plays must keep their current action context.
        if (context is HookPlayerChoiceContext hook &&
            (hook.Source is LibrarianCombatHooks || !ReferenceEquals(hook.Owner, player)))
        {
            var owned = new HookPlayerChoiceContext(card, LocalContext.NetId!.Value,
                player.Creature.CombatState!, GameActionType.Combat);
            await owned.AssignTaskAndWaitForPauseOrCompletion(CardCmd.AutoPlay(owned, card, target: null));
            await owned.WaitForCompletion();
            return;
        }
#endif
        await CardCmd.AutoPlay(context, card, target: null);
    }

    internal static async Task<bool> ExhaustSucceeded(PlayerChoiceContext context, CardModel card)
    {
#if LIBRARIAN_API_0107
        var parent = ExhaustScope.Value;
        var observation = new ExhaustObservation(card);
        ExhaustScope.Value = observation;
        try { await CardCmd.Exhaust(context, card); return observation.Succeeded; }
        finally { ExhaustScope.Value = parent; }
#else
        return await CardCmd.Exhaust(context, card) is { success: true, targetPile: PileType.Exhaust };
#endif
    }

    internal static Player PlayingPlayer(CardPlay play)
#if LIBRARIAN_API_0107
        => PlayingPlayers.TryGetValue(play, out var snapshot) ? snapshot.Player : play.Card.Owner;
#else
        => play.Player;
#endif

#if LIBRARIAN_API_0107
    internal static AttackCommand FromCard(this AttackCommand command, CardModel card, CardPlay play)
        => command.FromCard(card);

    private sealed record PlayingPlayerSnapshot(Player Player);
    private static readonly ConditionalWeakTable<CardPlay, PlayingPlayerSnapshot> PlayingPlayers = new();

    // Capture before the first model hook can transfer ownership. Weak keys
    // follow the native CardPlay lifetime without retaining finished runs.
    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), "BeforeCardPlayed")]
    private static class CapturePlayingPlayer
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(CardPlay cardPlay)
            => PlayingPlayers.GetValue(cardPlay, p => new PlayingPlayerSnapshot(p.Card.Owner));
    }

    private sealed class ExhaustObservation(CardModel card)
    {
        internal readonly CardModel Card = card;
        internal bool Succeeded;
        internal bool Captured;
    }
    private static readonly AsyncLocal<ExhaustObservation?> ExhaustScope = new();

    // Observe the native result before after-exhaust hooks can move the card again.
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
#endif
}
