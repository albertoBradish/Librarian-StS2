using Godot;
using HarmonyLib;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Cards.OrbBasics;
using Librarian.LibrarianCode.Cards.OrbAdvancedBasics;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Cards.PowerCards;
using Librarian.LibrarianCode.Cards.Stateful;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace Librarian.Mechanics;

/// <summary>Opt-in real-engine regression fixture. Launcher clones a dedicated validation profile.</summary>
[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static partial class DevelopmentRuntimeAudit
{
    private static bool _ran;
    private static readonly PlayerChoiceContext Context = new ThrowingPlayerChoiceContext();
    private static Player _player = null!;
    private static Creature _enemy = null!;
    private static LibrarianSession Session => LibrarianRuntime.Get(_player);
    private static int _checks;

    [HarmonyPostfix] private static void Postfix()
    {
        if (System.Environment.GetEnvironmentVariable("LIBRARIAN_041_FOCUS") == "ritsu-sweep") return;
        if (_ran || System.Environment.GetEnvironmentVariable("LIBRARIAN_RUNTIME_AUDIT") != "1"
            || System.Environment.GetEnvironmentVariable("LIBRARIAN_041_FOCUS") == "ritsu-coexist") return;
        if (!OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Runtime audit requires the dedicated validation profile.");
        _ran = true;
        Callable.From((Action)(() => { _ = Run(); })).CallDeferred();
    }

    private static void Equal<T>(T expected, T actual, string description)
    {
        _checks++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{description}: expected {expected}, got {actual}");
        MainFile.Logger.Info($"RUNTIME_CHECK_PASS {description}={actual}");
    }

    private static async Task WaitReady()
    {
        for (int i = 0; i < 900; i++)
        {
            if (CombatManager.Instance.IsInProgress && _player.PlayerCombatState?.Hand.Cards.Count > 0) return;
            await NGame.Instance!.ToSignal(NGame.Instance!.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        throw new TimeoutException("Combat did not reach the first hand.");
    }

    private static async Task FreshFight()
    {
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<TunnelerWeak>().ToMutable());
        await WaitReady();
        _enemy = _player.Creature.CombatState!.HittableEnemies.Single();
        foreach (var power in _player.Creature.Powers.Concat(_enemy.Powers).ToArray()) await PowerCmd.Remove(power);
        await CreatureCmd.SetMaxAndCurrentHp(_player.Creature, 1000);
        await CreatureCmd.SetMaxAndCurrentHp(_enemy, 10000);
        _player.Creature.LoseBlockInternal(_player.Creature.Block);
        _enemy.LoseBlockInternal(_enemy.Block);
        foreach (var kind in Session.Orbs.Positions.ToArray())
        {
            await LibrarianRuntime.Dispatch(Session, Context, Session.Orbs.LoseAll(kind, OrbScope.All));
            await LibrarianRuntime.Dispatch(Session, Context, Session.Orbs.Extinguish(kind, OrbScope.All));
        }
    }

    private static async Task<T> Play<T>(bool upgraded = false) where T : CardModel
    {
        var card = (T)_player.Creature.CombatState!.CreateCard<T>(_player);
        if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
        await CardCmd.AutoPlay(Context, card, _enemy, skipCardPileVisuals: true);
        MainFile.Logger.Info($"RUNTIME_CARD_PLAY {card.Id} upgraded={upgraded} pile={card.Pile?.Type}");
        return card;
    }

    private static Task Gain(OrbKind kind, int amount)
        => LibrarianRuntime.Dispatch(Session, Context, Session.Orbs.Gain(kind, amount, new("runtime-audit")));

    private static async Task End(bool half)
    {
        await Session.Orbs.ResolveEndTurnAsync(new(OrbScope.All, false, half), r => LibrarianRuntime.Settle(Session, Context, r),
            op => new ValueTask(LibrarianRuntime.Dispatch(Session, Context, op)),
            canSettle: () => !CombatManager.Instance.IsOverOrEnding && !_player.Creature.IsDead);
        await LibrarianRuntime.GainTidalBlock(Session, Session.Waves.TakeEndTurnAmount(Session.Orbs.OwnerTurn));
        LibrarianRuntime.VerifyBlock(Session);
    }

    private static async Task CheckRevision034()
    {
        await FreshFight();
        await Gain(OrbKind.Tide, 8);
        await Play<RidgeWard>();
        Equal(8, _player.Creature.Block, "034 Ridge Ward settles Tide before lock");
        Equal(8, Session.Waves.Amount, "034 Ridge Ward settlement grants Waves");
        Equal(1, Session.Orbs.SettlementsThisTurn, "034 Ridge Ward one immediate settlement");
        Equal(OrbView.PermanentLock, Session.Orbs.LockedTurns(OrbKind.Tide), "034 Ridge Ward remains permanent lock");
        await Play<RidgeWard>(true);
        Equal(1, Session.Orbs.SettlementsThisTurn, "034 Repeated Ridge Ward cannot settle locked Tide");

        foreach (bool upgraded in new[] { false, true })
        {
            foreach (int x in new[] { 0, 2 })
            {
                await FreshFight();
                await Gain(OrbKind.Growth, 1); await Gain(OrbKind.Tide, 2); await Gain(OrbKind.Fire, 9);
                var eruption = _player.Creature.CombatState!.CreateCard<MultipleEruption>(_player);
                if (upgraded) { eruption.UpgradeInternal(); eruption.FinalizeUpgradeInternal(); }
                eruption.EnergyCost.CapturedXValue = x;
                await CardCmd.AutoPlay(Context, eruption, _enemy, skipXCapture: true, skipCardPileVisuals: true);
                Equal(10000 - 9 * (x + (upgraded ? 1 : 0)), _enemy.CurrentHp, $"034 Eruption foreground X={x} upgrade={upgraded}");
                Equal(9, Session.Orbs.Value(OrbKind.Fire), "034 Eruption retains foreground value");
                Equal(PileType.Exhaust, eruption.Pile!.Type, "034 Eruption exhausts including zero X");
            }
            await FreshFight();
            await Gain(OrbKind.Fire, 5);
            await Play<EmberBookmark>(upgraded);
            int hand = _player.PlayerCombatState!.Hand.Cards.Count;
            int drawPile = _player.PlayerCombatState.DrawPile.Cards.Count;
            for (int n = 1; n <= 2; n++)
            {
                await LibrarianRuntime.Dispatch(Session, Context, Session.Orbs.Extinguish(OrbKind.Fire, OrbScope.All));
                Equal(5 + n * (upgraded ? 2 : 1), Session.Orbs.Value(OrbKind.Fire), "034 Bookmark gains Fire on each extinguish");
                Equal(hand, _player.PlayerCombatState.Hand.Cards.Count, "034 Bookmark does not draw hand cards");
                Equal(drawPile, _player.PlayerCombatState.DrawPile.Cards.Count, "034 Bookmark leaves draw pile unchanged");
            }
            await FreshFight();
            var symphony = await Play<LifeSymphony>(upgraded);
            Equal(1, symphony.EnergyCost.Canonical, "034 Symphony costs one");
            Equal(upgraded ? 10 : 6, Session.Orbs.Value(OrbKind.Growth), "034 Symphony revised Growth gain");
            await End(false);
            Equal(2, Session.Orbs.SettlementsThisTurn, "034 Symphony still queues one extra Growth settlement");
        }
        await FreshFight();
        await Gain(OrbKind.Fire, 9);
        await LibrarianRuntime.Dispatch(Session, Context, Session.Orbs.Lock(OrbKind.Fire));
        await Play<MultipleEruption>(true);
        Equal(0, Session.Orbs.SettlementsThisTurn, "034 Eruption skips locked foreground instead of selecting background");
        Equal(9, Session.Orbs.Value(OrbKind.Fire), "034 Locked Eruption preserves value");
        await FreshFight();
        var preview = ModelDb.Card<Transcribe>().ToMutable();
        string previewText = preview.GetDescriptionForPile(PileType.None);
        Equal(false, previewText.Contains("尚未打出") || previewText.Contains("No previous card"), "034 Transcribe encyclopedia omits combat status");
        Equal(false, previewText.Contains("\n\n"), "034 Transcribe no blank description line");
        var combatCopy = _player.Creature.CombatState!.CreateCard<Transcribe>(_player);
        await CardPileCmd.AddGeneratedCardToCombat(combatCopy, PileType.Hand, _player);
        string combatText = combatCopy.GetDescriptionForPile(PileType.Hand);
        Equal(true, combatText.Contains("尚未打出") || combatText.Contains("No previous card"), "034 Transcribe empty combat history feedback retained");
        string browseCombatCopy = combatCopy.GetDescriptionForPile(PileType.None);
        Equal(false, browseCombatCopy.Contains("尚未打出") || browseCombatCopy.Contains("No previous card"), "034 Transcribe browse context hides warning even for a combat-owned copy");
        await Play<LifeSymphony>();
        combatText = combatCopy.GetDescriptionForPile(PileType.Hand);
        Equal(false, combatText.Contains("尚未打出") || combatText.Contains("No previous card"), "034 Transcribe hides warning once history exists");
        MainFile.Logger.Info("BALANCE034_AUDIT_PASS five_cards=True native_autoplay=True");
    }

    private static async Task Run()
    {
        try
        {
            MainFile.Logger.Info("RUNTIME_AUDIT_BEGIN profile=" + OS.GetUserDataDir());
            if (System.Environment.GetEnvironmentVariable("LIBRARIAN_MOD_RESET_AUDIT") is not null)
            {
                await DevelopmentModResetAudit.Run();
                return;
            }
            if (System.Environment.GetEnvironmentVariable("LIBRARIAN_ONBOARDING_AUDIT") == "1")
            {
                await DevelopmentOnboardingAudit.Run();
                return;
            }
            if (System.Environment.GetEnvironmentVariable("LIBRARIAN_LANGUAGE_AUDIT") == "1")
            {
                try { await RunLanguageAudit(); }
                finally { NGame.Instance!.Quit(); }
                return;
            }
            if (System.Environment.GetEnvironmentVariable("LIBRARIAN_ANCIENT_042_AUDIT") == "1")
            {
                await DevelopmentAncientAudit042.Run();
                return;
            }
            if (System.Environment.GetEnvironmentVariable("LIBRARIAN_041_ONLY") == "1")
            {
                await Run041();
                return;
            }
            if (System.Environment.GetEnvironmentVariable("LIBRARIAN_040_ONLY") == "1")
            {
                await Run040();
                return;
            }
            if (System.Environment.GetEnvironmentVariable("LIBRARIAN_LEGACY_SAVE_AUDIT") == "1")
            {
                var legacy = SaveManager.Instance.LoadRunSave();
                Equal(true, legacy.Success && legacy.SaveData is not null, "Previous tester run deserializes");
                var oldRun = RunState.FromSerializable(legacy.SaveData!);
                await RunManager.Instance.SetUpSavedSingleplayer(oldRun, legacy.SaveData!);
                await NGame.Instance!.LoadRun(oldRun, legacy.SaveData!.PreFinishedRoom);
                await NGame.Instance.Transition.FadeIn();
                Equal(true, RunManager.Instance.IsInProgress, "Previous tester run enters game");
                Equal(ModelDb.Character<LibrarianCharacter>().Id, oldRun.Players.Single().Character.Id, "Previous Librarian character preserved");
                MainFile.Logger.Info("LEGACY_SAVE_AUDIT_PASS source=isolated_tester_copy original_save_untouched=True");
                return;
            }
            bool revision037VisualOnly = System.Environment.GetEnvironmentVariable("LIBRARIAN_037_VISUAL_ONLY") == "1";
            bool revision037Only = revision037VisualOnly || System.Environment.GetEnvironmentVariable("LIBRARIAN_037_ONLY") == "1";
            bool revision036Only = revision037Only || System.Environment.GetEnvironmentVariable("LIBRARIAN_036_ONLY") == "1";
            bool revision035Only = revision036Only || System.Environment.GetEnvironmentVariable("LIBRARIAN_035_ONLY") == "1";
            if (DevelopmentVisualAudit.Enabled)
            {
                await DevelopmentRevision039TextAudit.Run(NGame.Instance!.MainMenu!);
                await DevelopmentVisualAudit.CaptureCharacterSelect(NGame.Instance!.MainMenu!);
            }
            var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<LibrarianCharacter>(), true,
                ActModel.GetDefaultList(), [], "LIBRARIAN030", GameMode.Standard);
            _player = run.Players.Single();
            DevelopmentRelicAudit.ValidateRewardEligibility(_player);
            await FreshFight();
            if (DevelopmentVisualAudit.Enabled && System.Environment.GetEnvironmentVariable("LIBRARIAN_VISUAL_ONLY") == "1")
            {
                await DevelopmentVisualAudit.CaptureCombat(Session, Context, _enemy);
                MainFile.Logger.Info("VISUAL_ONLY_AUDIT_PASS gameplay_suite_not_repeated=True");
                return;
            }
            if (!revision035Only)
            {
            await LibrarianRuntime.Dispatch(Session, Context, Session.Orbs.Lock(OrbKind.Tide));
            await DevelopmentRelicAudit.ValidateUpgradeTurns(_player, Context);
            await FreshFight();
            await PowerCmd.Apply<StrengthPower>(Context, _player.Creature, 20, _player.Creature, null);
            await PowerCmd.Apply<WeakPower>(Context, _player.Creature, 2, _player.Creature, null);
            await PowerCmd.Apply<VulnerablePower>(Context, _enemy, 2, _player.Creature, null);
            await PowerCmd.Apply<ThornsPower>(Context, _enemy, 10, _enemy, null);
            await Gain(OrbKind.Fire, 15);
            await Play<Reignite>();
            Equal(9985, _enemy.CurrentHp, "Unpowered Fire ignores Strength Weak and Vulnerable");
            Equal(1000, _player.Creature.CurrentHp, "Unpowered Fire does not trigger Thorns");
            Equal(true, Session.Orbs.IsActivated(OrbKind.Fire), "Immediate Fire preserves activation");
            Equal(1, Session.Orbs.SettlementsThisTurn, "Immediate settlement counter");

            await FreshFight();
            await PowerCmd.Apply<FrailPower>(Context, _player.Creature, 2, _player.Creature, null);
            await PowerCmd.Apply<DexterityPower>(Context, _player.Creature, 20, _player.Creature, null);
            await Gain(OrbKind.Tide, 11);
            await Play<Springwater>();
            Equal(11, _player.Creature.Block, "Unpowered Tide ignores Frail and Dexterity");
            Equal(11, Session.Waves.Amount, "Frail leaves Waves unchanged");
            await LibrarianRuntime.Dispatch(Session, Context, Session.Orbs.Lock(OrbKind.Tide, 1));
            await End(true);
            Equal(11, _player.Creature.Block, "Immediate Tide suppresses Wave payout");
            Equal(5, Session.Waves.Amount, "Waves decay at the same end turn");

            // Both positional examples use full effect so their arithmetic isolates ordering.
            foreach (bool growthFirst in new[] { false, true })
            {
                await FreshFight();
                await PowerCmd.Apply<ThornsPower>(Context, _enemy, 10, _enemy, null);
                foreach (var (kind, value) in new[] { (OrbKind.Fire, 15), (OrbKind.Tide, 8), (OrbKind.Growth, 3) })
                {
                    await LibrarianRuntime.Dispatch(Session, Context, Session.Orbs.Strengthen(kind, value, OrbScope.All));
                    await LibrarianRuntime.Dispatch(Session, Context, Session.Orbs.ActivateWithoutSwitch(kind));
                }
                if (growthFirst) await LibrarianRuntime.Dispatch(Session, Context, Session.Orbs.SwapPositions(OrbKind.Fire, OrbKind.Growth));
                await End(false);
                Equal(1000, _player.Creature.CurrentHp, "Ordered unpowered Fire cannot trigger Thorns growthFirst=" + growthFirst);
                Equal(growthFirst ? 11 : 8, _player.Creature.Block, "Ordered remaining Block growthFirst=" + growthFirst);
                Equal(growthFirst ? 5 : 4, Session.Waves.Amount, "Ordered remaining Waves growthFirst=" + growthFirst);
                Equal(11, Session.Orbs.Value(OrbKind.Tide), "Growth strengthens exactly three");
                Equal(3, Session.Orbs.SettlementsThisTurn, "Natural settlement counter");
            }

            await FreshFight();
            await Gain(OrbKind.Fire, 5); await Gain(OrbKind.Growth, 3); await Gain(OrbKind.Tide, 7);
            await End(true);
            Equal(9997, _enemy.CurrentHp, "Background Fire reads Growth increase before halving");
            Equal(6, Session.Orbs.Value(OrbKind.Fire), "Background Growth floors three to one");
            Equal(7, _player.Creature.Block, "Foreground Tide full Block");
            Equal(3, Session.Waves.Amount, "Odd Tide Waves floor after settlement");

            await FreshFight();
            int energy = _player.PlayerCombatState!.Energy;
            var gravity = await Play<TidalGravity>(); await Play<TidalGravity>(true);
            Equal(energy + 5, _player.PlayerCombatState.Energy, "Repeated lock grants base and upgraded energy");
            Equal(4, Session.Orbs.LockedTurns(OrbKind.Tide), "Repeated temporary lock adds duration");
            Equal(PileType.Discard, gravity.Pile!.Type, "Tidal Gravity is not Exhaust");
            await Gain(OrbKind.Tide, 11);
            Equal(false, Session.Orbs.IsActivated(OrbKind.Tide), "Locked elemental gain stays inactive");
            Equal(11, Session.Orbs.Value(OrbKind.Tide), "Locked elemental gain adds raw value");
            var drought = await Play<DroughtEdict>();
            Equal(9989, _enemy.CurrentHp, "Drought attacks for actual Tide lost");
            Equal(-1, Session.Orbs.LockedTurns(OrbKind.Tide), "Drought permanently locks Tide");
            Equal(PileType.Exhaust, drought.Pile!.Type, "Drought exhausts");
            await Play<BurningPages>();
            Equal(6, Session.Orbs.Value(OrbKind.Fire), "Permanent repeated lock still grants Fire");
            await Play<ColdFlame>();
            Equal(5, Session.Orbs.Value(OrbKind.Tide), "Cold Flame strengthens locked Tide");
            await Play<Renewal>();
            Equal(8, Session.Orbs.Value(OrbKind.Growth), "Renewal reworked Growth");
            await Play<BurnRoots>();
            Equal(9, Session.Orbs.Value(OrbKind.Fire), "Burn Roots adds three Fire");
            Equal(3, Session.Orbs.Positions.Count(Session.Orbs.IsLocked), "All three locks coexist");
            int hp = _enemy.CurrentHp;
            await Play<ScatteredFlames>();
            Equal(hp - 22, _enemy.CurrentHp, "Locked attack is one hit with three bonuses");
            await Play<Ignite>();
            Equal(18, _player.Creature.Block, "Locked Block gains three bonuses");
            int beforeSettles = Session.Orbs.SettlementsThisTurn;
            await Play<Reignite>(true); await Play<Springwater>(true); await Play<TreeRingBurst>();
            Equal(beforeSettles, Session.Orbs.SettlementsThisTurn, "All locked immediate cards cannot settle");
            await Play<Calibrate>(true);
            Equal(3, Session.Orbs.Positions.Count(Session.Orbs.IsLocked), "All locked swap safely skips");
            await Play<SongOfIceAndFire>();
            Equal(23, _player.Creature.Block, "All locked activation choice still grants Block");

            await FreshFight();
            await Play<OverloadBurn>();
            Equal(9992, _enemy.CurrentHp, "Borrow Fire inactive deals eight only");
            await Gain(OrbKind.Fire, 9);
            await Gain(OrbKind.Tide, 1);
            await Play<OverloadBurn>(true);
            Equal(9972, _enemy.CurrentHp, "Borrow Fire upgraded targets eleven plus full background nine");
            Equal(false, Session.Orbs.IsActivated(OrbKind.Fire), "Borrow Fire extinguishes after settlement");
            Equal(9, Session.Orbs.Value(OrbKind.Fire), "Borrow Fire retains value");
            Equal(1, Session.Orbs.SettlementsThisTurn, "Borrow Fire counts one settlement");

            await FreshFight();
            await Play<UnretreatingTide>(); await Play<UnretreatingTide>(true); await Play<UnretreatingTide>();
            Equal(10, _player.Creature.GetPower<UnretreatingTidePower>()!.Amount, "Retention floors use maximum, not sum");
            Session.Waves.Add(21);
            await Play<RidgeWard>();
            await ModelDb.Singleton<LibrarianCombatHooks>().BeforeSideTurnEnd(Context, CombatSide.Player, [_player.Creature]);
            Equal(21, Session.Waves.Amount, "Endless Tide overrides floor and decay");
            Equal(0, _player.Creature.Block, "Ridge Ward immediate Tide settlement suppresses same-turn Wave payout");

            await FreshFight();
            await Play<SpacetimeTwist>(true);
            await _player.Creature.GetPower<SpacetimeTwistPower>()!.BeforeHandDraw(_player, Context, _player.Creature.CombatState!);
            Equal(1, Session.Orbs.Positions.Count(Session.Orbs.IsLocked), "Spacetime locks one random orb");
            Equal(12, Session.Orbs.Positions.Sum(Session.Orbs.Value), "Spacetime strengthens two others by six");
            Equal(2, Session.Orbs.Select(OrbScope.All, ActivationFilter.Active).Count, "Spacetime activates other available orbs");
            await Play<Sedimentation>(); await Play<Sedimentation>(true);
            var positions = string.Join(',', Session.Orbs.Positions);
            await _player.Creature.GetPower<SedimentationPower>()!.BeforeHandDraw(_player, Context, _player.Creature.CombatState!);
            Equal(22, Session.Orbs.Positions.Sum(Session.Orbs.Value), "Sedimentation sums copies once");
            Equal(positions, string.Join(',', Session.Orbs.Positions), "Sedimentation does not move positions");

            await FreshFight();
            await Gain(OrbKind.Tide, 9); await Play<Springwater>();
            int before = _enemy.CurrentHp;
            await Play<FireInscription>();
            Equal(before - 14, _enemy.CurrentHp, "Afterglow counts completed immediate settlement");
            await Play<AncientCatalog>(); await Gain(OrbKind.Growth, 0);
            Equal(3, Session.Orbs.Value(OrbKind.Fire), "Catalog strengthens one lowest orb on transition");
            await Gain(OrbKind.Growth, 0);
            Equal(3, Session.Orbs.Value(OrbKind.Fire), "Catalog does not retrigger already active Growth");
            await Play<OverlimitForm>();
            await LibrarianRuntime.Dispatch(Session, Context, Session.Orbs.Lock(OrbKind.Fire, 1));
            var ring = string.Join(',', Session.Orbs.Positions);
            await _player.Creature.GetPower<OverlimitFormPower>()!.BeforeHandDraw(_player, Context, _player.Creature.CombatState!);
            Equal(false, Session.Orbs.IsActivated(OrbKind.Fire), "Overlimit respects lock");
            Equal(ring, string.Join(',', Session.Orbs.Positions), "Overlimit preserves positions");

            await FreshFight();
            await Play<ResidualWarmth>();
            Equal(7, Session.Orbs.Value(OrbKind.Fire), "Residual Warmth ignites extinguished Fire");
            Equal(0, _player.Creature.Block, "Residual Warmth only takes one branch");
            await Play<ResidualWarmth>(true);
            Equal(11, _player.Creature.Block, "Upgraded Residual Warmth blocks when Fire is active");
            Session.Waves.Add(5);
            before = _enemy.CurrentHp;
            await Play<TidalStrike>(); await Play<TidalStrike>(true);
            Equal(before - 29, _enemy.CurrentHp, "Tidal Strike base and upgrade include eight/eleven plus Waves");
            await Gain(OrbKind.Tide, 4);
            var burial = await Play<SeaBurial>();
            Equal(PileType.Discard, burial.Pile!.Type, "Sea Burial no longer exhausts");
            var spring = await Play<Springwater>(true);
            var reignite = await Play<Reignite>(true);
            Equal(PileType.Exhaust, spring.Pile!.Type, "Upgraded Springwater still exhausts");
            Equal(PileType.Exhaust, reignite.Pile!.Type, "Upgraded Reignite still exhausts");
            await Play<BurnTheRiver>(true);
            Equal(1, Session.Orbs.LockedTurns(OrbKind.Tide), "Upgraded Burn the River locks one turn");
            await Play<OpeningTide>(true);
            Equal(8, Session.Orbs.Value(OrbKind.Tide), "Upgraded Opening Tide grants eight even while locked");
            var water = _player.Creature.CombatState!.CreateCard<WaterSpirit>(_player);
            await CardPileCmd.AddGeneratedCardToCombat(water, PileType.Draw, _player, CardPilePosition.Top);
            await CardPileCmd.Draw(Context, 1, _player);
            Equal(PileType.Hand, water.Pile!.Type, "Water Spirit draws normally once present in a deck");

            await CheckRevision034();
            }
            if (!revision036Only)
            {
                await DevelopmentRevision035Audit.Run(_player, FreshFight);
                await DevelopmentRevision035RelicAudit.Run(_player, FreshFight);
            }
            if (!revision037VisualOnly)
            {
                await DevelopmentRevision036Audit.Run(_player, FreshFight);
                await DevelopmentRevision037CardsAudit.Run(_player, FreshFight);
                await DevelopmentRevision037MultiplayerAudit.Run(_player, FreshFight);
                await DevelopmentRevision0310CardsAudit.Run(_player, FreshFight);
                await DevelopmentRevision0310AudioAudit.Run(_player, FreshFight);
            }
            if (DevelopmentVisualAudit.Enabled) await DevelopmentRevision035VisualAudit.Run(_player);
            if (DevelopmentVisualAudit.Enabled) await DevelopmentRevision037VisualAudit.Run(_player);
            if (DevelopmentVisualAudit.Enabled) await DevelopmentRevision039OrbVisualAudit.Run(_player);
            if (DevelopmentVisualAudit.Enabled) await DevelopmentRevision0311OrbVisualAudit.Run(_player);
            await SaveManager.Instance.SaveRun(null);
            var saved = SaveManager.Instance.LoadRunSave();
            Equal(true, saved.Success && saved.SaveData is not null, "Dedicated profile real save read");
            var restored = RunState.FromSerializable(saved.SaveData!);
            Equal(_player.Character.Id, restored.Players.Single().Character.Id, "Run save restores Librarian");
            await NGame.Instance!.ReturnToMainMenu();
            await RunManager.Instance.SetUpSavedSingleplayer(restored, saved.SaveData!);
            await NGame.Instance!.LoadRun(restored, saved.SaveData!.PreFinishedRoom);
            await NGame.Instance!.Transition.FadeIn();
            _player = restored.Players.Single();
            Equal(true, RunManager.Instance.IsInProgress, "Real menu-to-saved-run reload");
            MainFile.Logger.Info("SAVE_RELOAD_AUDIT_PASS boundary=map dedicated_profile=True");
            if (DevelopmentVisualAudit.Enabled && !revision035Only)
            {
                await FreshFight();
                await DevelopmentVisualAudit.CaptureCombat(Session, Context, _enemy);
            }

            // Leave a compact UI fixture for visual inspection, without modifying ordinary tester saves.
            await FreshFight();
            await CreatureCmd.SetMaxAndCurrentHp(_player.Creature, 80);
            await PowerCmd.Apply<WeakPower>(Context, _player.Creature, 2, _player.Creature, null);
            await PowerCmd.Apply<FrailPower>(Context, _player.Creature, 2, _player.Creature, null);
            await PowerCmd.Apply<DoomPower>(Context, _player.Creature, 5, _enemy, null);
            await Gain(OrbKind.Fire, 15); await Gain(OrbKind.Tide, 11); await Gain(OrbKind.Growth, 7);
            await LibrarianRuntime.Dispatch(Session, Context, Session.Orbs.Lock(OrbKind.Tide, 2));
            Session.Waves.Add(21); LibrarianRuntime.VerifyBlock(Session);
            await LibrarianRuntime.GainTidalBlock(Session, 7);
            foreach (var card in _player.PlayerCombatState!.Hand.Cards.ToArray())
                await CardPileCmd.Add(card, PileType.Discard);
            foreach (var prototype in new CardModel[] { ModelDb.Card<Calibrate>(), ModelDb.Card<Nourish>(), ModelDb.Card<SongOfIceAndFire>(),
                ModelDb.Card<ScatteredFlames>(), ModelDb.Card<Ignite>(), ModelDb.Card<FireInscription>() })
            {
                var card = _player.Creature.CombatState!.CreateCard(prototype, _player);
                await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, _player);
            }
            MainFile.Logger.Info("RUNTIME_VISUAL_FIXTURE_READY weak_frail_doom_waves_lock=True");
            MainFile.Logger.Info($"RUNTIME_AUDIT_PASS checks={_checks} real_card_autoplay=True full_manual_acceptance=False");
            if (System.Environment.GetEnvironmentVariable("LIBRARIAN_CHOICE_AUDIT") == "1")
            {
                var choiceContext = new BlockingPlayerChoiceContext();
                var beforePositions = string.Join(',', Session.Orbs.Positions);
                var choiceCard = _player.PlayerCombatState!.Hand.Cards.OfType<Calibrate>().Single();
                MainFile.Logger.Info("CHOICE_AUDIT_READY Calibrate");
                await CardCmd.AutoPlay(choiceContext, choiceCard, null, skipCardPileVisuals: true);
                Equal(false, beforePositions == string.Join(',', Session.Orbs.Positions), "Native choice swaps two available orbs");
                Equal(OrbKind.Tide, Session.Orbs.Positions[1], "Native choice keeps locked Tide position");
                var tidy = _player.PlayerCombatState.Hand.Cards.OfType<Nourish>().Single();
                MainFile.Logger.Info("CHOICE_AUDIT_READY Nourish");
                await CardCmd.AutoPlay(choiceContext, tidy, null, skipCardPileVisuals: true);
                Equal(true, _player.PlayerCombatState.DrawPile.Cards.Count > 0, "Native hand choice places a card in draw pile");
                var song = _player.PlayerCombatState.Hand.Cards.OfType<SongOfIceAndFire>().FirstOrDefault()
                    ?? _player.Creature.CombatState!.CreateCard<SongOfIceAndFire>(_player);
                MainFile.Logger.Info("CHOICE_AUDIT_READY SongOfIceAndFire");
                await CardCmd.AutoPlay(choiceContext, song, null, skipCardPileVisuals: true);
                MainFile.Logger.Info("CHOICE_AUDIT_PASS three_native_screens=True");
            }
        }
        catch (Exception error) { MainFile.Logger.Error($"RUNTIME_AUDIT_FAIL checks={_checks} {error}"); }
    }
}


