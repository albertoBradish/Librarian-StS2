using System.Reflection;
using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Relics;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace Librarian.Mechanics;

/// <summary>Invoked only by the coordinator's opt-in development fixtures.</summary>
internal static class DevelopmentRelicAudit
{
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Relic audit: " + message);
    }

    internal static void ValidateModels()
    {
        var pool = ModelDb.RelicPool<LibrarianRelicPool>();
        var regular = pool.AllRelics.ToArray();
        var upgrade = ModelDb.Relic<RestoredSpellScroll>();
        var starter = ModelDb.Relic<TatteredSpellScroll>();
        Require(regular.Length == 8, "character pool requires starter + seven normal relics");
        foreach (var (rarity, count) in new[] { (RelicRarity.Starter, 1), (RelicRarity.Common, 1),
                     (RelicRarity.Uncommon, 2), (RelicRarity.Rare, 3), (RelicRarity.Shop, 1) })
            Require(regular.Count(r => r.Rarity == rarity) == count, $"wrong count for {rarity}");
        Require(ModelDb.RelicPool<EventRelicPool>().AllRelics.Contains(upgrade), "upgrade missing from event pool");
        Require(!regular.Contains(upgrade) && upgrade.Rarity == RelicRarity.Starter, "upgrade must not enter normal rewards");
        Require(starter.GetUpgradeReplacement().Id == upgrade.Id, "starter upgrade mapping");
        Require(ModelDb.Relic<TouchOfOrobas>().GetUpgradedStarterRelic(starter).Id == upgrade.Id, "native patched upgrade mapping");
        Require(pool.GetUnlockedRelics(UnlockState.all).Count() == 8, "normal pool unlock eligibility");
        var all = regular.Append(upgrade).ToArray();
        Require(all.Select(r => r.Id).Distinct().Count() == 9, "nine independent model IDs");
        foreach (var relic in all)
        {
            Require(!relic.IsStackable, "placeholder inherited Circlet stacking: " + relic.Id);
            var title = relic.Title.GetFormattedText();
            var description = relic.DynamicDescription.GetFormattedText();
            Require(!string.IsNullOrWhiteSpace(title) && !title.Contains(relic.Id.Entry), "missing title " + relic.Id);
            Require(!string.IsNullOrWhiteSpace(description) && !description.Contains('{') && !description.Contains(relic.Id.Entry), "missing description " + relic.Id);
            Require(!description.Contains("开发占位") && !description.Contains("Development placeholder"), "implemented relic still advertises no effect " + relic.Id);
            foreach (var property in new[] { "PackedIconPath", "PackedIconOutlinePath", "BigIconPath" })
            {
                var path = (string)typeof(RelicModel).GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(relic)!;
                Require(ResourceLoader.Exists(path), "missing icon " + path);
                if (relic is LibrarianPlaceholderRelic) Require(path.StartsWith("res://Librarian/images/relics/") && !path.Contains("circlet"), "missing original relic art " + path);
            }
            var mutable = relic.ToMutable();
            var restored = RelicModel.FromSerializable(mutable.ToSerializable());
            Require(restored.Id == relic.Id && restored.GetType() == relic.GetType(), "save roundtrip " + relic.Id);
            MainFile.Logger.Info($"RELIC_AUDIT_MODEL {relic.Id} rarity={relic.Rarity} title={title} description={description}");
        }
        MainFile.Logger.Info("RELIC_MODEL_AUDIT_PASS total=9 characterPool=8 eventUpgrade=1 serialization=9 iconPaths=27");
    }

    /// <summary>Tests the real run's eligibility through native dequeue APIs on serialized copies only.
    /// Does not advance live RNG, consume a live reward, mark relics seen, or modify live bags.</summary>
    internal static void ValidateRewardEligibility(Player player)
    {
        Require(player.Character is LibrarianCharacter, "reward fixture must use Librarian");
        Require(player.RelicGrabBag.IsPopulated, "native run must populate the player grab bag first");
        var saved = player.RelicGrabBag.ToSerializable();
        var serializedBefore = System.Text.Json.JsonSerializer.Serialize(saved.RelicIdLists);
        var placeholders = ModelDb.RelicPool<LibrarianRelicPool>().AllRelics
            .OfType<LibrarianPlaceholderRelic>().ToArray();
        Require(placeholders.Length == 7, "seven normal reward/shop placeholders");
        foreach (var relic in placeholders)
        {
            Require(player.RelicGrabBag.Contains(relic), "missing from actual player bag " + relic.Id);
            Require(saved.RelicIdLists.TryGetValue(relic.Rarity, out var ids) && ids.Contains(relic.Id),
                "wrong live rarity bucket " + relic.Id);
            Require(relic.IsAllowed(player.RunState), "run eligibility rejected " + relic.Id);
            var copy = RelicGrabBag.FromSerializable(saved);
            var selected = relic.Rarity == RelicRarity.Shop
                ? copy.PullFromBack(relic.Rarity, candidate => candidate.Id == relic.Id && candidate.IsAllowedInShops, player.RunState)
                : copy.PullFromFront(relic.Rarity, candidate => candidate.Id == relic.Id, player.RunState);
            Require(selected?.Id == relic.Id, "native reward/shop dequeue rejected " + relic.Id);
            Require(!copy.Contains(relic), "dequeue did not consume its copy entry " + relic.Id);
            MainFile.Logger.Info($"RELIC_ELIGIBILITY_AUDIT {relic.Id} rarity={relic.Rarity} nativeDeque={(relic.Rarity == RelicRarity.Shop ? "back-shop" : "front-reward")} copyConsumed=true");
        }
        Require(!player.RelicGrabBag.Contains(ModelDb.Relic<TatteredSpellScroll>()), "starter entered normal rewards");
        Require(!player.RelicGrabBag.Contains(ModelDb.Relic<RestoredSpellScroll>()), "upgrade entered normal rewards");
        Require(serializedBefore == System.Text.Json.JsonSerializer.Serialize(player.RelicGrabBag.ToSerializable().RelicIdLists),
            "eligibility audit changed live grab bag");
        MainFile.Logger.Info("RELIC_REWARD_ELIGIBILITY_AUDIT_PASS rewards=6 shop=1 liveBagUnchanged=true rngUnused=true");
    }

    /// <summary>Run in a fresh, power-free audit fight. Existing lock states are retained and tested;
    /// restores the initial relic afterward, but leaves the tested +2 orb values for fixture inspection.</summary>
    internal static async Task ValidateUpgradeTurns(Player player, PlayerChoiceContext context)
    {
        Require(OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase), "isolated profile required");
        Require(player.Creature.Powers.Count == 0, "run relic effect audit without reactive powers");
        var starter = player.Relics.OfType<TatteredSpellScroll>().Single();
        var nativeUpgrade = (TouchOfOrobas)ModelDb.Relic<TouchOfOrobas>().ToMutable();
        nativeUpgrade.Owner = player;
        Require(nativeUpgrade.SetupForPlayer(player), "native upgrade setup rejected starter");
        var session = LibrarianRuntime.Get(player);
        var before = session.Orbs.Snapshot();
        await nativeUpgrade.AfterObtained();
        try
        {
            var upgraded = player.Relics.OfType<RestoredSpellScroll>().Single();
            Require(!player.Relics.OfType<TatteredSpellScroll>().Any(), "native replacement left starter active");
            var combat = player.Creature.CombatState!;
            await upgraded.BeforeSideTurnStart(context, CombatSide.Enemy, Array.Empty<Creature>(), combat);
            foreach (var orb in before.Orbs) Require(session.Orbs.Value(orb.Kind) == orb.Value, "non-owner turn triggered");
            for (var turn = 1; turn <= 2; turn++)
            {
                // The native hook dispatcher consults the player's current relic list, after replacement.
                foreach (var relic in player.Relics.OfType<LibrarianRelic>().ToArray())
                    await relic.BeforeSideTurnStart(context, CombatSide.Player, new[] { player.Creature }, combat);
                foreach (var orb in before.Orbs)
                {
                    Require(session.Orbs.Value(orb.Kind) == orb.Value + turn, $"upgrade should strengthen each orb once: {orb.Kind}");
                    Require(session.Orbs.LockedTurns(orb.Kind) == orb.LockedTurns, "upgrade altered lock");
                    bool expectedActive = orb.IsActivated || (orb.IsForeground && !orb.IsLocked);
                    Require(session.Orbs.IsActivated(orb.Kind) == expectedActive, "upgrade foreground activation or lock rule");
                }
                Require(session.Orbs.Foreground == before.Foreground, "upgrade moved foreground");
            }
            MainFile.Logger.Info("RELIC_RUNTIME_AUDIT_PASS nativeReplacement=1 ownerTurns=2 enemyTurn=0 allOrbs=3 locksPreserved=true");
        }
        finally
        {
            if (player.Relics.OfType<RestoredSpellScroll>().SingleOrDefault() is { } upgraded)
                await RelicCmd.Replace(upgraded, starter);
        }
    }
}
