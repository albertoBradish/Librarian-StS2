using Godot;
using HarmonyLib;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.OrbBasics;
using Librarian.LibrarianCode.Cards.OrbAdvancedBasics;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Cards.Stateful;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes;

namespace Librarian.Mechanics;

/// <summary>
/// Isolated native-command audit for v0.4.1 shared mechanics. It is invoked by
/// the development harness only; no save or user combat state is selected here.
/// </summary>
internal static class DevelopmentRevision041MechanicsAudit
{
    private static bool _captureTideBlock;
    private static Player? _capturePlayer;
    private static readonly List<int> CapturedTideBlockDeltas = [];

    internal static bool CaptureTideBlock(Creature creature)
        => _captureTideBlock && ReferenceEquals(creature.Player, _capturePlayer)
            && LibrarianRuntime.TryGet(_capturePlayer!, out var session)
            && session!.ResolvingEndTurn;

    internal static void RecordTideBlock(int delta)
    {
        if (delta > 0) CapturedTideBlockDeltas.Add(delta);
    }

    internal static async Task Run(Player player, Func<Task> freshFight)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(freshFight);
        var context = new ThrowingPlayerChoiceContext();
        int checks = 0;
        LibrarianSession Session() => LibrarianRuntime.Get(player);

        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("041 mechanics: " + description);
            checks++;
            MainFile.Logger.Info("MECHANICS041_CHECK_PASS " + description);
        }

        async Task WaitForPlay(long previousTurn = -1)
        {
            for (int frame = 0; frame < 1200; frame++)
            {
                if (player.PlayerCombatState?.Phase == PlayerTurnPhase.Play
                    && (previousTurn < 0 || Session().Orbs.OwnerTurn > previousTurn)) return;
                await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            throw new TimeoutException("041 native player turn did not reach Play phase");
        }

        async Task Reset()
        {
            await freshFight();
            await WaitForPlay();
            foreach (Creature enemy in player.Creature.CombatState!.HittableEnemies)
                await CreatureCmd.SetMaxAndCurrentHp(enemy, 100000);
            CapturedTideBlockDeltas.Clear();
            _captureTideBlock = false;
            _capturePlayer = null;
        }

        CardModel Create<T>() where T : CardModel
            => player.Creature.CombatState!.CreateCard<T>(player);

        async Task AddToHand(CardModel card)
            => await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, player);

        async Task AddToBottom(CardModel card)
            => await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Draw, player, CardPilePosition.Bottom);

        async Task Play(CardModel card)
        {
            Creature? target = card.TargetType == TargetType.AnyEnemy
                ? player.Creature.CombatState!.HittableEnemies.FirstOrDefault()
                : null;
            await CardCmd.AutoPlay(context, card, target, skipCardPileVisuals: true);
        }

        async Task EndTurn()
        {
            long turn = Session().Orbs.OwnerTurn;
            PlayerCmd.EndTurn(player, canBackOut: false);
            await WaitForPlay(turn);
        }

        try
        {
            // Native Wave settlement: request value is compared before Block
            // hooks, while the actual two Block gains are observed at the native
            // Creature boundary. The phase is entered by the real end-turn path.
            await Reset();
            Session().Waves.Add(15);
            await LibrarianRuntime.Dispatch(Session(), context, Session().Orbs.Gain(OrbKind.Tide, 10));
            _capturePlayer = player;
            _captureTideBlock = true;
            await EndTurn();
            _captureTideBlock = false;
            Check(CapturedTideBlockDeltas.TakeLast(2).SequenceEqual(new[] { 10, 5 }), "native Tide then frozen-Waves shortfall gains");
            Check(Session().Waves.Amount == 12, "new end-turn Waves are excluded from comparison then halve normally");

            // Lock history is combat-local, de-duplicated by orb kind, and is
            // available to the native ZeroSearch card without a current-lock shortcut.
            await Reset();
            foreach (OrbKind kind in new[] { OrbKind.Fire, OrbKind.Tide })
                await LibrarianRuntime.Dispatch(Session(), context, Session().Orbs.Lock(kind));
            await LibrarianRuntime.Dispatch(Session(), context, Session().Orbs.Unlock(OrbKind.Fire));
            await LibrarianRuntime.Dispatch(Session(), context, Session().Orbs.Lock(OrbKind.Fire));
            Check(Session().Orbs.LockedKindsThisCombatCount == 2 && Session().Orbs.WasEverLocked(OrbKind.Fire), "lock kinds survive unlock and deduplicate");
            Check(Session().Orbs.LockedKindsThisCombatCount <= 3, "lock history remains bounded to orb kinds");
            await Play(Create<ZeroSearch>());

            // Empty bottom is a no-op; an unplayable card uses native result-pile
            // routing, while a consumed/temporary flag is preserved by autoplay.
            await Reset();
            player.PlayerCombatState!.DrawPile.Clear();
            player.PlayerCombatState.DiscardPile.Clear();
            var empty = await LibrarianBottomPlay041.TryPlayBottomAsync(context, player);
            Check(!empty.Played, "empty draw-pile bottom does not shuffle or play");

            var unplayable = Create<MegaCrit.Sts2.Core.Models.Cards.Void>();
            await AddToBottom(unplayable);
            var attempted = await LibrarianBottomPlay041.TryPlayBottomAsync(context, player);
            Check(attempted.Card == unplayable && unplayable.Pile?.Type != PileType.Draw && unplayable.Pile?.Type != PileType.Play,
                "unplayable bottom card follows native result routing");

            var consumed = Create<ReadBackward>();
            consumed.AddKeyword(CardKeyword.Exhaust);
            await AddToBottom(consumed);
            await LibrarianBottomPlay041.TryPlayBottomAsync(context, player);
            Check(consumed.Pile?.Type == PileType.Exhaust, "Exhaust keyword survives bottom autoplay");

            var temporary = Create<ReadBackward>();
            temporary.ExhaustOnNextPlay = true;
            await AddToBottom(temporary);
            await LibrarianBottomPlay041.TryPlayBottomAsync(context, player);
            Check(temporary.Pile?.Type == PileType.Exhaust, "ExhaustOnNextPlay survives bottom autoplay");

            // X cards capture the live energy through CardCmd.AutoPlay rather than
            // a direct card-effect call.
            await Reset();
            await PlayerCmd.SetEnergy(4, player);
            var xCard = Create<EarthCollapse>();
            await AddToBottom(xCard);
            await LibrarianBottomPlay041.TryPlayBottomAsync(context, player);
            Check(xCard.EnergyCost.CapturedXValue == 4, "bottom X card captures current native energy");

            // Mainstem is checked exactly once at stage entry. Fuel layers are
            // frozen at entry, while each layer reads the current bottom card.
            await Reset();
            var firstMainstem = Create<BurnTheRiver>();
            var secondMainstem = Create<BurnTheRiver>();
            await AddToBottom(firstMainstem);
            await AddToBottom(secondMainstem);
            await EndTurn();
            Check(firstMainstem.Pile?.Type == PileType.Draw && secondMainstem.Pile?.Type != PileType.Draw,
                "two consecutive Mainstem cards trigger only the entry-bottom instance");

            await Reset();
            var combat = player.Creature.CombatState!;
            var mainstem = Create<BurnTheRiver>();
            var dynamicBottom = Create<Rekindle>();
            await AddToBottom(dynamicBottom);
            await CardPileCmd.AddGeneratedCardToCombat(mainstem, PileType.Draw, player, CardPilePosition.Bottom);
            await Play(Create<FuelTheFire>());
            Check(player.Creature.GetPower<FuelTheFirePower>()?.Amount == 1, "Fuel power stores one entry layer");
            int[] fireGains = [];
            Task ObserveFuelFire(LibrarianSession observed, PlayerChoiceContext _, OrbEvent change)
            {
                if (ReferenceEquals(observed, Session()) && change.Orb == OrbKind.Fire
                    && change.Kind == OrbEventKind.Gained)
                    fireGains = [.. fireGains, change.ActualAmount];
                return Task.CompletedTask;
            }
            LibrarianRuntime.OrbChanged += ObserveFuelFire;
            try { await EndTurn(); }
            finally { LibrarianRuntime.OrbChanged -= ObserveFuelFire; }
            Check(Session().EndTurnBottomSnapshot is null, "entry bottom snapshot consumed once");
            Check(fireGains.SequenceEqual(new[] { 7, 3 }),
                "Mainstem and dynamically re-read Fuel bottom emit exact Fire gains");
            Check(Session().LastPlayedSnapshot is Rekindle && dynamicBottom.Pile?.Type != PileType.Draw,
                "Fuel layer actually plays the dynamically changed bottom");
            Check(player.PlayerCombatState!.AllCards.Count(card => card is Rekindle) >= 2
                && player.PlayerCombatState.AllCards.Any(card => card is Rekindle && !ReferenceEquals(card, dynamicBottom)),
                "Fuel-played Rekindle creates an independent copy");

            // A pre-existing Fuel layer is frozen before the bottom Fuel card
            // resolves. That card may raise the power to two, but its new layer
            // cannot reach the ordinary card above it during this phase.
            await Reset();
            await Play(Create<FuelTheFire>());
            var stackedFuel = Create<FuelTheFire>();
            var ordinaryAboveFuel = Create<ReadBackward>();
            await AddToBottom(ordinaryAboveFuel);
            await AddToBottom(stackedFuel);
            Check(player.Creature.GetPower<FuelTheFirePower>()?.Amount == 1,
                "stacked Fuel fixture starts with one layer");
            await EndTurn();
            Check(player.Creature.GetPower<FuelTheFirePower>()?.Amount == 2
                && stackedFuel.Pile?.Type != PileType.Draw && stackedFuel.Pile?.Type != PileType.Play,
                "end-turn bottom Fuel plays once and raises layers to two");
            Check(Session().LastPlayedSnapshot is FuelTheFire
                && ordinaryAboveFuel.Pile?.Type is PileType.Draw or PileType.Hand,
                "AfterCardPlayed snapshot identifies Fuel and ordinary card stays unplayed");

            // Cards created during the actual end-turn stage are individually
            // retained across the native flush; old ordinary/Ethereal cards use
            // the unmodified native cleanup path.
            await Reset();
            var oldOrdinary = Create<ReadBackward>();
            var oldEthereal = Create<ReadBackward>();
            oldEthereal.AddKeyword(CardKeyword.Ethereal);
            await AddToHand(oldOrdinary);
            await AddToHand(oldEthereal);
            CardModel? newOrdinary = null;
            CardModel? newEthereal = null;
            int suppressedEtherealBefore = LibrarianCombatHooks.SuppressedEtherealTriggers;
            Session().QueueEndTurn(async _ =>
            {
                newOrdinary = Create<ReadBackward>();
                newEthereal = Create<ReadBackward>();
                newEthereal.AddKeyword(CardKeyword.Ethereal);
                await AddToHand(newOrdinary);
                await AddToHand(newEthereal);
                LibrarianBottomPlay041.RetainOnlyThisCard(newOrdinary);
                LibrarianBottomPlay041.RetainOnlyThisCard(newEthereal);
                Check(Session().EndTurnHandCards.Contains(newOrdinary)
                    && Session().EndTurnHandCards.Contains(newEthereal),
                    "narrow end-turn hand markers register both new instances");
            });
            await EndTurn();
            Check(newOrdinary is not null && newOrdinary.Pile?.Type == PileType.Hand, "new ordinary hand card crosses turn");
            Check(LibrarianCombatHooks.SuppressedEtherealTriggers > suppressedEtherealBefore,
                "native ShouldEtherealTrigger suppresses marked card before Exhaust");
            Check(newEthereal is not null && newEthereal.Pile?.Type == PileType.Hand, "new Ethereal hand card crosses turn through narrow hook");
            Check(oldOrdinary.Pile?.Type != PileType.Hand && oldEthereal.Pile?.Type != PileType.Hand,
                "old ordinary and Ethereal hand cards use native cleanup");
            Check(newOrdinary is not null && newEthereal is not null && !ReferenceEquals(newOrdinary, newEthereal), "retained cards are distinct instances");

            // Exercise the real Fuel -> Calibrate path. Calibrate passes the
            // end-turn retain flag into native top-draw and bottom-take helpers;
            // run both bottom-card keyword cases so the ordinary and Ethereal
            // instances are retained individually while old hand cards flush.
            async Task RunFuelCalibrateCase(bool bottomEthereal)
            {
                await Reset();
                player.PlayerCombatState!.DrawPile.Clear();
                player.PlayerCombatState.DiscardPile.Clear();
                var oldOrdinary = Create<LibrarianDefend>();
                var oldEthereal = Create<LibrarianDefend>();
                oldEthereal.AddKeyword(CardKeyword.Ethereal);
                await AddToHand(oldOrdinary);
                await AddToHand(oldEthereal);

                var topOrdinary = Create<LibrarianDefend>();
                var topEthereal = Create<LibrarianDefend>();
                topEthereal.AddKeyword(CardKeyword.Ethereal);
                var bottom = Create<LibrarianDefend>();
                if (bottomEthereal) bottom.AddKeyword(CardKeyword.Ethereal);
                var calibrate = Create<Calibrate>();
                // Keep the next turn's ordinary five-card draw away from the
                // returned Calibrate and from the old discarded hand cards.
                for (int i = 0; i < 12; i++) await AddToBottom(Create<LibrarianDefend>());
                await CardPileCmd.AddGeneratedCardToCombat(topOrdinary, PileType.Draw, player, CardPilePosition.Top);
                await CardPileCmd.AddGeneratedCardToCombat(topEthereal, PileType.Draw, player, CardPilePosition.Top);
                await AddToBottom(bottom);
                await AddToBottom(calibrate);
                await Play(Create<FuelTheFire>());
                int suppressedBefore = LibrarianCombatHooks.SuppressedEtherealTriggers;
                await EndTurn();

                Check(Session().LastPlayedSnapshot is Calibrate
                    && calibrate.Pile?.Type == PileType.Draw
                    && player.PlayerCombatState!.DrawPile.Cards.Contains(calibrate),
                    $"Fuel actually plays Calibrate and Calibrate returns to draw bottom ethereal={bottomEthereal}");
                Check(topOrdinary.Pile?.Type == PileType.Hand
                    && topEthereal.Pile?.Type == PileType.Hand
                    && bottom.Pile?.Type == PileType.Hand
                    && player.PlayerCombatState.Hand.Cards.Contains(topOrdinary)
                    && player.PlayerCombatState.Hand.Cards.Contains(topEthereal)
                    && player.PlayerCombatState.Hand.Cards.Contains(bottom),
                    $"Calibrate retains top ordinary/top Ethereal/bottom instance ethereal={bottomEthereal}");
                Check(oldOrdinary.Pile?.Type != PileType.Hand && oldEthereal.Pile?.Type != PileType.Hand,
                    $"Calibrate case flushes old hand cards ethereal={bottomEthereal}");
                Check(LibrarianCombatHooks.SuppressedEtherealTriggers > suppressedBefore,
                    $"Calibrate retained Ethereal instances bypass native exhaust ethereal={bottomEthereal}");
            }

            await RunFuelCalibrateCase(bottomEthereal: false);
            await RunFuelCalibrateCase(bottomEthereal: true);

            MainFile.Logger.Info($"MECHANICS041_NATIVE_AUDIT_PASS checks={checks} nativeEndTurn=True isolatedFight=True");
        }
        catch (Exception error)
        {
            MainFile.Logger.Error($"MECHANICS041_NATIVE_AUDIT_FAIL checks={checks} {error}");
            throw;
        }
        finally
        {
            _captureTideBlock = false;
            _capturePlayer = null;
            CapturedTideBlockDeltas.Clear();
            await Reset();
        }
    }
}

[HarmonyPatch(typeof(Creature), nameof(Creature.GainBlockInternal))]
internal static class DevelopmentRevision041TideBlockProbe
{
    [HarmonyPrefix]
    private static void Prefix(Creature __instance, out int __state) => __state = __instance.Block;

    [HarmonyPostfix]
    private static void Postfix(Creature __instance, int __state)
    {
        if (!DevelopmentRevision041MechanicsAudit.CaptureTideBlock(__instance)) return;
        DevelopmentRevision041MechanicsAudit.RecordTideBlock(__instance.Block - __state);
    }
}
