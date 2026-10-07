using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace Librarian.Mechanics;

/// <summary>
/// Shared bottom-card protocol. Empty bottom reads normally do not shuffle.
/// ReadBackward explicitly opts into native shuffling before its one read in v0.4.3.
/// Top draws use the native draw command and its normal shuffle behavior.
/// </summary>
public static class LibrarianBottomPlay041
{
    /// <summary>v043 ReadBackward alone follows Havoc's empty-pile shuffle, then reads the bottom once.</summary>
    public static async Task<BottomPlayResult> ReadBackwardAsync(PlayerChoiceContext context, Player player)
    {
        if (CombatManager.Instance.IsOverOrEnding || player.Creature.IsDead) return BottomPlayResult.Empty;
        await CardPileCmd.ShuffleIfNecessary(context, player);
        return await TryPlayBottomAsync(context, player);
    }
    public sealed record BottomPlayResult(CardModel? Card, bool Played)
    {
        public static BottomPlayResult Empty { get; } = new(null, false);
    }

    public sealed record TopThenBottomResult(
        IReadOnlyList<CardModel> TopDrawn,
        CardModel? BottomTaken);

    public sealed record EndTurnBottomSnapshot(CardModel? MainstemCard, int FuelLayers);

    /// <summary>
    /// BurnTheRiver is the stable model class for 干流燃卷.  The class-name check
    /// keeps this shared scheduler independent of the card aggregate file owned
    /// by agent A while preserving old-save model identity.
    /// </summary>
    public static bool IsMainstem(CardModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card.GetType().Name == "BurnTheRiver";
    }

    /// <summary>Snapshots only the entry bottom card and Fuel layers, before any await.</summary>
    public static EndTurnBottomSnapshot CaptureEndTurnSnapshot(Player player, int fuelLayers)
    {
        ArgumentNullException.ThrowIfNull(player);
        CardModel? bottom = PeekBottom(player);
        return new(bottom is not null && IsMainstem(bottom) ? bottom : null,
            Math.Max(0, fuelLayers));
    }

    public static CardModel? PeekBottom(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return PileType.Draw.GetPile(player).Cards.LastOrDefault();
    }

    /// <summary>
    /// Plays exactly the card currently at the draw-pile bottom.  The card is
    /// selected once; effects that alter the draw pile while it resolves do not
    /// trigger another bottom-card lookup.  Existing ExhaustOnNextPlay state is
    /// preserved unless the caller explicitly requests forced Exhaust.
    /// </summary>
    public static async Task<BottomPlayResult> TryPlayBottomAsync(
        PlayerChoiceContext choiceContext,
        Player player,
        bool forceExhaust = false)
    {
        ArgumentNullException.ThrowIfNull(choiceContext);
        ArgumentNullException.ThrowIfNull(player);
        CardModel? card = PeekBottom(player);
        if (card is null || CombatManager.Instance.IsOverOrEnding || player.Creature.IsDead)
            return BottomPlayResult.Empty;

        CardPileAddResult moved = await CardPileCmd.Add(card, PileType.Play);
        if (!moved.success)
            return BottomPlayResult.Empty;
        LibrarianCardVfx050.Page(player, "bottom-read", card.GetType().Name);
        if (forceExhaust)
            card.ExhaustOnNextPlay = true;

        // A global singleton hook has no owner on 0.107.1, while 0.111.0
        // falls back to the first player. Neither identifies this card's owner.
        // Give only that hook-origin play its own native action context. Nested
        // card plays keep the existing context, so they can pause/resume the
        // currently executing action without enqueueing behind themselves.
        if (choiceContext is HookPlayerChoiceContext hook &&
            (hook.Source is LibrarianCombatHooks || !ReferenceEquals(hook.Owner, player)))
        {
            var owned = new HookPlayerChoiceContext(card, LocalContext.NetId!.Value,
                player.Creature.CombatState!, GameActionType.Combat);
            await owned.AssignTaskAndWaitForPauseOrCompletion(CardCmd.AutoPlay(owned, card, target: null));
            // Wait for the result pile and all nested effects before the next
            // Fuel layer, delayed task, or natural orb settlement can start.
            await owned.WaitForCompletion();
        }
        else
        {
            await CardCmd.AutoPlay(choiceContext, card, target: null);
        }
        return new(card, true);
    }

