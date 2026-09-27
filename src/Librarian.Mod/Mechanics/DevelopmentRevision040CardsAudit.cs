using Godot;
using HarmonyLib;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.OrbBasics;
using Librarian.LibrarianCode.Cards.OrbAdvancedBasics;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Cards.PowerCards;
using Librarian.LibrarianCode.Powers.Implemented;
using Librarian.LibrarianCode.Cards.Stateful;
using Librarian.LibrarianCode.Cards.NativeBatch;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Random;

namespace Librarian.Mechanics;

/// <summary>Native-play checks for approved 0.4 rules; launched only by the isolated development harness.</summary>
internal static class DevelopmentRevision040CardsAudit
{
    internal static Action<LibrarianSession, SettlementRequest>? SettlementObserver;
    internal static async Task Run(Player player, Func<Task> freshFight)
    {
        var ctx = new ThrowingPlayerChoiceContext();
        int checks = 0;
        LibrarianSession S() => LibrarianRuntime.Get(player);
        void Check(bool ok, string text)
        {
            if (!ok) throw new InvalidOperationException("040 card: " + text);
            checks++; MainFile.Logger.Info("CARD040_CHECK_PASS " + text);
        }
        async Task Reset()
        {
            await freshFight();
            for (int i = 0; player.PlayerCombatState?.Phase != PlayerTurnPhase.Play; i++)
            {
                if (i > 900) throw new TimeoutException("040 native play phase");
                await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            foreach (var enemy in player.Creature.CombatState!.HittableEnemies)
                await CreatureCmd.SetMaxAndCurrentHp(enemy, 10000);
        }
        CardModel Create<T>(bool up = false) where T : CardModel
        {
            var c = player.Creature.CombatState!.CreateCard<T>(player);
            if (up) { c.UpgradeInternal(); c.FinalizeUpgradeInternal(); }
            return c;
        }
        Task Gain(OrbKind k, int n) => LibrarianRuntime.Dispatch(S(), ctx, S().Orbs.Gain(k, n));
        async Task Play(CardModel c, int? x = null)
        {
            if (x is { } count) c.EnergyCost.CapturedXValue = count;
            await CardCmd.AutoPlay(ctx, c, c.TargetType == TargetType.AnyEnemy ? player.Creature.CombatState!.HittableEnemies.First() : null,
                skipXCapture: x != null, skipCardPileVisuals: true);
        }
        int Hp() => player.Creature.CombatState!.HittableEnemies.Sum(e => e.CurrentHp);
        async Task StartTasks()
        {
            S().Orbs.BeginOwnerTurn();
            var due = S().StartTasks.Where(t => t.Turn <= S().Orbs.OwnerTurn).ToArray();
            foreach (var task in due) S().StartTasks.Remove(task);
            foreach (var task in due) await task.Action(ctx);
        }
        try
        {
            // Distribution checks exercise conservation, zero count, zero shares, and deterministic replay.
            for (int seed = 0; seed < 32; seed++)
                for (int count = 1; count <= 7; count++)
                {
                    var a = DamagePartition.Split(19, count, new AuditRandom(seed));
                    var b = DamagePartition.Split(19, count, new AuditRandom(seed));
                    Check(a.Sum() == 19 && a.All(n => n >= 0) && a.SequenceEqual(b), $"partition seed={seed} hits={count}");
                }
            Check(DamagePartition.Split(9, 0, new AuditRandom(0)).Length == 0, "X0 partition has no hit");
            foreach (bool up in new[] { false, true })
            {
                await Reset();
                void Var<T>(string variable, int plain, int upgraded) where T : CardModel
                    => Check(Create<T>(up).DynamicVars[variable].IntValue == (up ? upgraded : plain), $"approved variable {typeof(T).Name}.{variable} upgrade={up}");
                Var<ScatteredFlames>("CalculationBase", 7, 7); Var<ScatteredFlames>("ExtraDamage", 5, 7);
                Var<Ignite>("CalculationBase", 6, 6); Var<Ignite>("CalculationExtra", 4, 6);
                Var<CombatNotes>("Damage", 8, 10); Var<CombatNotes>("Block", 8, 10);
                Var<BurningPages>("Fire", 8, 11); Var<AshenBlow>("Damage", 31, 37);
                Var<TidalErosion>("Damage", 9, 13); Var<Evaporation>("Fire", 6, 9);
                Var<CirculationNotes>("Cards", 1, 1); Var<CirculationNotes>("AllyCards", 3, 4);
                Var<ZeroSearch>("Damage", 10, 14); Var<RingCurriculum>("RingCurriculumPower", 4, 6);
                Var<UnretreatingTide>("UnretreatingTidePower", 8, 12);
                Var<BurnRoots>("Fire", 6, 8); Var<BurnRoots>("Damage", 9, 12);
                Var<AncientSpark>("Fire", 5, 10); Var<OpeningTide>("Tide", 5, 7);
                Var<WaterSpirit>("Tide", 8, 13);
                foreach (var pair in new (CardModel Card, int Cost)[] {
                    (Create<Lifeline>(up), 1), (Create<OverlimitForm>(up), 3),
                    (Create<TreeRingBurst>(up), 2), (Create<RingCurriculum>(up), 1),
                    (Create<BuildCanal>(up), up ? 0 : 1) })
                    Check(pair.Card.EnergyCost.GetWithModifiers(CostModifiers.None) == pair.Cost, "approved fixed cost " + pair.Card.Id + " " + up);
                var life = Create<Lifeline>(up); var form = Create<OverlimitForm>(up);
                Check(!life.Keywords.Contains(CardKeyword.Retain) && life.Keywords.Contains(CardKeyword.Innate) == up,
                    "lifeline retain removed innate upgrade " + up);
                Check(form.Keywords.Contains(CardKeyword.Ethereal) == !up, "overlimit ethereal removal " + up);
                Check(Create<TreeRingBurst>(up).Keywords.Contains(CardKeyword.Exhaust), "rotation retains exhaust " + up);

                await Reset();
                foreach (var k in new[] { OrbKind.Fire, OrbKind.Tide })
                    await LibrarianRuntime.Dispatch(S(), ctx, S().Orbs.Lock(k));
                int lockedHp = Hp(), lockedBlock = player.Creature.Block;
                await Play(Create<ScatteredFlames>(up)); await Play(Create<Ignite>(up));
                Check(Hp() == lockedHp - 7 - 2 * (up ? 7 : 5) && player.Creature.Block == lockedBlock + 6 + 2 * (up ? 6 : 4),
                    "lock scaling uses upgrade bonus instead of base " + up);

                await Reset(); await Gain(OrbKind.Growth, 9);
                int hp = Hp(); await Play(Create<EarthCollapse>(up), 3);
                Check(Hp() == hp - 3 * (up ? 11 : 8) - 9, "soil total conserved " + up);
                Check(S().Orbs.Value(OrbKind.Growth) == 0 && !S().Orbs.IsLocked(OrbKind.Growth), "soil clears without locking " + up);
                await Reset(); await Gain(OrbKind.Growth, 9); hp = Hp();
                await Play(Create<EarthCollapse>(up), 0);
                Check(Hp() == hp && S().Orbs.Value(OrbKind.Growth) == 0, "soil X0 clears without damage " + up);

                await Reset(); await Gain(OrbKind.Growth, 7); hp = Hp();
                await Play(Create<ThornBurst>(up));
                Check(Hp() == hp - 7 * (up ? 2 : 1) * player.Creature.CombatState!.HittableEnemies.Count() && S().Orbs.Value(OrbKind.Growth) == 7
                    && S().Orbs.LockedTurns(OrbKind.Growth) == 2, "thorn preserves growth and locks last " + up);

                await Reset(); await Gain(OrbKind.Fire, 4); hp = Hp(); int settlements = S().Orbs.SettlementsThisCombat;
                await Play(Create<OverloadBurn>(up));
                Check(Hp() == hp - (up ? 5 : 3) - 4 && S().Orbs.IsActivated(OrbKind.Fire)
                    && S().Orbs.SettlementsThisCombat == settlements + 1, "borrow keeps activation " + up);
                await Reset(); await Gain(OrbKind.Fire, 4);
                await PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>(ctx, player.Creature, 2m, player.Creature, null);
                hp = Hp(); await Play(Create<BookBurning>(up));
                Check(Hp() == hp - 24 && S().Orbs.Value(OrbKind.Fire) == 0, "burning four independent Strength applications " + up);

                await Reset(); await Gain(OrbKind.Fire, 4); int block = player.Creature.Block;
                await Play(Create<QuietEmbers>(up));
                Check(player.Creature.Block == block + (up ? 13 : 10) && !S().Orbs.IsActivated(OrbKind.Fire)
                    && !S().Orbs.IsLocked(OrbKind.Fire), "quiet extinguishes without lock " + up);

                await Reset();
                foreach (var k in Enum.GetValues<OrbKind>()) await Gain(k, 1);
                settlements = S().Orbs.SettlementsThisCombat;
                var expectedOrder = Enumerable.Range(0, up ? 2 : 1).SelectMany(_ => S().Orbs.Positions.ToArray()).ToArray();
                var actualOrder = new List<OrbKind>();
                SettlementObserver = (session, request) => { if (session.Player == player) actualOrder.Add(request.Orb); };
                try { await Play(Create<TreeRingBurst>(up)); }
                finally { SettlementObserver = null; }
                Check(S().Orbs.SettlementsThisCombat == settlements + (up ? 6 : 3)
                    && S().Orbs.Positions.All(S().Orbs.IsActivated), "rotation rounds preserve activation " + up);
                Check(actualOrder.SequenceEqual(expectedOrder), "rotation exact whole-round position order " + up);

                await Reset();
                var survivor = await CreatureCmd.Add<Tunneler>(player.Creature.CombatState!);
                await CreatureCmd.SetMaxAndCurrentHp(survivor, 10000);
                foreach (var p in survivor.Powers.ToArray()) await PowerCmd.Remove(p);
                survivor.LoseBlockInternal(survivor.Block);
                var fragile = player.Creature.CombatState!.HittableEnemies.First(e => e != survivor);
                await CreatureCmd.SetMaxAndCurrentHp(fragile, 1);
                // Zero growth still shuffles three zero shares (3 then 2 RNG bounds).
                var targets = player.Creature.CombatState.HittableEnemies.ToArray();
                bool seeded = false;
                for (ulong seed = 0; seed < 1000; seed++)
                {
                    var rng = new Rng(seed); var saved = rng.ToSerializable();
                    rng.NextInt(3); rng.NextInt(2);
                    if (targets[rng.NextInt(targets.Length)] != fragile) continue;
                    player.RunState.Rng.CombatTargets.LoadFromSerializable(saved); seeded = true; break;
                }
                Check(seeded, "soil retarget deterministic seed available " + up);
                await Play(Create<EarthCollapse>(up), 3);
                Check(fragile.IsDead && survivor.CurrentHp == 10000 - 2 * (up ? 11 : 8), "soil subsequent hits reselect living target " + up);

                await Reset(); await Gain(OrbKind.Fire, 5);
                survivor = await CreatureCmd.Add<Tunneler>(player.Creature.CombatState!);
                await CreatureCmd.SetMaxAndCurrentHp(survivor, 10000);
                fragile = player.Creature.CombatState!.HittableEnemies.First(e => e != survivor);
                await CreatureCmd.SetMaxAndCurrentHp(fragile, 1);
                settlements = S().Orbs.SettlementsThisCombat;
                await CardCmd.AutoPlay(ctx, Create<OverloadBurn>(up), fragile, skipCardPileVisuals: true);
                Check(fragile.IsDead && survivor.CurrentHp == 10000 && S().Orbs.SettlementsThisCombat == settlements,
                    "borrow first hit kill does not redirect or count settlement " + up);

                await Reset(); await Play(Create<AncientCatalog>(up));
                int TotalOther() => S().Orbs.Value(OrbKind.Fire) + S().Orbs.Value(OrbKind.Tide);
                await Gain(OrbKind.Growth, 3);
                Check(TotalOther() == (up ? 5 : 3), "catalog first gain triggers once " + up);
                await Gain(OrbKind.Growth, 2);
                Check(TotalOther() == 2 * (up ? 5 : 3), "catalog repeated gain " + up);
                await LibrarianRuntime.Dispatch(S(), ctx, S().Orbs.Activate(OrbKind.Growth, OrbScope.All));
                Check(TotalOther() == 3 * (up ? 5 : 3), "catalog repeated channel " + up);
                await Gain(OrbKind.Growth, 0);
                await LibrarianRuntime.Dispatch(S(), ctx, S().Orbs.Strengthen(OrbKind.Growth, 2, OrbScope.All));
                Check(TotalOther() == 3 * (up ? 5 : 3), "catalog excludes zero gain and strengthen " + up);
                foreach (var k in Enum.GetValues<OrbKind>())
                    await LibrarianRuntime.Dispatch(S(), ctx, S().Orbs.Lock(k));
                await Gain(OrbKind.Growth, 1);
                Check(TotalOther() == 4 * (up ? 5 : 3), "catalog locked source and targets " + up);

                await Reset(); await Play(Create<CropRotation>(up)); await Play(Create<CropRotation>(up));
                Check(S().StartTasks.Count == 2, "crop casts keep separate chains " + up);
                foreach (var k in Enum.GetValues<OrbKind>()) await LibrarianRuntime.Dispatch(S(), ctx, S().Orbs.Lock(k));
                for (int turn = 0; turn < (up ? 3 : 2); turn++) await StartTasks();
                Check(S().StartTasks.Count == 0 && S().Orbs.Positions.All(k => !S().Orbs.IsActivated(k)), "crop skipped starts expire " + up);

                await Reset(); int energy = player.PlayerCombatState!.Energy;
                await Play(Create<BuildCanal>(up));
                Check(S().Orbs.Value(OrbKind.Tide) == 4 && player.PlayerCombatState.Energy == energy + 2, "canal effects " + up);

                await Reset(); energy = player.PlayerCombatState!.Energy;
                int hand = player.PlayerCombatState.Hand.Cards.Count;
                await Play(Create<ReadWidely>(up));
                Check(player.PlayerCombatState.Energy == energy + 1 && player.PlayerCombatState.Hand.Cards.Count == hand
                    && S().Orbs.Value(OrbKind.Fire) == 2 && S().Orbs.Value(OrbKind.Tide) == 3 && S().Orbs.Value(OrbKind.Growth) == 4,
                    "read widely gains energy instead of drawing and preserves gain order " + up);

                await Reset(); settlements = S().Orbs.SettlementsThisCombat;
                await Play(Create<AncientSpark>(up));
                Check(S().Orbs.Value(OrbKind.Fire) == (up ? 10 : 5) && S().Orbs.SettlementsThisCombat == settlements + 2
                    && S().Orbs.IsActivated(OrbKind.Fire), "ancient spark two extra settlements without extinguish " + up);

                await Reset();
                var spark = (ImmortalSpark)Create<ImmortalSpark>(up);
                await Play(spark);
                Check(S().Orbs.Value(OrbKind.Fire) == 1 && spark.PermanentIncrease == (up ? 3 : 2), "permanent spark current versus next gain " + up);
                var loaded = (ImmortalSpark)CardModel.FromSerializable(spark.ToSerializable());
                Check(loaded.PermanentIncrease == (up ? 3 : 2) && loaded.DynamicVars["Fire"].IntValue == (up ? 4 : 3)
                    && loaded.DynamicVars["Increase"].IntValue == (up ? 3 : 2), "permanent spark serialized growth and upgrade " + up);
                var clone = (ImmortalSpark)spark.CreateClone();
                clone.PermanentIncrease += 5;
                Check(spark.PermanentIncrease == (up ? 3 : 2) && clone.PermanentIncrease == (up ? 8 : 7), "permanent spark clone independence " + up);
                if (up)
                {
                    loaded.DowngradeInternal();
                    Check(loaded.PermanentIncrease == 3 && loaded.DynamicVars["Fire"].IntValue == 4 && loaded.DynamicVars["Increase"].IntValue == 2,
                        "permanent spark downgrade retains accrued value");
                }
            }
            MainFile.Logger.Info($"CARD040_RUNTIME_AUDIT_PASS checks={checks}");
        }
        finally { SettlementObserver = null; await Reset(); }
    }
    private sealed class AuditRandom(int seed) : IOrbRandom
    {
        private readonly Random _random = new(seed);
        public int NextInt(int exclusiveMax) => _random.Next(exclusiveMax);
    }

    /// <summary>Call in a clean two-player fight, on the players' turn; not a multi-client claim.</summary>
    internal static async Task RunWaterSpirit(Player owner, Player ally)
    {
        var ctx = new ThrowingPlayerChoiceContext();
        var session = LibrarianRuntime.Get(owner);
        var card = owner.Creature.CombatState!.CreateCard<WaterSpirit>(owner);
        int before = ally.Creature.Block;
        await CardCmd.AutoPlay(ctx, card, null, skipCardPileVisuals: true);
        if (ally.Creature.Block != before + 8 || session.Orbs.Value(OrbKind.Tide) != 8)
            throw new InvalidOperationException("040 water: initial gain must trigger new passive");
        // Two copies must not multiply the passive.
        card = owner.Creature.CombatState.CreateCard<WaterSpirit>(owner);
        before = ally.Creature.Block;
        await CardCmd.AutoPlay(ctx, card, null, skipCardPileVisuals: true);
        if (ally.Creature.Block != before + 8)
            throw new InvalidOperationException("040 water: single passive after recast");
        await PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.BeaconOfHopePower>(ctx, owner.Creature, 1m, owner.Creature, null);
        await PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.BeaconOfHopePower>(ctx, ally.Creature, 1m, ally.Creature, null);
        int ownBlock = owner.Creature.Block, allyBlock = ally.Creature.Block;
        int tide = session.Orbs.Value(OrbKind.Tide);
        await LibrarianRuntime.Dispatch(session, ctx, session.Orbs.Gain(OrbKind.Tide, 8));
        if (owner.Creature.Block != ownBlock + 4 || ally.Creature.Block != allyBlock + 10
            || session.Orbs.Value(OrbKind.Tide) != tide + 8)
            throw new InvalidOperationException("040 water: Beacon chain must terminate without Tide feedback");
        MainFile.Logger.Info("CARD040_WATER_SPIRIT_PASS initial=8 recast=single beaconOwner=4 beaconAlly=10");
    }

    /// <summary>Use a non-Librarian player in a clean native combat; checks actual history hooks and play.</summary>
    internal static async Task RunForeignTranscribe(Player foreign)
    {
        if (foreign.Character is Librarian.LibrarianCode.Character.LibrarianCharacter || LibrarianRuntime.TryGet(foreign, out _))
            throw new InvalidOperationException("040 foreign fixture must begin without Librarian orb state");
        var ctx = new ThrowingPlayerChoiceContext();
        var combat = foreign.Creature.CombatState!;
        // Register Transcribe first so the real native before-play hook enables independent history.
        var transcribe = combat.CreateCard<Transcribe>(foreign);
        await CardPileCmd.AddGeneratedCardToCombat(transcribe, PileType.Hand, foreign);
        var original = combat.CreateCard<MegaCrit.Sts2.Core.Models.Cards.DefendIronclad>(foreign);
        original.UpgradeInternal(); original.FinalizeUpgradeInternal();
        await CardCmd.AutoPlay(ctx, original, null, skipCardPileVisuals: true);
        if (LibrarianCrossCharacter040.LastPlayed(foreign)?.Id != original.Id)
            throw new InvalidOperationException("040 foreign history did not capture native card");
        var before = foreign.PlayerCombatState!.Hand.Cards.ToHashSet();
        await CardCmd.AutoPlay(ctx, transcribe, null, skipCardPileVisuals: true);
        var generated = foreign.PlayerCombatState.Hand.Cards.Where(c => !before.Contains(c)).ToArray();
        if (generated.Length != 1 || generated[0].Id != original.Id || generated[0].CurrentUpgradeLevel != original.CurrentUpgradeLevel
            || !generated[0].Keywords.Contains(CardKeyword.Ethereal) || !generated[0].Keywords.Contains(CardKeyword.Exhaust)
            || LibrarianRuntime.TryGet(foreign, out _))
            throw new InvalidOperationException("040 foreign copy mismatch or hidden orb session allocated");
        MainFile.Logger.Info("CARD040_FOREIGN_TRANSCRIBE_PASS nativeHistory=True upgradedClone=True ethereal=True exhaust=True hiddenOrbs=False");

        // Keep the hand small so a full hand cannot suppress any of the three native draws.
        foreach (var card in foreign.PlayerCombatState.Hand.Cards.ToArray())
            await CardPileCmd.Add(card, PileType.Discard, skipVisuals: true);
        var selected = combat.CreateCard<MegaCrit.Sts2.Core.Models.Cards.DefendIronclad>(foreign);
        await CardPileCmd.AddGeneratedCardToCombat(selected, PileType.Hand, foreign);
        for (int i = 0; i < 3; i++)
            await CardPileCmd.AddGeneratedCardToCombat(
                combat.CreateCard<MegaCrit.Sts2.Core.Models.Cards.DefendIronclad>(foreign), PileType.Draw, foreign);
        int handBefore = foreign.PlayerCombatState.Hand.Cards.Count;
        var selector = new MegaCrit.Sts2.Core.TestSupport.TestCardSelector();
        selector.PrepareToSelect(new[] { selected });
        using (CardSelectCmd.PushSelector(selector))
            await CardCmd.AutoPlay(ctx, combat.CreateCard<Nourish>(foreign), null, skipCardPileVisuals: true);
        if (foreign.PlayerCombatState.Hand.Cards.Count != handBefore + 2 || selected.Pile?.Type != PileType.Draw
            || foreign.PlayerCombatState.DrawPile.Cards.Last() != selected || LibrarianRuntime.TryGet(foreign, out _))
            throw new InvalidOperationException("040 foreign Nourish must draw three, bottom one, without hidden orb session");
        MainFile.Logger.Info("CARD040_FOREIGN_NOURISH_PASS drawn=3 placedBottom=1 netHand=2 hiddenOrbs=False");
    }
}

// No observer is installed outside the explicit isolated audit.
[HarmonyPatch(typeof(LibrarianRuntime), nameof(LibrarianRuntime.Settle))]
internal static class DevelopmentRevision040SettlementObserver
{
    [HarmonyPrefix]
    private static void Before(LibrarianSession session, SettlementRequest request)
        => DevelopmentRevision040CardsAudit.SettlementObserver?.Invoke(session, request);
}
