using HarmonyLib;
using System.Runtime.CompilerServices;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;

namespace Librarian.Mechanics;

/// <summary>Strict philosopher rewards and a separately reviewed, broader Sea Glass pool.</summary>
public static class LibrarianCrossCharacter040
{
    private sealed class ForeignHistory
    {
        internal CardModel? Last;
        internal readonly Dictionary<CardPlay, CardModel> Pending = new();
    }
    private static readonly ConditionalWeakTable<PlayerCombatState, ForeignHistory> Histories = new();

    public static CardModel? LastPlayed(Player player)
    {
        if (player.Character is LibrarianCharacter)
            return LibrarianRuntime.TryGet(player, out var session) ? session?.LastPlayedSnapshot : null;
        return player.PlayerCombatState is { } combat && Histories.TryGetValue(combat, out var history) ? history.Last : null;
    }

    internal static void RecordBefore(CardPlay play)
    {
        var player = play.Card.Owner;
        if (player.Character is LibrarianCharacter || player.PlayerCombatState is not { } combat) return;
        bool alreadyTracking = Histories.TryGetValue(combat, out var history);
        if (!alreadyTracking && !player.Deck.Cards.Concat(combat.AllCards).Any(c => c.Id.Entry == "LIBRARIAN-TRANSCRIBE")) return;
        history ??= Histories.GetValue(combat, _ => new ForeignHistory());
        history.Pending[play] = (CardModel)play.Card.ClonePreservingMutability();
    }

    internal static void RecordAfter(CardPlay play)
    {
        if (play.Card.Owner.PlayerCombatState is { } combat && Histories.TryGetValue(combat, out var history) &&
            history.Pending.Remove(play, out var snapshot)) history.Last = snapshot;
    }

    private static readonly HashSet<string> Singleplayer = new(StringComparer.Ordinal)
    {
        "LIBRARIAN-COMBAT_NOTES", "LIBRARIAN-WEARY_INCANTATION", "LIBRARIAN-FLYING_PAGES",
        "LIBRARIAN-NOURISH", "LIBRARIAN-BOOKWORM", "LIBRARIAN-TRANSCRIBE",
        "LIBRARIAN-CALIBRATE", "LIBRARIAN-READ_BACKWARD", "LIBRARIAN-FUEL_THE_FIRE", "LIBRARIAN-TO_BE_CONTINUED"
    };
    private static readonly HashSet<string> Multiplayer = new(StringComparer.Ordinal)
        { "LIBRARIAN-CIRCULATION_NOTES", "LIBRARIAN-SHARED_SHELTER" };

    public static bool IsUseful(CardModel card, Player recipient) =>
        Singleplayer.Contains(card.Id.Entry) || recipient.RunState.Players.Count > 1 && Multiplayer.Contains(card.Id.Entry);

