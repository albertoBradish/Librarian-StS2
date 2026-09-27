using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Cards.PowerCards;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace Librarian.Mechanics;

/// <summary>Call in a fresh isolated live combat. Actual cards, native powers and death commands;
/// three native player models in one process do not establish multiplayer transport correctness.</summary>
internal static class DevelopmentRevision040WaterBoundaryAudit
{
    internal static async Task Run(Player owner)
    {
        if (!OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("040 water boundary requires isolated profile");
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("040 water boundary: " + message);
            checks++;
            MainFile.Logger.Info("CARD040_WATER_BOUNDARY_CHECK_PASS " + message);
        }
        Check(owner.Character is LibrarianCharacter && !owner.Creature.Powers.OfType<WaterSpiritPower>().Any(), "fresh Librarian owner");
        var combat = (CombatState)owner.Creature.CombatState!;
        var librarian = Player.CreateForNewRun<LibrarianCharacter>(UnlockState.all, 940011);
        var ironclad = Player.CreateForNewRun<Ironclad>(UnlockState.all, 940012);
        foreach (var player in new[] { librarian, ironclad })
        {
            player.RunState = owner.RunState;
            player.ResetCombatState();
            combat.AddPlayer(player);
            // Native power-card flight resolves the caster's scene even during
            // a headless audit. Keep the actual creature/VFX routing intact.
            if (NCombatRoom.Instance!.GetCreatureNode(player.Creature) is null)
                NCombatRoom.Instance.AddCreature(player.Creature);
        }
        var context = new ThrowingPlayerChoiceContext();
        async Task PlayWater(Player player, bool upgraded)
        {
            var card = combat.CreateCard<WaterSpirit>(player);
            if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
            await CardCmd.AutoPlay(context, card, null, skipCardPileVisuals: true);
        }
        try
        {
            var ownSession = LibrarianRuntime.Get(owner);
            var otherSession = LibrarianRuntime.Get(librarian);
            // Positive control: confirm these native modifiers are effective, rather than
            // merely asserting that an ignored/incorrectly applied modifier changes nothing.
            await PowerCmd.Apply<DexterityPower>(context, ironclad.Creature, 7m, ironclad.Creature, null);
            await PowerCmd.Apply<FrailPower>(context, ironclad.Creature, 1m, ironclad.Creature, null);
            int foreignBefore = ironclad.Creature.Block;
            var defend = combat.CreateCard<DefendIronclad>(ironclad);
            await CardCmd.AutoPlay(context, defend, null, skipCardPileVisuals: true);
            Check(ironclad.Creature.Block == foreignBefore + 9, "native Defend positive control: (5+7)*0.75=9");
            int ownTide = ownSession.Orbs.Value(OrbKind.Tide), otherTide = otherSession.Orbs.Value(OrbKind.Tide);
            int librarianBlock = librarian.Creature.Block;
            foreignBefore = ironclad.Creature.Block;
            await PlayWater(owner, true);
            Check(ownSession.Orbs.Value(OrbKind.Tide) == ownTide + 13, "upgraded Water Spirit immediate Tide thirteen");
            Check(librarian.Creature.Block == librarianBlock + 13 && ironclad.Creature.Block == foreignBefore + 13,
                "three players: each living teammate gets thirteen despite native Dexterity/Frail");
            Check(otherSession.Orbs.Value(OrbKind.Tide) == otherTide && !LibrarianRuntime.TryGet(ironclad, out _),
                "teammates receive ordinary Block without Tide or foreign orb session");

            // Give the second Librarian its own passive by playing the real card.
            int ownerBlock = owner.Creature.Block;
            foreignBefore = ironclad.Creature.Block;
            ownTide = ownSession.Orbs.Value(OrbKind.Tide);
            await PlayWater(librarian, false);
            Check(owner.Creature.Block == ownerBlock + 8 && ironclad.Creature.Block == foreignBefore + 8,
                "second Librarian passive grants eight to both other players");
            Check(ownSession.Orbs.Value(OrbKind.Tide) == ownTide && otherSession.Orbs.Value(OrbKind.Tide) == otherTide + 8,
                "two Water Spirit owners do not feed Block back into Tide");
            librarianBlock = librarian.Creature.Block;
            foreignBefore = ironclad.Creature.Block;
            otherTide = otherSession.Orbs.Value(OrbKind.Tide);
            await PlayWater(owner, true);
            Check(librarian.Creature.Block == librarianBlock + 13 && ironclad.Creature.Block == foreignBefore + 13,
                "recast upgraded card with two passive owners remains single thirteen grant");
            Check(owner.Creature.Powers.OfType<WaterSpiritPower>().Count() == 1
                && otherSession.Orbs.Value(OrbKind.Tide) == otherTide, "single passive and no cross-owner recursion");

            // Execute native death rather than flipping a predicate directly.
            await CreatureCmd.Kill(ironclad.Creature, force: true);
            Check(ironclad.Creature.IsDead && !CombatManager.Instance.IsOverOrEnding, "native teammate death leaves combat active");
            int deadBlock = ironclad.Creature.Block;
            librarianBlock = librarian.Creature.Block;
            await PlayWater(owner, false);
            Check(ironclad.Creature.Block == deadBlock && librarian.Creature.Block == librarianBlock + 8,
                "dead teammate skipped while living teammate still gains eight");
            MainFile.Logger.Info($"CARD040_WATER_BOUNDARY_AUDIT_PASS checks={checks} players=3 upgraded=True dead=True dexFrail=True twoLibrarians=True liveMulticlient=False");
        }
        finally
        {
            combat.RemoveCreature(ironclad.Creature);
            combat.RemoveCreature(librarian.Creature);
        }
    }
}
