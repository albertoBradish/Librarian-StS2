using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Timeline;
using MegaCrit.Sts2.Core.Unlocks;

namespace Librarian.Mechanics;

/// <summary>Pure fixture progress only: never writes the active profile or launches UI.</summary>
internal static class DevelopmentRevision040UnlockAudit
{
    /// <summary>Pass an isolated foreign-character fixture: consumes only that fixture's reward RNG.</summary>
    internal static void RunCrossCharacter(Player player)
    {
        void Require(bool value, string name)
        {
            if (!value) throw new InvalidOperationException("040 cross-character audit: " + name);
            MainFile.Logger.Info("REVISION040_CROSS_CHARACTER_PASS " + name);
        }
        Require(player.Character is not LibrarianCharacter, "foreign fixture");
        Require(LibrarianCrossCharacter040.Eligible(player, CardRarity.Common).Count == 3, "three safe common cards");
        Require(LibrarianCrossCharacter040.Eligible(player, CardRarity.Uncommon).Count == (player.RunState.Players.Count > 1 ? 3 : 1), "safe uncommon cards respect multiplayer");
        Require(LibrarianCrossCharacter040.Eligible(player, CardRarity.Rare).Count == 2, "two safe rare cards");
        var cards = LibrarianCrossCharacter040.CreateSeaGlassCards(player, 15);
        Require(cards.Count == 15, "fifteen SeaGlass cards");
        Require(cards.Select(c => c.Card).Distinct(ReferenceEqualityComparer.Instance).Count() == 15, "duplicate IDs have independent card objects");
        Require(cards.GroupBy(c => c.Card.Rarity).All(g => g.Count() == 5), "five per rarity");
        Require(cards.All(c => c.Card.Owner == player && LibrarianCrossCharacter040.IsSeaGlassEligible(c.Card, player)), "owned and reviewed broad pool");
        Require(cards.GroupBy(c => c.Card.Rarity).All(g => g.Count(c => LibrarianCrossCharacter040.IsSeaGlassBlank(c.Card)) <= 1), "at most one blank per rarity");
        Require(cards.Select(c => c.Card.Id).Distinct().Count() == 15, "fifteen distinct IDs when full pool unlocked");
        Require(cards.All(c => !c.Card.IsUpgraded), "native unupgraded SeaGlass cards");
    }

    internal static void Run()
    {
        static void Require(bool value, string name)
        {
            if (!value) throw new InvalidOperationException("040 unlock audit: " + name);
            MainFile.Logger.Info("REVISION040_UNLOCK_PASS " + name);
        }
        LibrarianUnlocks040.Initialize();
        var ids = Enumerable.Range(1, 7).Select(LibrarianUnlocks040.Id).ToArray();
        Require(ids.All(EpochModel.IsValid), "seven registered epoch IDs");
        Require(ids.All(id => EpochModel.AllEpochIds.Count(e => e == id) == 1), "registration idempotent");
        Require(EpochModel.AllEpochIds.Select(EpochModel.Get)
            .GroupBy(e => (e.Era, e.EraPosition)).Where(g => g.Any(e => e is LibrarianEpoch040)).All(g => g.Count() == 1), "no occupied native timeline slots");
        Require(StoryModel.Get("LIBRARIAN_V040").Epochs.Select(e => e.Id).SequenceEqual(ids), "seven ordered story chapters");
        var progressive = ProgressState.CreateDefault();
        var nativeEpochs = progressive.Epochs.Select(e => (e.Id, e.State, e.ObtainDate)).ToHashSet();
        var nativeCharacters = new UnlockState(progressive).Characters.Where(c => c is not LibrarianCharacter).Select(c => c.Id).ToHashSet();
        LibrarianUnlocks040.ApplyChoice(progressive, false);
        var initial = new UnlockState(progressive);
        Require(ids.Count(id => progressive.Epochs.Any(e => e.Id == id && e.State == EpochState.Revealed)) == 1, "progressive only reveals introduction");
        var allCards = ModelDb.CardPool<LibrarianCardPool>().AllCards.ToList();
        Require(allCards.Count - LibrarianUnlocks040.FilterCards(initial, allCards).Count() == 9, "nine gated cards");
        Require(!allCards.Any(c => c.Id.Entry is "LIBRARIAN-FLAME_BURST" or "LIBRARIAN-SEAL_AWAY" or "LIBRARIAN-SEVER_CURRENT"), "legacy cards excluded");
        Require(ModelDb.RelicPool<LibrarianRelicPool>().GetUnlockedRelics(initial).Count() ==
            ModelDb.RelicPool<LibrarianRelicPool>().AllRelics.Count() - 6, "six gated relics");
        Require(!ModelDb.PotionPool<LibrarianPotionPool>().GetUnlockedPotions(initial).Any(), "three potions gated");
        progressive.ObtainEpochOverride(LibrarianUnlocks040.Id(2), EpochState.Revealed);
        Require(LibrarianUnlocks040.FilterCards(new UnlockState(progressive), allCards).Count() == allCards.Count - 6, "first three-card unlock");
        Require(LibrarianUnlocks040.FilterCards(initial, allCards).Count() == allCards.Count - 9, "existing run snapshot unchanged");
        LibrarianUnlocks040.ApplyChoice(progressive, true);
        var full = new UnlockState(progressive);
        Require(nativeCharacters.SetEquals(full.Characters.Where(c => c is not LibrarianCharacter).Select(c => c.Id)), "full choice does not unlock vanilla characters");
        Require(nativeEpochs.SetEquals(progressive.Epochs.Where(e => !ids.Contains(e.Id)).Select(e => (e.Id, e.State, e.ObtainDate))), "full choice preserves all native epoch states");
        Require(LibrarianUnlocks040.FilterCards(full, allCards).Count() == allCards.Count, "full choice unlocks cards");
        Require(ModelDb.PotionPool<LibrarianPotionPool>().GetUnlockedPotions(full).Count() == 3, "full choice unlocks potions");
        Require(ModelDb.RelicPool<LibrarianRelicPool>().GetUnlockedRelics(full).Count() == ModelDb.RelicPool<LibrarianRelicPool>().AllRelics.Count(), "full choice unlocks relics");
        LibrarianUnlocks040.ApplyChoice(progressive, false);
        Require(ids.All(id => progressive.Epochs.Any(e => e.Id == id && e.State == EpochState.Revealed)), "choice never erases existing unlocks");
        var restored = UnlockState.FromSerializable(full.ToSerializable());
        Require(ids.All(id => restored.ToSerializable().UnlockedEpochs.Contains(id)), "snapshot serializable roundtrip");
        Require(LibrarianUnlocks040.FilterCards(UnlockState.all, allCards).Count() == allCards.Count, "native all-unlocked sentinel");
    }
}
