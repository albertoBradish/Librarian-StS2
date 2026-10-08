using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Godot;
using HarmonyLib;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.Stateful;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.Mechanics;

// Identical fixture compiled separately against the original and migrated card implementations.
[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class DevelopmentRitsuCardSweepAudit
{
    private static bool _ran;
    private static int _checks, _actions, _handChoices, _gridChoices, _turns;
    private static Player _player = null!;
    private static Player? _ally;
    private static string? _desiredChoice;
    private static readonly PlayerChoiceContext Context = new ThrowingPlayerChoiceContext();
    private static readonly List<object> Results = [];
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static string Output => System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT")!;
    private static string Phase => System.Environment.GetEnvironmentVariable("LIBRARIAN_RITSU_PHASE")!;
    private static LibrarianSession Session => LibrarianRuntime.Get(_player);
    private static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException("Card sweep: " + label);
        _checks++;
    }
    [HarmonyPostfix]
    private static void Postfix()
    {
        if (_ran || System.Environment.GetEnvironmentVariable("LIBRARIAN_RUNTIME_AUDIT") != "1"
            || System.Environment.GetEnvironmentVariable("LIBRARIAN_041_FOCUS") != "ritsu-sweep") return;
        _ran = true;
        Callable.From((Action)(() => { _ = Run(); })).CallDeferred();
    }
    private static async Task Frame() => await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
    private static async Task Settle(double seconds = 1.3)
        => await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private static async Task RealDelay(double seconds)
        => await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(seconds, true, false, true), SceneTreeTimer.SignalName.Timeout);
    private static async Task Wait(Func<bool> ready, string label, int max = 3600)
    {
        for (int i = 0; !ready(); i++) { if (i >= max) throw new TimeoutException(label); await Frame(); }
    }
    private static IEnumerable<Node> Desc(Node root)
    {
        foreach (var node in root.GetChildren()) { yield return node; foreach (var child in Desc(node)) yield return child; }
    }
    private static async Task Drive(Func<bool> complete, string label)
    {
        for (int i = 0; !complete(); i++)
        {
            if (i >= 3600) throw new TimeoutException("Native controls: " + label);
            var screen = Desc(NGame.Instance!).OfType<NChooseACardSelectionScreen>().FirstOrDefault(s => s.IsVisibleInTree());
            var hand = NCombatRoom.Instance?.Ui.Hand;
            if (screen is not null)
            {
                await RealDelay(0.8);
                var holders = Desc(screen).OfType<NGridCardHolder>().Where(h => h.IsVisibleInTree()).ToArray();
                var holder = _desiredChoice is null ? holders.OrderBy(h => h.CardModel.Id.Entry, StringComparer.Ordinal).First()
                    : holders.Single(h => h.CardModel.Id.Entry == _desiredChoice);
                MainFile.Logger.Info("RITSU_SWEEP_GRID_CLICK card=" + holder.CardModel.Id);
                holder.EmitSignal(NCardHolder.SignalName.Pressed, holder);
                _gridChoices++;
                await Wait(() => !GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree() || !screen.IsVisibleInTree(), "grid closes");
            }
            else if (hand?.IsInCardSelection == true)
            {
                await RealDelay(0.8);
                var holder = Desc(hand).OfType<NHandCardHolder>().First(h => h.IsVisibleInTree() && h.CardNode is not null);
                holder.EmitSignal(NCardHolder.SignalName.Pressed, holder);
                var confirm = hand.GetNode<NConfirmButton>("%SelectModeConfirmButton");
                confirm.EmitSignal(NClickableControl.SignalName.Released, confirm);
                _handChoices++;
                await Wait(() => !hand.IsInCardSelection, "hand selection closes");
            }
            await Frame();
        }
    }
    private static object Card(CardModel card) => new
    {
        id = card.Id.Entry, upgraded = card.IsUpgraded,
        cost = card.EnergyCost.GetWithModifiers(CostModifiers.None),
        costsX = card.EnergyCost.CostsX, capturedX = card.EnergyCost.CostsX ? card.EnergyCost.CapturedXValue : (int?)null,
        keywords = card.Keywords.Select(k => k.ToString()).Order().ToArray(),
        variables = card.DynamicVars.OrderBy(p => p.Key).ToDictionary(p => p.Key, p => p.Value.BaseValue),
        permanent = (card as ImmortalSpark)?.PermanentIncrease
    };
    private static object Creature(Creature creature) => new
    {
        hp = creature.CurrentHp, block = creature.Block,
        powers = creature.Powers.OrderBy(p => p.Id.Entry).Select(p => new { id = p.Id.Entry, amount = p.Amount }).ToArray()
    };
    private static object Snapshot() => new
    {
        player = Creature(_player.Creature), energy = _player.PlayerCombatState!.Energy,
        enemies = _player.Creature.CombatState!.HittableEnemies.Select(Creature).ToArray(),
        ally = _ally is null ? null : Creature(_ally.Creature),
        allyHand = _ally?.PlayerCombatState?.Hand.Cards.Select(Card).ToArray(),
        orbs = Session.Orbs.Positions.Select(k => new { kind = k.ToString(), value = Session.Orbs.Value(k), activated = Session.Orbs.IsActivated(k), locked = Session.Orbs.LockedTurns(k) }).ToArray(),
        waves = Session.Waves.Amount, settlements = Session.Orbs.SettlementsThisCombat,
        fireLost = Session.FireLostThisCombat,
        startTasks = Session.StartTasks.Count, endTasks = Session.EndTasks.Count,
        piles = new[] { PileType.Hand, PileType.Draw, PileType.Discard, PileType.Exhaust }.ToDictionary(p => p.ToString(), p => p.GetPile(_player).Cards.Select(Card).ToArray())
    };
    private static CardModel Create(CardModel prototype, bool upgrade = false)
    {
        var card = _player.Creature.CombatState!.CreateCard(prototype, _player);
        if (upgrade) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
        return card;
    }
    private static void RemoveAlly()
    {
        if (_ally is null) return;
        var room = NCombatRoom.Instance!;
        var node = room.GetCreatureNode(_ally.Creature);
        ((CombatState)_player.Creature.CombatState!).RemoveCreature(_ally.Creature);
        if (node is not null) { room.RemoveCreatureNode(node); node.QueueFreeSafely(); }
        _ally = null;
    }
    private static async Task Reset(CardModel prototype)
    {
        // Generated-card previews last 2.2 seconds; never recycle their native nodes early.
        await Settle(2.5);
        RemoveAlly();
        foreach (var power in _player.Creature.Powers.Concat(_player.Creature.CombatState!.HittableEnemies.SelectMany(e => e.Powers)).ToArray()) await PowerCmd.Remove(power);
        foreach (var card in PileType.Hand.GetPile(_player).Cards.ToArray()) await CardPileCmd.Add(card, PileType.Discard);
        await Settle();
        Check(NCombatRoom.Instance!.Ui.Hand.ActiveHolders.Count == 0, "hand model and holder cleanup");
        foreach (var pile in new[] { PileType.Draw, PileType.Discard, PileType.Exhaust })
            foreach (var card in pile.GetPile(_player).Cards.ToArray()) await CardPileCmd.RemoveFromCombat(card, skipVisuals: true);
        ((ConditionalWeakTable<PlayerCombatState, LibrarianSession>)AccessTools.Field(typeof(LibrarianRuntime), "Sessions").GetValue(null)!).Remove(_player.PlayerCombatState!);
        await CreatureCmd.SetMaxAndCurrentHp(_player.Creature, 10000);
        _player.Creature.LoseHpInternal(1000, ValueProp.Unpowered);
        _player.Creature.LoseBlockInternal(_player.Creature.Block);
        foreach (var enemy in _player.Creature.CombatState.HittableEnemies)
        {
            await CreatureCmd.SetMaxAndCurrentHp(enemy, 100000);
            enemy.LoseBlockInternal(enemy.Block);
        }
        _player.PlayerCombatState!.LoseEnergy(_player.PlayerCombatState.Energy);
        _player.PlayerCombatState.GainEnergy(15);
        foreach (var pair in new[] { (OrbKind.Fire, 11), (OrbKind.Tide, 7), (OrbKind.Growth, 5) })
            await LibrarianRuntime.Dispatch(Session, Context, Session.Orbs.Gain(pair.Item1, pair.Item2, new("sweep-fixture")));
        for (int i = 0; i < 3; i++) await CardPileCmd.Add(Create(ModelDb.Card<LibrarianDefend>()), PileType.Hand);
        for (int i = 0; i < 12; i++) await CardPileCmd.Add(Create(ModelDb.Card<LibrarianDefend>()), PileType.Draw);
        for (int i = 0; i < 2; i++) await CardPileCmd.Add(Create(ModelDb.Card<LibrarianDefend>()), PileType.Discard);
        if (prototype.MultiplayerConstraint == CardMultiplayerConstraint.MultiplayerOnly)
        {
            _ally = Player.CreateForNewRun<Ironclad>(UnlockState.all, 903701);
            _ally.RunState = _player.RunState; _ally.ResetCombatState();
            ((CombatState)_player.Creature.CombatState!).AddPlayer(_ally);
            NCombatRoom.Instance!.AddCreature(_ally.Creature);
            await Settle();
            Check(NCombatRoom.Instance.GetCreatureNode(_ally.Creature) is not null, "native ally creature node exists");
            for (int i = 0; i < 8; i++)
                await CardPileCmd.Add(_ally.Creature.CombatState!.CreateCard(ModelDb.Card<LibrarianDefend>(), _ally), PileType.Draw, skipVisuals: true);
            if (prototype.GetType().Name == "MultiplayerPlaceholderB")
                await LibrarianRuntime.Dispatch(Session, Context, Session.Orbs.SwapPositions(Session.Orbs.Foreground, OrbKind.Fire, new("sweep-binding-fire")));
        }
        // Supply the last-card prerequisite through a genuine native play.
        if (prototype.GetType().Name == "Transcribe") await Play(Create(ModelDb.Card<LibrarianStrike>()));
        Check(!Session.ResolvingEndTurn && CardSelectCmd.Selector is null, "clean session and native selection");
    }
    private static async Task Play(CardModel card)
    {
        await CardPileCmd.Add(card, PileType.Hand);
        Creature? target = card.TargetType == TargetType.AnyEnemy ? _player.Creature.CombatState!.HittableEnemies.First()
            : card.TargetType == TargetType.AnyAlly ? _ally!.Creature : null;
        var action = new PlayCardAction(card, target);
        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
        await Drive(() => action.CompletionTask.IsCompleted, "play " + card.Id);
        Check(action.Exception is null && action.CompletionTask.IsCompletedSuccessfully, "action completed " + card.Id);
        Check(card.Pile?.Type != PileType.Play && card.Pile?.Type != PileType.Hand, "native result lifecycle " + card.Id);
        _actions++; await Settle();
    }
    private static async Task Turn()
    {
        if (_ally is not null)
        {
            // Transient actors have no network client or native hand UI. Detach after recording immediate ally effects.
            RemoveAlly();
        }
        int round = _player.Creature.CombatState!.RoundNumber;
        CombatManager.Instance.SetReadyToEndTurn(_player, false);
        await Drive(() => _player.Creature.CombatState!.RoundNumber > round && _player.PlayerCombatState!.Phase == PlayerTurnPhase.Play, "real end turn");
        Check(!Session.ResolvingEndTurn, "end-turn session cleared");
        _turns++; await Settle();
    }
    private static CardModel[] Menu()
    {
        var cards = ModelDb.CardPool<LibrarianCardPool>().AllCards.OrderBy(c => c.Id.Entry).ToArray();
        Check(cards.Length == 91 && cards.Select(c => c.Id).Distinct().Count() == 91, "91 unique active models");
        Check(cards.Count(c => c is BaseLib.Abstracts.CustomCardModel) == (Phase == "control" ? 91 : 0), "backend card ancestry");
        var retired = new List<object>();
        var cardMethod = typeof(ModelDb).GetMethods().Single(m => m.Name == "Card" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
        foreach (var type in typeof(LibrarianCard).Assembly.GetTypes().Where(t => t.IsSealed && typeof(CardModel).IsAssignableFrom(t)
            && t.Namespace?.StartsWith("Librarian.LibrarianCode.Cards") == true && !cards.Any(c => c.GetType() == t)).OrderBy(t => t.Name))
        {
            var model = (CardModel)cardMethod.MakeGenericMethod(type).Invoke(null, null)!;
            foreach (bool upgrade in new[] { false, true })
            {
                var card = model.ToMutable(); if (upgrade) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
                var restored = CardModel.FromSerializable(card.ToSerializable());
                Check(restored.Id == card.Id && restored.IsUpgraded == upgrade, "retired legacy model serialization " + card.Id);
                retired.Add(Card(card));
            }
        }
        Check(retired.Count == 18, "nine retired models stay outside active pool");
        File.WriteAllText(Path.Combine(Output, "retired-models.json"), JsonSerializer.Serialize(retired, Json));
        var records = new List<object>();
        foreach (string language in new[] { "zhs", "eng" })
        {
            LibrarianLanguage.Select(language);
            foreach (var canonical in cards) foreach (bool upgrade in new[] { false, true })
            {
                var card = canonical.ToMutable(); if (upgrade) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
                var restored = CardModel.FromSerializable(card.ToSerializable());
                Check(restored.Id == card.Id && restored.IsUpgraded == upgrade, "native model serialization");
                Check(ResourceLoader.Exists(card.PortraitPath), "portrait resource");
                Check(!card.GetDescriptionForPile(PileType.None).Contains('{'), "formatted native description");
                records.Add(new { language, model = Card(card), title = card.Title, text = card.GetDescriptionForPile(PileType.None),
                    type = card.Type.ToString(), rarity = card.Rarity.ToString(), target = card.TargetType.ToString(),
                    card.GainsBlock, portrait = card.PortraitPath, tips = card.HoverTips.Select(t => t.Id).Order().ToArray() });
            }
        }
        LibrarianLanguage.Select("zhs");
        File.WriteAllText(Path.Combine(Output, "models.json"), JsonSerializer.Serialize(records, Json));
        MainFile.Logger.Info("RITSU_SWEEP_MODELS_PASS stages=364");
        return cards;
    }
    private static async Task SaveReload(CardModel[] cards)
    {
        foreach (var card in PileType.Deck.GetPile(_player).Cards.ToArray()) await CardPileCmd.RemoveFromDeck(card, false);
        foreach (var model in cards) foreach (bool upgrade in new[] { false, true })
        {
            var card = _player.RunState.CreateCard(model, _player);
            if (upgrade) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
            if (card is ImmortalSpark immortal) immortal.PermanentIncrease = 7;
            await CardPileCmd.Add(card, PileType.Deck);
        }
        var before = JsonSerializer.Serialize(PileType.Deck.GetPile(_player).Cards.Select(Card), Json);
        await Settle(2.5); await SaveManager.Instance.SaveRun(null);
        var saved = SaveManager.Instance.LoadRunSave(); Check(saved.Success && saved.SaveData is not null, "native save read");
        File.WriteAllText(Path.Combine(Output, "run-save.json"), JsonSerializer.Serialize(saved.SaveData, Json));
        var restored = RunState.FromSerializable(saved.SaveData!);
        await NGame.Instance!.ReturnToMainMenu();
        await RunManager.Instance.SetUpSavedSingleplayer(restored, saved.SaveData!);
        await NGame.Instance.LoadRun(restored, saved.SaveData!.PreFinishedRoom); await NGame.Instance.Transition.FadeIn();
        _player = restored.Players.Single();
        var after = JsonSerializer.Serialize(PileType.Deck.GetPile(_player).Cards.Select(Card), Json);
        Check(before == after, "all 182 active base-upgrade deck cards and permanent value reload");
        File.WriteAllText(Path.Combine(Output, "reloaded-deck.json"), after);
        MainFile.Logger.Info("SAVE_RELOAD_AUDIT_PASS revision=ritsu-sweep cards=182 permanent=7");
    }
    private static async Task Run()
    {
        try
        {
            Directory.CreateDirectory(Output);
            Check(OS.GetUserDataDir().Contains("ritsu-all-cards-tests-20261007") && OS.GetUserDataDir().Contains("revision030-userdata"), "isolated profile");
            var cards = Menu();
            LibrarianUnlocks040.ApplyChoice(SaveManager.Instance.Progress, true);
            bool legacy = System.Environment.GetEnvironmentVariable("LIBRARIAN_RITSU_LEGACY") == "1";
            if (legacy)
            {
                var oldSave = SaveManager.Instance.LoadRunSave(); Check(oldSave.Success && oldSave.SaveData is not null, "old BaseLib native save read");
                var oldRun = RunState.FromSerializable(oldSave.SaveData!);
                await RunManager.Instance.SetUpSavedSingleplayer(oldRun, oldSave.SaveData!);
                await NGame.Instance!.LoadRun(oldRun, oldSave.SaveData!.PreFinishedRoom); await NGame.Instance.Transition.FadeIn();
                _player = oldRun.Players.Single();
                foreach (var model in cards) foreach (bool upgrade in new[] { false, true })
                    Check(PileType.Deck.GetPile(_player).Cards.Any(c => c.Id == model.Id && c.IsUpgraded == upgrade), "legacy deck restores " + model.Id);
                Check(PileType.Deck.GetPile(_player).Cards.OfType<ImmortalSpark>().All(c => c.PermanentIncrease == 7), "legacy permanent value");
                MainFile.Logger.Info("RITSU_SWEEP_LEGACY_PASS oldBaseLibSave=True newProcess=True cards=182");
            }
            else
            {
                var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<LibrarianCharacter>(), true, ActModel.GetDefaultList(), [], "RITSUSWEEP", GameMode.Standard);
                _player = run.Players.Single();
            }
            await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<TunnelerWeak>().ToMutable());
            await Wait(() => CombatManager.Instance.IsInProgress && _player.PlayerCombatState?.Phase == PlayerTurnPhase.Play && _player.PlayerCombatState.Hand.Cards.Count > 0, "combat initialized");
            foreach (var relic in _player.Relics.ToArray()) await RelicCmd.Remove(relic);
            Engine.TimeScale = 4;
            MainFile.Logger.Info("RITSU_SWEEP_FIXTURE timeScale=4 nativeActionQueue=True");
            bool onlyMp = System.Environment.GetEnvironmentVariable("LIBRARIAN_RITSU_ONLY") == "local-mp";
            bool onlyThreefold = System.Environment.GetEnvironmentVariable("LIBRARIAN_RITSU_ONLY") == "threefold";
            foreach (var model in cards.Where(c => !legacy || c.GetType().Name is "Spark" or "LibrarianDefend")
                .Where(c => !onlyMp || c.MultiplayerConstraint == CardMultiplayerConstraint.MultiplayerOnly)
                .Where(c => !onlyThreefold || c.GetType().Name == "ThreefoldUnity")) foreach (bool upgrade in new[] { false, true })
            foreach (string? choice in onlyThreefold ? new[] { "LIBRARIAN-RENEWAL", "LIBRARIAN-SPARK", "LIBRARIAN-TRICKLE" } : new string?[] { null })
            {
                _desiredChoice = choice;
                MainFile.Logger.Info($"RITSU_SWEEP_CASE_BEGIN id={model.Id.Entry} upgraded={upgrade}");
                await Reset(model);
                var card = Create(model, upgrade);
                if (card.EnergyCost.CostsX)
                {
                    _player.PlayerCombatState!.LoseEnergy(_player.PlayerCombatState.Energy);
                    _player.PlayerCombatState.GainEnergy(3);
                }
                var before = Snapshot(); int hand = _handChoices, grid = _gridChoices;
                await Play(card);
                if (card.Type == CardType.Power)
                {
                    _player.PlayerCombatState!.GainEnergy(5);
                    // Exercise installed hooks with a real attack and an orb-changing skill.
                    await Play(Create(ModelDb.Card<LibrarianStrike>()));
                    await Play(Create(ModelDb.Card<Spark>()));
                    if (model.GetType().Name == "WaterSpirit") await Play(Create(ModelDb.Card<Trickle>()));
                    if (model.GetType().Name == "MultiplayerPlaceholderA")
                    {
                        var alliedAttack = _ally!.Creature.CombatState!.CreateCard(ModelDb.Card<LibrarianStrike>(), _ally);
                        await CardCmd.AutoPlay(Context, alliedAttack, _player.Creature.CombatState!.HittableEnemies.First(), skipCardPileVisuals: true);
                        Check(alliedAttack.Pile?.Type == PileType.Discard, "native allied attack exercises Crowd Kindling");
                        await Settle();
                    }
                }
                var immediate = Snapshot(); var played = Card(card);
                await Turn(); var nextTurn = Snapshot();
                Results.Add(new { id = card.Id.Entry, upgraded = upgrade, choice, before, immediate, nextTurn, played,
                    handChoices = _handChoices - hand, gridChoices = _gridChoices - grid });
                File.WriteAllText(Path.Combine(Output, "cases.json"), JsonSerializer.Serialize(Results, Json));
                MainFile.Logger.Info($"RITSU_SWEEP_CASE_PASS id={model.Id.Entry} upgraded={upgrade} cases={Results.Count}");
            }
            await SaveReload(cards);
            Engine.TimeScale = 1;
            MainFile.Logger.Info($"RITSU_SWEEP_AUDIT_PASS phase={Phase} cases={Results.Count} checks={_checks} actions={_actions} turns={_turns} handChoices={_handChoices} gridChoices={_gridChoices} native=True multiclient=False");
            await NGame.Instance!.ReturnToMainMenu();
        }
        catch (Exception error) { MainFile.Logger.Error("RITSU_SWEEP_AUDIT_FAIL " + error); }
        await Settle(); NGame.Instance!.Quit();
    }
}
