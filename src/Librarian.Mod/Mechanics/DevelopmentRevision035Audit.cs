using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Potions;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Unlocks;

namespace Librarian.Mechanics;

/// <summary>Called only by the isolated opt-in runtime fixture; never installed as a gameplay hook.</summary>
internal static class DevelopmentRevision035Audit
{
    internal static async Task Run(Player player, Func<Task> freshFight)
    {
        var context = new ThrowingPlayerChoiceContext();
        int checks = 0;
        void Require(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("V035 content: " + label);
            checks++;
            MainFile.Logger.Info("V035_CONTENT_CHECK_PASS " + label);
        }
        LibrarianSession Session() => LibrarianRuntime.Get(player);
        Task Gain(OrbKind kind, int amount) => LibrarianRuntime.Dispatch(Session(), context,
            Session().Orbs.Gain(kind, amount, new("v035-content-audit")));
        async Task<CardModel> Play(CardModel canonical, bool upgraded)
        {
            var card = player.Creature.CombatState!.CreateCard(canonical, player);
            if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
            await CardCmd.AutoPlay(context, card, player.Creature, skipCardPileVisuals: true);
            var restored = CardModel.FromSerializable(card.ToSerializable());
            Require(restored.Id == card.Id && restored.CurrentUpgradeLevel == card.CurrentUpgradeLevel,
                "card serialization " + card.Id + " upgraded=" + upgraded);
            return card;
        }
        async Task Use<T>() where T : PotionModel
        {
            var potion = ModelDb.Potion<T>().ToMutable();
            Require((await PotionCmd.TryToProcure(potion, player)).success, "procure " + potion.Id);
            Require(potion.IsValidTarget(player.Creature), "owner target " + potion.Id);
            Require(!potion.IsValidTarget(player.Creature.CombatState!.HittableEnemies.First()), "reject enemy target " + potion.Id);
            Require(PotionModel.FromSerializable(potion.ToSerializable(0)).Id == potion.Id, "potion serialization " + potion.Id);
            await potion.OnUseWrapper(context, player.Creature);
            Require(!player.Potions.Contains(potion), "consumed " + potion.Id);
        }

        // Clear fixture inventory only; no player saves or original installation are involved.
        foreach (var potion in player.Potions.ToArray()) await PotionCmd.Discard(potion);
        var pool = ModelDb.CardPool<LibrarianCardPool>();
        foreach (var multiplayerCard in new CardModel[] { ModelDb.Card<MultiplayerPlaceholderA>(), ModelDb.Card<MultiplayerPlaceholderB>() })
        {
            Require(pool.GetUnlockedCards(UnlockState.all, CardMultiplayerConstraint.MultiplayerOnly).Contains(multiplayerCard), "multiplayer card included " + multiplayerCard.Id);
            Require(!pool.GetUnlockedCards(UnlockState.all, CardMultiplayerConstraint.SingleplayerOnly).Contains(multiplayerCard), "singleplayer card excluded " + multiplayerCard.Id);
        }
        foreach (bool upgraded in new[] { false, true })
        {
            await freshFight();
            int hp = player.Creature.CombatState!.HittableEnemies.First().CurrentHp;
            var card = await Play(ModelDb.Card<AncientSpark>(), upgraded);
            int expected = upgraded ? 15 : 12;
            Require(card.EnergyCost.GetWithModifiers(CostModifiers.None) == (upgraded ? 0 : 1), "Ancient Spark upgraded cost " + upgraded);
            Require(Session().Orbs.Value(OrbKind.Fire) == expected && Session().Orbs.IsActivated(OrbKind.Fire), "Ancient Spark Fuel " + upgraded);
            Require(Session().Orbs.SettlementsThisTurn == 1 && player.Creature.CombatState.HittableEnemies.First().CurrentHp == hp - expected,
                "Ancient Spark immediate full settlement " + upgraded);
        }
        await freshFight();
        await LibrarianRuntime.Dispatch(Session(), context, Session().Orbs.Lock(OrbKind.Fire));
        await Play(ModelDb.Card<AncientSpark>(), true);
        Require(Session().Orbs.Value(OrbKind.Fire) == 15 && !Session().Orbs.IsActivated(OrbKind.Fire)
            && Session().Orbs.SettlementsThisTurn == 0, "locked Ancient Spark gains value but cannot activate or settle or redirect");

        var tooth = (ArchaicTooth)ModelDb.Relic<ArchaicTooth>().ToMutable();
        Require(tooth.SetupForPlayer(player), "native Archaic Tooth accepts Librarian starter");
        Require(tooth.StarterCard is { } starter && CardModel.FromSerializable(starter) is Spark
            && tooth.AncientCard is { } ancient && CardModel.FromSerializable(ancient) is AncientSpark,
            "native Archaic Tooth preview maps Spark to Ancient Spark");
        Require(ArchaicTooth.TranscendenceCards.Contains(ModelDb.Card<AncientSpark>()), "Ancient Spark native transcendence registration and Dusty Tome exclusion");

        var potionPool = ModelDb.PotionPool<LibrarianPotionPool>();
        foreach (var (potion, rarity) in new (PotionModel, PotionRarity)[] {
            (ModelDb.Potion<KindlingPotion>(), PotionRarity.Common),
            (ModelDb.Potion<ClarityPotion>(), PotionRarity.Uncommon),
            (ModelDb.Potion<FluidForbiddenFruit>(), PotionRarity.Rare) })
        {
            Require(potionPool.GetUnlockedPotions(UnlockState.all).Contains(potion) && potion.Rarity == rarity,
                "potion pool eligibility and rarity " + potion.Id);
            Require(potion.Usage == PotionUsage.CombatOnly && potion.ExtraHoverTips.Any(), "potion combat use and keyword hover " + potion.Id);
        }
        await freshFight();
        await Use<KindlingPotion>();
        Require(Session().Orbs.Value(OrbKind.Fire) == 8 && Session().Orbs.IsActivated(OrbKind.Fire), "Kindling Fuel8 and activation");
        Require(Session().Orbs.SettlementsThisTurn == 0, "Kindling does not immediately settle");
        await freshFight();
        await Use<ClarityPotion>();
        Require(Session().Waves.Amount == 14 && player.Creature.Block == 0 && Session().Orbs.SettlementsThisTurn == 0,
            "Clarity adds14 Waves without Block or settlement");
        await freshFight();
        await Gain(OrbKind.Fire, 9); await Gain(OrbKind.Tide, 6); await Gain(OrbKind.Growth, 4);
        var ordered = Session().Orbs.Positions.ToArray();
        foreach (var kind in ordered)
            await LibrarianRuntime.Dispatch(Session(), context, Session().Orbs.Lock(kind, kind == OrbKind.Fire ? null : 2));
        Require(ordered.All(k => !Session().Orbs.IsActivated(k)), "locked fruit fixture extinguished");
        int beforeHp = player.Creature.CombatState!.HittableEnemies.First().CurrentHp;
        await Use<FluidForbiddenFruit>();
        Require(ordered.All(k => !Session().Orbs.IsLocked(k)), "fruit clears finite and permanent locks");
        Require(ordered.SequenceEqual(Session().Orbs.Positions) && ordered.All(k => !Session().Orbs.IsActivated(k)), "fruit preserves order and does not activate");
        Require(Session().Orbs.SettlementsThisTurn == 3 && player.Creature.CombatState.HittableEnemies.First().CurrentHp == beforeHp - 9,
            "fruit settles all three once with full Fire effect");
        // Growth is foreground: its4 strengthens lower Tide6 to10 before Tide's full settlement.
        Require(Session().Orbs.Value(OrbKind.Tide) == 10 && Session().Waves.Amount == 10 && player.Creature.Block == 10,
            "fruit position-ordered Growth then full Tide settlement");
        await freshFight();
        MainFile.Logger.Info($"V035_CONTENT_AUDIT_PASS checks={checks} cards=True potions=True native_use=True serialization=True");
    }
}