    /// <summary>Plays the snapshotted entry card only if it is still the bottom instance.</summary>
    public static async Task<BottomPlayResult> TryPlayExpectedBottomAsync(
        PlayerChoiceContext choiceContext,
        Player player,
        CardModel expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (!ReferenceEquals(PeekBottom(player), expected)) return BottomPlayResult.Empty;
        return await TryPlayBottomAsync(choiceContext, player);
    }

    /// <summary>
    /// Executes the single-entry Mainstem check, followed by the fixed number of
    /// Fuel layers. Each Fuel iteration re-reads the current bottom card; newly
    /// gained Fuel layers cannot extend this snapshot.
    /// </summary>
    public static async Task ResolveEndTurnBottomStageAsync(
        PlayerChoiceContext choiceContext,
        Player player,
        EndTurnBottomSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.MainstemCard is { } mainstem)
            await TryPlayExpectedBottomAsync(choiceContext, player, mainstem);
        for (int layer = 0; layer < snapshot.FuelLayers; layer++)
        {
            if (CombatManager.Instance.IsOverOrEnding || player.Creature.IsDead) break;
            await CardPileCmd.ShuffleIfNecessary(choiceContext, player);
            await TryPlayBottomAsync(choiceContext, player);
        }
    }

    /// <summary>Moves the selected card into the player's hand without re-reading or shuffling the draw pile.</summary>
    public static async Task<CardModel?> TakeBottomIntoHandAsync(
        PlayerChoiceContext choiceContext,
        Player player,
        bool retainForNextTurn = false)
    {
        ArgumentNullException.ThrowIfNull(choiceContext);
        ArgumentNullException.ThrowIfNull(player);
        CardModel? card = PeekBottom(player);
        if (card is null || CombatManager.Instance.IsOverOrEnding || player.Creature.IsDead)
            return null;
        CardPileAddResult moved = await CardPileCmd.Add(card, PileType.Hand);
        if (!moved.success)
            return null;
        LibrarianCardVfx050.Page(player, "bottom-read", card.GetType().Name);
        if (retainForNextTurn)
        {
            card.GiveSingleTurnRetain();
            LibrarianRuntime.MarkEndTurnHandCard(card);
        }
        return card;
    }

    /// <summary>
    /// Executes the exact order needed by 对页: native top draws complete first,
    /// then one current bottom card is taken.  The helper does not move a caller's
    /// own Play card; callers must move that instance to Draw.Bottom after this
    /// operation has completed.
    /// </summary>
    public static async Task<TopThenBottomResult> DrawTopThenTakeBottomAsync(
        PlayerChoiceContext choiceContext,
        Player player,
        int topCount,
        bool retainForNextTurn = false)
    {
        if (topCount < 0)
            throw new ArgumentOutOfRangeException(nameof(topCount));
        ArgumentNullException.ThrowIfNull(choiceContext);
        ArgumentNullException.ThrowIfNull(player);
        IEnumerable<CardModel> drawn = topCount == 0
            ? Array.Empty<CardModel>()
            : await CardPileCmd.Draw(choiceContext, topCount, player);
        CardModel[] top = drawn.ToArray();
        if (retainForNextTurn)
            foreach (CardModel card in top)
            {
                card.GiveSingleTurnRetain();
                LibrarianRuntime.MarkEndTurnHandCard(card);
            }
        CardModel? bottom = await TakeBottomIntoHandAsync(choiceContext, player, retainForNextTurn);
        return new(top, bottom);
    }

    /// <summary>Moves an existing combat card to the draw-pile bottom.</summary>
    public static async Task<bool> MoveToBottomAsync(PlayerChoiceContext choiceContext, CardModel card)
    {
        ArgumentNullException.ThrowIfNull(choiceContext);
        ArgumentNullException.ThrowIfNull(card);
        if (card.Owner.Creature.IsDead || CombatManager.Instance.IsOverOrEnding)
            return false;
        CardPileAddResult moved = await CardPileCmd.Add(card, PileType.Draw, CardPilePosition.Bottom);
        if (moved.success)
        {
            LibrarianCardVfx050.Page(card.Owner, "bottom-return", card.GetType().Name);
        }
        return moved.success;
    }

    /// <summary>Marks one newly obtained hand card, leaving all pre-existing hand cards untouched.</summary>
    public static void RetainOnlyThisCard(CardModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        card.GiveSingleTurnRetain();
        LibrarianRuntime.MarkEndTurnHandCard(card);
    }

}
