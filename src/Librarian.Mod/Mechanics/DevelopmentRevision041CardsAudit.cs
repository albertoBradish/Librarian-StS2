using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.OrbAdvancedBasics;
using Librarian.LibrarianCode.Cards.OrbBasics;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Cards.Stateful;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace Librarian.Mechanics;

/// <summary>
/// V0.4.1 card-only native combat audit. The coordinator invokes this from the
/// isolated development harness; defining this entry point does not constitute
/// a build or runtime pass.
/// </summary>
internal static class DevelopmentRevision041CardsAudit
{
    internal static async Task Run(Player player, Func<Task> freshFight)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(freshFight);
        var context = new ThrowingPlayerChoiceContext();
        int checks = 0;
        LibrarianSession Session() => LibrarianRuntime.Get(player);
        void Check(bool value, string label)
        {
            if (!value) throw new InvalidOperationException("041 card: " + label);
            checks++;
            MainFile.Logger.Info("CARD041_CHECK_PASS " + label);
        }

        async Task Reset()
        {
            await freshFight();
            if (player.Creature.CombatState is null) throw new InvalidOperationException("041 audit has no combat state");
            foreach (var enemy in player.Creature.CombatState.HittableEnemies)
                await CreatureCmd.SetMaxAndCurrentHp(enemy, 10000);
        }

        CardModel Create<T>(bool upgraded = false) where T : CardModel
        {
            var card = player.Creature.CombatState!.CreateCard<T>(player);
            if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
            return card;
        }

        async Task Play(CardModel card)
        {
            var target = card.TargetType is TargetType.AnyEnemy or TargetType.RandomEnemy
                ? player.Creature.CombatState!.HittableEnemies.FirstOrDefault() : null;
            await CardCmd.AutoPlay(context, card, target, skipCardPileVisuals: true);
        }