    public static List<CardModel> Eligible(Player player, CardRarity rarity) =>
        ModelDb.CardPool<LibrarianCardPool>().GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint)
            .Where(c => c.Rarity == rarity && IsUseful(c, player)).OrderBy(c => c.Id.Entry, StringComparer.Ordinal).ToList();

    // Independent printed benefits count even when the orb-dependent portion does nothing.
    private static readonly HashSet<string> SeaGlassPartial = new(StringComparer.Ordinal)
    {
        "NEEDLE_FLURRY", "FLAME_STRIKE", "WAVE_STRIKE", "SCATTERED_FLAMES", "ASHEN_BLOW",
        "QUIET_EMBERS", "SPROUTING_SEED", "CHANNEL_FLOW", "DRY_BRANCH_SEARCH", "VINE_SHIELD",
        "GAP_NEEDLE", "BUILD_CANAL", "ROOTBIND", "EMBER_PIERCE", "TIDAL_STRIKE", "BURN_ROOTS",
        "TIDAL_EROSION", "EARTH_COLLAPSE", "SEEDBURIAL_STRIKE", "STEAM_BLAST", "EMBER_RECKONING",
        "TIDAL_GRAVITY", "EVAPORATION", "COOLDOWN", "SPROUTING_BULWARK", "REKINDLE",
        "OUT_OF_CONTEXT", "OVERLOAD_BURN", "IGNITE", "ZERO_SEARCH", "NOURISHING_LIFE", "FIRE_INSCRIPTION",
        "READ_WIDELY", "SONG_OF_ICE_AND_FIRE", "DEEP_SEA_BARRIER", "RIDGE_WARD", "UNRETREATING_TIDE",
        "CALIBRATE", "READ_BACKWARD", "FUEL_THE_FIRE", "RESIDUAL_WARMTH"
    };
    // A small, explicit set of orb-only blanks. Never silently reintroduce the entire unusable pool.
    private static readonly HashSet<string> SeaGlassBlanks = new(StringComparer.Ordinal)
    {
        "REIGNITE", "SPRINGWATER", "RENEWAL",
        "BURN_THE_RIVER", "OPENING_TIDE", "CROP_ROTATION",
        "ETERNAL_GRIMOIRE", "TREE_RING_BURST", "LIFE_SYMPHONY"
    };
    private static string ShortId(CardModel card) => card.Id.Entry.Replace("LIBRARIAN-", "", StringComparison.Ordinal);
    public static bool IsSeaGlassUseful(CardModel card, Player player) => IsUseful(card, player) || SeaGlassPartial.Contains(ShortId(card));
    public static bool IsSeaGlassBlank(CardModel card) => SeaGlassBlanks.Contains(ShortId(card));
    public static bool IsSeaGlassEligible(CardModel card, Player player) => IsSeaGlassUseful(card, player) || IsSeaGlassBlank(card);
    public static List<CardModel> SeaGlassEligible(Player player, CardRarity rarity, bool blanks = false) =>
        ModelDb.CardPool<LibrarianCardPool>().GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint)
            .Where(c => c.Rarity == rarity && (blanks ? IsSeaGlassBlank(c) : IsSeaGlassUseful(c, player)))
            .OrderBy(c => c.Id.Entry, StringComparer.Ordinal).ToList();
    internal static bool NeedsForeignAdaptation(CardModel card) => SeaGlassPartial.Contains(ShortId(card)) || SeaGlassBlanks.Contains(ShortId(card));

    private static CardCreationOptions Options(Player player, CardRarity rarity, bool noUpgrade = false)
    {
        var options = new CardCreationOptions([ModelDb.CardPool<LibrarianCardPool>()], CardCreationSource.Other,
            CardRarityOddsType.Uniform, c => c.Rarity == rarity && IsUseful(c, player))
            .WithFlags(CardCreationFlags.NoRarityModification | CardCreationFlags.NoCardPoolModifications);
        return noUpgrade ? options.WithFlags(CardCreationFlags.NoUpgradeRoll) : options;
    }

    internal static async Task OfferOrange(ColorfulPhilosophers model)
    {
        var player = model.Owner!;
        var rewards = new List<Reward>();
        foreach (var rarity in new[] { CardRarity.Common, CardRarity.Uncommon, CardRarity.Rare })
        {
            int count = Math.Min(3, Eligible(player, rarity).Count);
            if (count > 0) rewards.Add(new CardReward(Options(player, rarity), count, player));
        }
        await RewardsCmd.OfferCustom(player, rewards);
        AccessTools.Method(typeof(EventModel), "SetEventFinished").Invoke(model,
            [new LocString("events", "COLORFUL_PHILOSOPHERS.pages.DONE.description")]);
    }

    internal static async Task OfferSeaGlass(SeaGlass relic)
    {
        var player = relic.Owner;
        var cards = CreateSeaGlassCards(player, relic.DynamicVars.Cards.IntValue);
        if (cards.Count == 0) return;
        var prefs = new CardSelectorPrefs(new LocString("relics", "SEA_GLASS.selectionScreenPrompt"), 0, cards.Count);
        foreach (var card in await CardSelectCmd.FromSimpleGridForRewards(new BlockingPlayerChoiceContext(), cards, player, prefs))
            CardCmd.PreviewCardPileAdd(await CardPileCmd.Add(card, PileType.Deck));
    }

    internal static List<CardCreationResult> CreateSeaGlassCards(Player player, int total)
    {
        var cards = new List<CardCreationResult>();
        // Preserve native five-per-rarity. Sample without replacement first, with at most one
        // optional blank in each rarity; duplicate useful cards only if unlocks make that necessary.
        foreach (var rarity in new[] { CardRarity.Common, CardRarity.Uncommon, CardRarity.Rare })
        {
            var eligible = SeaGlassEligible(player, rarity);
            if (eligible.Count == 0) continue;
            var remaining = eligible.ToList();
            var blanks = SeaGlassEligible(player, rarity, blanks: true);
            int quota = total / 3;
            // A blank is optional, not a guaranteed tax on every event.
            bool includeBlank = quota > 1 && blanks.Count > 0 && player.PlayerRng.Rewards.NextInt(2) == 0;
            for (int i = 0; i < quota; i++)
            {
                var pool = includeBlank && i == quota - 1 ? blanks : remaining.Count > 0 ? remaining : eligible;
                var canonical = player.PlayerRng.Rewards.NextItem(pool)!;
                remaining.Remove(canonical);
                cards.Add(new CardCreationResult(player.RunState.CreateCard(canonical, player)));
            }
        }
        return cards;
    }
}

[HarmonyPatch(typeof(ColorfulPhilosophers), "get_CardPoolColorOrder")]
internal static class LibrarianOrangeOption040
{
    [HarmonyPostfix] private static void Postfix(ref IEnumerable<CardPoolModel> __result) =>
        __result = __result.Append(ModelDb.CardPool<LibrarianCardPool>()).Distinct().ToArray();
}

[HarmonyPatch(typeof(ColorfulPhilosophers), "OfferRewards")]
internal static class LibrarianOrangeRewards040
{
    [HarmonyPrefix] private static bool Prefix(ColorfulPhilosophers __instance, CardPoolModel pool, ref Task __result)
    {
        if (pool is not LibrarianCardPool) return true;
        __result = LibrarianCrossCharacter040.OfferOrange(__instance);
        return false;
    }
}

[HarmonyPatch(typeof(SeaGlass), nameof(SeaGlass.AfterObtained))]
internal static class LibrarianSeaGlass040
{
    [HarmonyPrefix] private static bool Prefix(SeaGlass __instance, ref Task __result)
    {
        if (__instance.CharacterId != ModelDb.Character<LibrarianCharacter>().Id || __instance.Owner.Character is LibrarianCharacter)
            return true;
        __result = LibrarianCrossCharacter040.OfferSeaGlass(__instance);
        return false;
    }
}

/// <summary>Record copied-card history without allocating a Librarian session, orbs, or Block ledger.</summary>
[HarmonyPatch(typeof(LibrarianCombatHooks), nameof(LibrarianCombatHooks.BeforeCardPlayed))]
internal static class LibrarianForeignHistory040
{
    [HarmonyPrefix] private static void Prefix(CardPlay cardPlay)
        => LibrarianCrossCharacter040.RecordBefore(cardPlay);
}

[HarmonyPatch(typeof(LibrarianCombatHooks), nameof(LibrarianCombatHooks.AfterCardPlayed))]
internal static class LibrarianForeignHistoryAfter040
{
    [HarmonyPostfix] private static void Postfix(CardPlay cardPlay) => LibrarianCrossCharacter040.RecordAfter(cardPlay);
}
