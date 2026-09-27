using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace Librarian.Mechanics;

/// <summary>Native autoplay of every newly admitted effect and blank, both upgrades.</summary>
internal static class DevelopmentRevision040SeaGlassPlayAudit
{
    internal static async Task Run(Player player)
    {
        if (player.Character is LibrarianCharacter || LibrarianRuntime.TryGet(player, out _))
            throw new InvalidOperationException("SeaGlass audit requires a fresh foreign fixture without a session");
        var combat = player.Creature.CombatState!;
        var context = new ThrowingPlayerChoiceContext();
        var candidates = ModelDb.CardPool<LibrarianCardPool>().AllCards
            .Where(LibrarianCrossCharacter040.NeedsForeignAdaptation)
            .OrderBy(c => c.Id.Entry is "LIBRARIAN-COOLDOWN" or "LIBRARIAN-RIDGE_WARD" or "LIBRARIAN-UNRETREATING_TIDE" ? 1 : 0).ThenBy(c => c.Id.Entry).ToArray();
        int checks = 0;
        foreach (var canonical in candidates)
        foreach (bool upgrade in new[] { false, true })
        {
            foreach (var enemy in combat.HittableEnemies)
            {
                await CreatureCmd.SetMaxAndCurrentHp(enemy, 10000);
                foreach (var power in enemy.Powers.ToArray()) await PowerCmd.Remove(power);
            }
            foreach (var held in player.PlayerCombatState!.Hand.Cards.ToArray())
                await CardPileCmd.Add(held, PileType.Discard, skipVisuals: true);
            for (int i = 0; i < 8; i++)
                await CardPileCmd.AddGeneratedCardToCombat(combat.CreateCard<MegaCrit.Sts2.Core.Models.Cards.DefendIronclad>(player), PileType.Draw, player);
            var card = combat.CreateCard(canonical, player);
            if (upgrade) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
            string shortId = card.Id.Entry.Replace("LIBRARIAN-", "");
            decimal V(string key) => card.DynamicVars[key].BaseValue;
            int enemies = combat.HittableEnemies.Count();
            int hp = combat.HittableEnemies.Sum(e => e.CurrentHp);
            int block = player.Creature.Block;
            int energy = player.PlayerCombatState.Energy;
            bool hadSession = LibrarianRuntime.TryGet(player, out var priorSession);
            int waves = priorSession?.Waves.Amount ?? 0;
            if (shortId == "DEEP_SEA_BARRIER")
                for (int i = 0; i < 2; i++)
                    await CardPileCmd.AddGeneratedCardToCombat(combat.CreateCard<MegaCrit.Sts2.Core.Models.Cards.DefendIronclad>(player), PileType.Hand, player);
            int exhaust = player.PlayerCombatState.ExhaustPile.Cards.Count;
            if (card.EnergyCost.CostsX) card.EnergyCost.CapturedXValue = 2;
            await CardCmd.AutoPlay(context, card, card.TargetType == TargetType.AnyEnemy ? combat.HittableEnemies.First() : null,
                skipXCapture: true, skipCardPileVisuals: true);
            decimal expectedDamage = shortId switch
            {
                "NEEDLE_FLURRY" or "FLAME_STRIKE" or "WAVE_STRIKE" or "ASHEN_BLOW" or "ROOTBIND" or "EMBER_PIERCE"
                    or "BURN_ROOTS" or "SEEDBURIAL_STRIKE" or "OVERLOAD_BURN" or "ZERO_SEARCH" => V("Damage"),
                "SCATTERED_FLAMES" or "GAP_NEEDLE" or "EMBER_RECKONING" or "FIRE_INSCRIPTION" => V("CalculationBase"),
                "TIDAL_STRIKE" => V("CalculationBase") + waves,
                "STEAM_BLAST" or "TIDAL_EROSION" => V("Damage") * enemies,
                "EARTH_COLLAPSE" or "NOURISHING_LIFE" => V("Damage") * 2,
                _ => 0
            };
            decimal expectedBlock = shortId switch
            {
                "QUIET_EMBERS" or "SPROUTING_SEED" or "VINE_SHIELD" or "SONG_OF_ICE_AND_FIRE" or "COOLDOWN" or "SPROUTING_BULWARK" => V("Block"),
                "IGNITE" => V("CalculationBase"), _ => 0
            };
            decimal expectedDraw = shortId switch
            {
                "CHANNEL_FLOW" or "DRY_BRANCH_SEARCH" or "EVAPORATION" or "REKINDLE" or "OUT_OF_CONTEXT" or "CALIBRATE" or "SPROUTING_BULWARK" => V("Cards"),
                _ => 0
            };
            decimal expectedEnergy = shortId is "BUILD_CANAL" or "TIDAL_GRAVITY" or "READ_WIDELY" ? V("Energy") : 0;
            void Require(bool condition, string property)
            {
                if (!condition) throw new InvalidOperationException($"SeaGlass foreign {shortId} up={upgrade}: {property}");
                checks++;
            }
            Require(hp - combat.HittableEnemies.Sum(e => e.CurrentHp) == expectedDamage, "printed independent damage");
            Require(player.Creature.Block - block == expectedBlock, "printed independent block");
            Require(player.PlayerCombatState.Hand.Cards.Count == expectedDraw, "printed independent draw/hand cleanup");
            Require(player.PlayerCombatState.Energy - energy == expectedEnergy, "printed independent energy");
            if (shortId == "TIDAL_EROSION")
                Require(combat.HittableEnemies.All(e => e.GetPower<MegaCrit.Sts2.Core.Models.Powers.WeakPower>()?.Amount == V("WeakPower")), "unconditional Weak preserved");
            if (shortId == "DEEP_SEA_BARRIER") Require(player.PlayerCombatState.ExhaustPile.Cards.Count == exhaust + 3, "two hand cards and played card exhaust");
            bool hasSession = LibrarianRuntime.TryGet(player, out var session);
            Require(hasSession == (hadSession || shortId is "COOLDOWN" or "RIDGE_WARD" or "UNRETREATING_TIDE"), "no incidental session allocation");
            if (hasSession)
            {
                Require(!session!.HasCharacterOrbs && session.Orbs.Positions.All(k => session.Orbs.Value(k) == 0 && !session.Orbs.IsActivated(k) && !session.Orbs.IsLocked(k)), "foreign player still has no active or valued orbs");
                if (shortId == "COOLDOWN") Require(session.Waves.Amount == waves + V("Waves"), "supported foreign Waves preserved");
                if (shortId == "RIDGE_WARD") Require(session.Waves.Retained, "supported foreign Wave retention");
                if (shortId == "UNRETREATING_TIDE") Require(session.Waves.RetentionFloor == V("UnretreatingTidePower"), "supported foreign Wave retention floor");
            }
            MainFile.Logger.Info($"SEAGLASS040_FOREIGN_PLAY_PASS id={card.Id.Entry} upgraded={upgrade} damage={expectedDamage} block={expectedBlock} draw={expectedDraw} energy={expectedEnergy}");
        }
        // Repeated native card applications update the existing power rather than invoking
        // AfterApplied again. Exercise both recipient types and immediate removal as well.
        foreach (var recipient in new[] { player, combat.Players.First(p => p.Character is LibrarianCharacter) })
        {
            await PowerCmd.Remove<Librarian.LibrarianCode.Powers.Implemented.UnretreatingTidePower>(recipient.Creature);
            async Task FloorCard(bool upgraded)
            {
                var c = combat.CreateCard<Librarian.LibrarianCode.Cards.PowerCards.UnretreatingTide>(recipient);
                if (upgraded) { c.UpgradeInternal(); c.FinalizeUpgradeInternal(); }
                await CardCmd.AutoPlay(context, c, null, skipCardPileVisuals: true);
            }
            void FloorCheck(int expected, string stage)
            {
                var power = recipient.Creature.GetPower<Librarian.LibrarianCode.Powers.Implemented.UnretreatingTidePower>();
                if ((power?.Amount ?? 0) != expected || LibrarianRuntime.Get(recipient).Waves.RetentionFloor != expected)
                    throw new InvalidOperationException($"SeaGlass Wave floor {recipient.Character.Id} {stage}: expected {expected}");
                checks++;
            }
            await FloorCard(false); FloorCheck(8, "base");
            await FloorCard(true); FloorCheck(12, "upgrade stacked onto base");
            await FloorCard(false); FloorCheck(12, "weaker recast preserves maximum");
            await PowerCmd.ModifyAmount(context,
                recipient.Creature.GetPower<Librarian.LibrarianCode.Powers.Implemented.UnretreatingTidePower>()!, -4,
                recipient.Creature, null);
            FloorCheck(8, "native amount decrease");
            await PowerCmd.Remove<Librarian.LibrarianCode.Powers.Implemented.UnretreatingTidePower>(recipient.Creature);
            FloorCheck(0, "native removal clears floor");
            MainFile.Logger.Info($"SEAGLASS040_WAVE_FLOOR_REGRESSION_PASS character={recipient.Character.Id} stages=5");
        }

        // Actual Binding transfers a settlement to the foreign player's history. Its
        // Fire Inscription must match the printed preview without gaining character orbs.
        var librarian = combat.Players.First(p => p.Character is LibrarianCharacter);
        var librarianSession = LibrarianRuntime.Get(librarian);
        await LibrarianRuntime.Dispatch(librarianSession, context, librarianSession.Orbs.Gain(OrbKind.Fire, 5));
        var imported = LibrarianRuntime.Get(player);
        long priorCount = imported.Orbs.SettlementsThisCombat;
        await CardCmd.AutoPlay(context, combat.CreateCard<Librarian.LibrarianCode.Cards.MultiplayerPlaceholderB>(librarian), null,
            skipCardPileVisuals: true);
        if (imported.Orbs.SettlementsThisCombat != priorCount + 1)
            throw new InvalidOperationException("SeaGlass Binding must record a real foreign shared settlement");
        checks++;
        foreach (bool upgraded in new[] { false, true })
        {
            var inscription = combat.CreateCard<Librarian.LibrarianCode.Cards.Stateful.FireInscription>(player);
            if (upgraded) { inscription.UpgradeInternal(); inscription.FinalizeUpgradeInternal(); }
            var target = combat.HittableEnemies.First();
            int hp = target.CurrentHp;
            decimal expected = 10 + imported.Orbs.SettlementsThisCombat * (upgraded ? 6 : 4);
            await CardCmd.AutoPlay(context, inscription, target, skipCardPileVisuals: true);
            if (hp - target.CurrentHp != expected || !ReferenceEquals(imported, LibrarianRuntime.Get(player))
                || imported.Orbs.Positions.Any(k => imported.Orbs.Value(k) != 0 || imported.Orbs.IsActivated(k)))
                throw new InvalidOperationException("SeaGlass Fire Inscription must use actual shared history without creating orbs");
            checks++;
            MainFile.Logger.Info($"SEAGLASS040_SHARED_INSCRIPTION_PASS upgraded={upgraded} count={imported.Orbs.SettlementsThisCombat} damage={expected}");
        }
        MainFile.Logger.Info($"SEAGLASS040_FOREIGN_AUDIT_PASS cards={candidates.Length} plays={candidates.Length * 2} checks={checks}");
    }
}
