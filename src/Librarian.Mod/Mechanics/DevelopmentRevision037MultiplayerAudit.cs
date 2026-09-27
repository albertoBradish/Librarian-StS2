using Godot;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.NativeBatch;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Unlocks;

namespace Librarian.Mechanics;

/// <summary>Isolated local engine checks, with transient combat actors; not a multi-client network test.</summary>
internal static class DevelopmentRevision037MultiplayerAudit
{
    internal static async Task Run(Player player, Func<Task> freshFight)
    {
        var context = new ThrowingPlayerChoiceContext();
        var extras = new List<Player>();
        int checks = 0;
        void Check(bool condition, string text)
        {
            if (!condition) throw new InvalidOperationException("037 multiplayer: " + text);
            checks++; MainFile.Logger.Info("MP037_CHECK_PASS " + text);
        }
        async Task Reset()
        {
            foreach (var extra in extras)
                if (extra.Creature.CombatState is CombatState combat) combat.RemoveCreature(extra.Creature);
            extras.Clear();
            await freshFight();
            for (int frame = 0; player.PlayerCombatState?.Phase != PlayerTurnPhase.Play; frame++)
            {
                if (frame >= 900) throw new TimeoutException("037 multiplayer fixture did not reach Play phase.");
                await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
        Player Extra(ulong id)
        {
            var extra = Player.CreateForNewRun<Ironclad>(UnlockState.all, id);
            extra.RunState = player.RunState;
            extra.ResetCombatState();
            ((CombatState)player.Creature.CombatState!).AddPlayer(extra);
            extras.Add(extra);
            return extra;
        }
        async Task<T> Play<T>(Creature target, bool upgraded = false) where T : CardModel
        {
            var card = player.Creature.CombatState!.CreateCard<T>(player);
            if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
            await CardCmd.AutoPlay(context, card, target, skipCardPileVisuals: true);
            return card;
        }
        async Task<List<CardModel>> SeedDraw(Player owner)
        {
            foreach (var c in owner.PlayerCombatState!.Hand.Cards.ToArray())
                await CardPileCmd.Add(c, PileType.Discard, skipVisuals: true);
            var seeded = new List<CardModel>();
            for (int i = 0; i < 5; i++)
            {
                var c = player.Creature.CombatState!.CreateCard<WearyIncantation>(owner);
                seeded.Add(c);
                await CardPileCmd.Add(c, PileType.Draw, CardPilePosition.Top, skipVisuals: true);
            }
            return seeded;
        }
        try
        {
            var pool = ModelDb.CardPool<LibrarianCardPool>();
            var ids = new[] { ModelDb.Card<CirculationNotes>().Id, ModelDb.Card<SharedShelter>().Id };
            foreach (var card in new CardModel[] { ModelDb.Card<CirculationNotes>(), ModelDb.Card<SharedShelter>() })
                Check(card.Rarity == CardRarity.Uncommon && card.Type == CardType.Skill && card.EnergyCost.Canonical == 1
                    && card.TargetType == TargetType.AnyAlly && card.MultiplayerConstraint == CardMultiplayerConstraint.MultiplayerOnly
                    && !card.Keywords.Contains(CardKeyword.Exhaust), "Card metadata " + card.Id);
            Check(ids.All(id => pool.GetUnlockedCards(UnlockState.all, CardMultiplayerConstraint.MultiplayerOnly).Any(c => c.Id == id)), "Both cards in multiplayer pool");
            Check(ids.All(id => !pool.GetUnlockedCards(UnlockState.all, CardMultiplayerConstraint.SingleplayerOnly).Any(c => c.Id == id)), "Both cards excluded from solo pool");

            foreach (bool upgrade in new[] { false, true })
            {
                await Reset();
                var ally = Extra(903701);
                var observer = Extra(903702);
                var ownSeed = await SeedDraw(player);
                var allySeed = await SeedDraw(ally);
                var observerSeed = await SeedDraw(observer);
                int ownDraw = player.PlayerCombatState!.DrawPile.Cards.Count;
                int allyDraw = ally.PlayerCombatState!.DrawPile.Cards.Count;
                var notes = await Play<CirculationNotes>(ally.Creature, upgrade);
                int selfCards = ownSeed.Count(c => c.Pile?.Type == PileType.Hand);
                int allyCards = allySeed.Count(c => c.Pile?.Type == PileType.Hand);
                Check(selfCards == 2 && ownDraw - player.PlayerCombatState.DrawPile.Cards.Count == 2, $"Notes self draws exactly two upgrade={upgrade} actual={selfCards}");
                Check(allyCards == (upgrade ? 4 : 3) && allyDraw - ally.PlayerCombatState.DrawPile.Cards.Count == (upgrade ? 4 : 3), $"Notes chosen ally draw upgrade={upgrade} actual={allyCards}");
                Check(observerSeed.All(c => c.Pile?.Type == PileType.Draw), "Notes nonselected player unaffected " + upgrade);
                Check(notes.Pile?.Type == PileType.Discard && notes.EnergyCost.GetWithModifiers(CostModifiers.None) == 1, "Notes reusable and one cost " + upgrade);

                await Reset();
                ally = Extra(903701); observer = Extra(903702);
                await Play<SharedShelter>(ally.Creature, upgrade);
                Check(player.Creature.Block == (upgrade ? 11 : 8) && ally.Creature.Block == (upgrade ? 11 : 8), "Shelter exact two-player ordinary Block " + upgrade);
                Check(observer.Creature.Block == 0 && LibrarianRuntime.Get(player).Waves.Amount == 0, "Shelter no other recipients or Waves " + upgrade);

                await Reset();
                ally = Extra(903701);
                await PowerCmd.Apply<DexterityPower>(context, player.Creature, 2, player.Creature, null);
                await PowerCmd.Apply<DexterityPower>(context, ally.Creature, 7, ally.Creature, null);
                await PowerCmd.Apply<FrailPower>(context, ally.Creature, 1, ally.Creature, null);
                var shelter = await Play<SharedShelter>(ally.Creature, upgrade);
                int expected = (upgrade ? 11 : 8) + 2;
                Check(player.Creature.Block == expected && ally.Creature.Block == expected * 3 / 4,
                    $"Shelter native caster Dexterity and target Frail upgrade={upgrade} self={player.Creature.Block} ally={ally.Creature.Block}");
                Check(shelter.Pile?.Type == PileType.Discard && shelter.EnergyCost.GetWithModifiers(CostModifiers.None) == 1, "Shelter reusable and one cost " + upgrade);
            }

            await Reset();
            var targetAlly = Extra(903701);
            var enemy = player.Creature.CombatState!.HittableEnemies.First();
            // AutoPlay is deliberately invoked with stale/invalid targets to verify safe action-time guards.
            foreach (var invalidTarget in new[] { player.Creature, enemy })
            {
                int hand = player.PlayerCombatState!.Hand.Cards.Count;
                await Play<CirculationNotes>(invalidTarget);
                await Play<SharedShelter>(invalidTarget);
                Check(player.PlayerCombatState.Hand.Cards.Count == hand && player.Creature.Block == 0 && enemy.Block == 0,
                    "Invalid self/enemy target grants no partial benefit " + invalidTarget.Side);
            }
            targetAlly.Creature.SetCurrentHpInternal(0);
            int handBefore = player.PlayerCombatState!.Hand.Cards.Count;
            var blockedCard = player.Creature.CombatState.CreateCard<SharedShelter>(player);
            Check(!blockedCard.IsValidTarget(targetAlly.Creature), "Native target validation rejects dead teammate");
            await Play<CirculationNotes>(targetAlly.Creature);
            await Play<SharedShelter>(targetAlly.Creature);
            Check(player.PlayerCombatState.Hand.Cards.Count == handBefore && player.Creature.Block == 0 && targetAlly.Creature.Block == 0,
                "Dead teammate action guard gives no partial benefit");
            Check(!player.RunState.Players.Contains(targetAlly), "Transient actors absent from saved run roster");
            MainFile.Logger.Info($"MP037_RUNTIME_AUDIT_PASS checks={checks} scope=local-engine-not-multiclient");
        }
        finally { await Reset(); }
    }
}