        try
        {
            foreach (bool upgraded in new[] { false, true })
            {
                await Reset();
                Check(Create<EmberReckoning>(upgraded).DynamicVars.CalculationBase.IntValue == (upgraded ? 10 : 7), "EmberReckoning base");
                Check(Create<ResidualWarmth>(upgraded).DynamicVars.Block.IntValue == (upgraded ? 11 : 8), "ResidualWarmth block");
                Check(Create<Rekindle>(upgraded).DynamicVars["Fire"].IntValue == (upgraded ? 5 : 3), "Rekindle Fire");
                Check(!Create<ZeroSearch>(upgraded).Keywords.Contains(CardKeyword.Exhaust), "ZeroSearch no Exhaust");
                Check(Create<ThornBurst>(upgraded).EnergyCost.GetWithModifiers(CostModifiers.None) == 1, "ThornBurst cost");
                Check(Create<FuelTheFire>(upgraded).EnergyCost.GetWithModifiers(CostModifiers.None) == (upgraded ? 1 : 2), "FuelTheFire cost");
                Check(Create<ArchiveBulwark>(upgraded).EnergyCost.GetWithModifiers(CostModifiers.None) == 1
                    && Create<ArchiveBulwark>(upgraded).DynamicVars.CalculationExtra.IntValue == (upgraded ? 2 : 1)
                    && Create<ArchiveBulwark>(upgraded).Keywords.Contains(CardKeyword.Exhaust), "ArchiveBulwark scaling and Exhaust");
                Check(Create<EmberPierce>(upgraded).DynamicVars.Damage.IntValue == (upgraded ? 11 : 8), "EmberPierce damage");
                Check(Create<Calibrate>(upgraded).DynamicVars.Cards.IntValue == (upgraded ? 3 : 2)
                    && !Create<Calibrate>(upgraded).Keywords.Contains(CardKeyword.Exhaust), "Calibrate ordering vars");
                Check(Create<ReadBackward>(upgraded).EnergyCost.GetWithModifiers(CostModifiers.None) == (upgraded ? 0 : 1), "ReadBackward cost");
                Check(Create<SeaBurial>(upgraded).DynamicVars.Repeat.IntValue == (upgraded ? 3 : 2), "SeaBurial hits");
                var seed = Create<SproutingSeed>(upgraded);
                Check(seed.DynamicVars.Block.IntValue == 7 && seed.DynamicVars["Growth"].IntValue == (upgraded ? 9 : 7), "SproutingSeed upgrade");

                await LibrarianRuntime.Dispatch(Session(), context, Session().Orbs.Gain(OrbKind.Fire, 5));
                int hp = player.Creature.CombatState!.HittableEnemies.First().CurrentHp;
                var reckoning = Create<EmberReckoning>(upgraded);
                string reckoningText = reckoning.GetDescriptionForPile(PileType.Hand);
                Check(reckoningText.Contains(upgraded ? "10" : "7") && !reckoningText.Contains(upgraded ? "15" : "12"),
                    "EmberReckoning description prints base before the separate Fire explanation");
                await Play(reckoning);
                Check(player.Creature.CombatState.HittableEnemies.First().CurrentHp == hp - (upgraded ? 15 : 12), "EmberReckoning single hit");
                Check(MegaCrit.Sts2.Core.Combat.CombatManager.Instance.History.Entries
                    .OfType<MegaCrit.Sts2.Core.Combat.History.Entries.DamageReceivedEntry>()
                    .Count(entry => ReferenceEquals(entry.CardSource, reckoning)) == 1,
                    "EmberReckoning records exactly one native damage event");

                await Reset();
                await LibrarianRuntime.Dispatch(Session(), context, Session().Orbs.Gain(OrbKind.Growth, 2));
                await Play(Create<ThornBurst>(upgraded));
                var thorn = player.Creature.GetPower<ThornBurstPower>();
                Check(thorn is not null && thorn.Amount == (upgraded ? 4 : 3) && Session().Orbs.IsLocked(OrbKind.Growth), "ThornBurst power and lock");
                await thorn!.BeforeHandDraw(player, context, player.Creature.CombatState!);
                Check(Session().Orbs.Value(OrbKind.Growth) == 8 && thorn.Amount == (upgraded ? 3 : 2), "ThornBurst owner turn");

                await Reset();
                await LibrarianRuntime.Dispatch(Session(), context, Session().Orbs.Gain(OrbKind.Tide, 4));
                hp = player.Creature.CombatState!.HittableEnemies.Sum(e => e.CurrentHp);
                await Play(Create<SeaBurial>(upgraded));
                Check(Session().Orbs.Value(OrbKind.Tide) == 0 && Session().Orbs.IsLocked(OrbKind.Fire)
                    && Session().Orbs.IsLocked(OrbKind.Growth), "SeaBurial snapshot and locks");
                Check(player.Creature.CombatState.HittableEnemies.Sum(e => e.CurrentHp) == hp - 4 * (upgraded ? 3 : 2), "SeaBurial hit total");

                await Reset();
                var bottom = Create<LibrarianDefend>();
                await CardPileCmd.AddGeneratedCardToCombat(bottom, PileType.Draw, player, CardPilePosition.Bottom);
                await Play(Create<ReadBackward>(upgraded));
                Check(bottom.Pile?.Type == PileType.Discard || bottom.Pile?.Type == PileType.Exhaust, "ReadBackward played bottom");

                await Reset();
                var page = Create<LibrarianDefend>();
                await CardPileCmd.AddGeneratedCardToCombat(page, PileType.Draw, player, CardPilePosition.Bottom);
                await Play(Create<Calibrate>(upgraded));
                Check(player.PlayerCombatState!.DrawPile.Cards.LastOrDefault() is Calibrate, "Calibrate moves self bottom");

                await Reset();
                var fireCard = Create<FlameStrike>();
                await CardPileCmd.AddGeneratedCardToCombat(fireCard, PileType.Draw, player, CardPilePosition.Bottom);
                await Play(Create<FuelTheFire>(upgraded));
                var fuel = player.Creature.GetPower<FuelTheFirePower>();
                fuel!.SnapshotEndTurnLayers();
                await LibrarianBottomPlay041.TryPlayBottomAsync(context, player);
                Check(fireCard.Pile?.Type is PileType.Discard or PileType.Exhaust, "FuelTheFire bottom play");
            }

            await Reset();
            await LibrarianRuntime.Dispatch(Session(), context, Session().Orbs.Lock(OrbKind.Fire));
            await LibrarianRuntime.Dispatch(Session(), context, Session().Orbs.Lock(OrbKind.Tide));
            Check(Session().Orbs.LockedKindsThisCombatCount == 2, "ZeroSearch distinct lock history");
            Check(Session().Orbs.WasEverLocked(OrbKind.Fire) && Session().Orbs.WasEverLocked(OrbKind.Tide), "ZeroSearch lock identities");
            MainFile.Logger.Info($"CARD041_NATIVE_AUDIT_PASS checks={checks}");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error("CARD041_NATIVE_AUDIT_FAIL " + ex);
            throw;
        }
    }
}
