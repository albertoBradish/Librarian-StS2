using System.IO;
using System.Text.Json;
using Godot;
using HarmonyLib;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.PowerCards;
using Librarian.LibrarianCode.Cards.Stateful;
using Librarian.LibrarianCode.Cards.OrbBasics;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using STS2RitsuLib.Content;
using STS2RitsuLib.Scaffolding.Content;

namespace Librarian.Mechanics;

/// <summary>Opt-in native mixed-library experiment; never touches normal profiles.</summary>
[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class DevelopmentRitsuCoexistAudit
{
    private static bool _ran;
    private static int _checks, _actions;
    private static Player _player = null!;
    private static readonly PlayerChoiceContext Context = new ThrowingPlayerChoiceContext();
    private static string Phase => System.Environment.GetEnvironmentVariable("LIBRARIAN_RITSU_PHASE") ?? "mixed";
    private static string Output => System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT")
        ?? throw new InvalidOperationException("Ritsu coexist output required");
    private static LibrarianSession Session => LibrarianRuntime.Get(_player);
    private static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException("Ritsu coexist: " + label);
        _checks++; MainFile.Logger.Info("RITSU_COEXIST_CHECK_PASS " + label);
    }
    private static async Task Frame() => await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
    private static async Task Settle()
    {
        for (int i = 0; i < 75; i++) await Frame();
    }
    private static async Task Wait(Func<bool> ready, string label)
    {
        for (int i = 0; !ready(); i++)
        {
            if (i > 3600) throw new TimeoutException("Ritsu coexist: " + label);
            await Frame();
        }
    }
    [HarmonyPostfix]
    private static void Postfix()
    {
        if (_ran || System.Environment.GetEnvironmentVariable("LIBRARIAN_RUNTIME_AUDIT") != "1"
            || System.Environment.GetEnvironmentVariable("LIBRARIAN_041_FOCUS") != "ritsu-coexist") return;
        if (LibrarianRitsuCardRegistration.LegacyEntries.Count != 4)
        {
            _ran = true;
            MainFile.Logger.Warn("RITSU_COEXIST_FIXTURE_SKIPPED scope=four-cards full-card-review-awaits-test-authorization");
            return;
        }
        _ran = true;
        Callable.From((Action)(() => { _ = Run(); })).CallDeferred();
    }
    private static object Describe(CardModel card) => new
    {
        id = card.Id.Entry, upgraded = card.IsUpgraded, title = card.Title,
        text = card.GetDescriptionForPile(PileType.None),
        cost = card.EnergyCost.GetWithModifiers(CostModifiers.None),
        type = card.Type.ToString(), rarity = card.Rarity.ToString(), target = card.TargetType.ToString(),
        keywords = card.Keywords.Select(k => k.ToString()).Order().ToArray(),
        tags = card.Tags.Select(k => k.ToString()).Order().ToArray(),
        variables = card.DynamicVars.ToDictionary(p => p.Key, p => p.Value.BaseValue),
        portrait = card.PortraitPath, tips = card.HoverTips.Select(t => t.Id).Order().ToArray()
    };
    private static CardModel[] Menu()
    {
        var cards = ModelDb.CardPool<LibrarianCardPool>().AllCards.OrderBy(c => c.Id.Entry).ToArray();
        Check(cards.Length == 91 && cards.Select(c => c.Id).Distinct().Count() == 91, "91 unique active cards in mixed native pool");
        var migrating = cards.Where(c => LibrarianRitsuCardRegistration.LegacyEntries.ContainsKey(c.GetType())).ToArray();
        Check(migrating.Length == 4, "four selected cards resolve exactly once");
        bool mixed = Phase != "baseline";
        Check(cards.Count(c => c is ModCardTemplate) == (mixed ? 4 : 0), "Ritsu card count " + mixed);
        Check(cards.Count(c => c is BaseLib.Abstracts.CustomCardModel) == (mixed ? 87 : 91), "BaseLib card count " + mixed);
        foreach (var card in migrating)
        {
            Check(card.Id.Entry == LibrarianRitsuCardRegistration.LegacyEntries[card.GetType()], "published entry kept " + card.Id);
            Check(card is ILibrarianCard, "shared character marker " + card.Id);
            if (mixed)
                Check(ModContentRegistry.TryGetOwnerModId(card.GetType(), out string owner) && owner == MainFile.ModId,
                    "registered through Ritsu content pack " + card.Id);
            Check(ModelDb.CardPool<LibrarianCardPool>().GetUnlockedCards(UnlockState.all, CardMultiplayerConstraint.SingleplayerOnly).Contains(card),
                "native unlocked catalog contains " + card.Id);
        }
        var records = new List<object>();
        string original = LibrarianLanguage.Selected;
        foreach (string language in new[] { "zhs", "eng" })
        {
            LibrarianLanguage.Select(language);
            foreach (var canonical in cards)
            foreach (bool upgrade in new[] { false, true })
            {
                var card = canonical.ToMutable();
                if (upgrade) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
                string text = card.GetDescriptionForPile(PileType.None);
                Check(!string.IsNullOrEmpty(card.Title) && !string.IsNullOrEmpty(text) && !text.Contains('{'),
                    "native model formatted " + language + " " + card.Id + " " + upgrade);
                Check(ResourceLoader.Exists(card.PortraitPath) && card.Portrait.ResourcePath == card.PortraitPath,
                    "actual portrait retained " + card.Id);
                var restored = CardModel.FromSerializable(card.ToSerializable());
                Check(restored.Id == card.Id && restored.IsUpgraded == upgrade, "native model serialization " + card.Id);
                records.Add(new { language, model = Describe(card) });
            }
        }
        LibrarianLanguage.Select(original);
        File.WriteAllText(Path.Combine(Output, "models.json"), JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
        MainFile.Logger.Info($"RITSU_COEXIST_MODELS_PASS phase={Phase} models=91 stages=364 ritsu={(mixed ? 4 : 0)} baselib={(mixed ? 87 : 91)}");
        return cards;
    }
    private static async Task FreshFight()
    {
        await Settle();
        bool entered = !CombatManager.Instance.IsInProgress;
        if (entered)
            await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<TunnelerWeak>().ToMutable());
        await Wait(() => CombatManager.Instance.IsInProgress && _player.PlayerCombatState?.Phase == PlayerTurnPhase.Play
            && (!entered || _player.PlayerCombatState.Hand.Cards.Count > 0), "real combat play phase");
        foreach (var relic in _player.Relics.ToArray()) await RelicCmd.Remove(relic);
        foreach (var power in _player.Creature.Powers.Concat(_player.Creature.CombatState!.HittableEnemies.SelectMany(e => e.Powers)).ToArray())
            await PowerCmd.Remove(power);
        await CreatureCmd.SetMaxAndCurrentHp(_player.Creature, 1000);
        foreach (var enemy in _player.Creature.CombatState!.HittableEnemies) await CreatureCmd.SetMaxAndCurrentHp(enemy, 10000);
        _player.Creature.LoseBlockInternal(_player.Creature.Block);
        foreach (var kind in Session.Orbs.Positions.ToArray())
        {
            await LibrarianRuntime.Dispatch(Session, Context, Session.Orbs.LoseAll(kind, OrbScope.All));
            await LibrarianRuntime.Dispatch(Session, Context, Session.Orbs.Extinguish(kind, OrbScope.All));
        }
        await ClearHand();
        foreach (var pile in new[] { PileType.Draw, PileType.Discard, PileType.Exhaust })
            foreach (var card in pile.GetPile(_player).Cards.ToArray()) await CardPileCmd.RemoveFromCombat(card, skipVisuals: true);
        await Settle();
    }
    private static async Task ClearHand()
    {
        foreach (var card in PileType.Hand.GetPile(_player).Cards.ToArray())
            await CardPileCmd.Add(card, PileType.Discard);
        await Settle();
        Check(NCombatRoom.Instance!.Ui.Hand.ActiveHolders.Count == 0, "native hand holders removed with normal pile movement");
    }
    private static async Task<CardModel> Play(CardModel prototype, bool upgraded = false)
    {
        var card = _player.Creature.CombatState!.CreateCard(prototype, _player);
        if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
        _player.PlayerCombatState!.GainEnergy(10);
        await CardPileCmd.Add(card, PileType.Hand);
        decimal energyBefore = _player.PlayerCombatState.Energy;
        int cost = card.EnergyCost.GetWithModifiers(CostModifiers.None);
        var action = new PlayCardAction(card, card.TargetType == TargetType.AnyEnemy ? _player.Creature.CombatState.HittableEnemies.First() : null);
        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
        await Wait(() => action.CompletionTask.IsCompleted, "real play action " + card.Id);
        Check(action.Exception is null && action.CompletionTask.IsCompletedSuccessfully, "native card action completes " + card.Id);
        Check(_player.PlayerCombatState.Energy == energyBefore - cost, "native action pays exact energy " + card.Id + " " + upgraded);
        _actions++;
        await Settle();
        return card;
    }
    private static async Task Actions()
    {
        foreach (bool upgraded in new[] { false, true })
        {
            await FreshFight();
            var enemy = _player.Creature.CombatState!.HittableEnemies.First();
            int hp = enemy.CurrentHp;
            await Play(ModelDb.Card<LibrarianStrike>(), upgraded);
            Check(enemy.CurrentHp == hp - (upgraded ? 9 : 6), "migrating attack damage " + upgraded);
            await Play(ModelDb.Card<LibrarianDefend>(), upgraded);
            Check(_player.Creature.Block == (upgraded ? 8 : 5), "migrating block " + upgraded);
            await Play(ModelDb.Card<Spark>(), upgraded);
            Check(Session.Orbs.Value(OrbKind.Fire) == 5 && Session.Orbs.IsActivated(OrbKind.Fire), "migrating orb skill " + upgraded);
            await Play(ModelDb.Card<ShiftingPages>(), upgraded);
            int amount = upgraded ? 4 : 3;
            Check(_player.Creature.GetPower<ShiftingPagesPower>()?.Amount == amount, "Ritsu ability applies existing BaseLib power " + upgraded);
            int block = _player.Creature.Block;
            var foreground = Session.Orbs.Foreground;
            await Play(ModelDb.Card<Trickle>(), upgraded);
            Check(Session.Orbs.Value(OrbKind.Tide) == (upgraded ? 6 : 4), "retained BaseLib card updates same core session " + upgraded);
            Check(_player.Creature.Block == block + (foreground != Session.Orbs.Foreground ? amount : 0),
                "retained card triggers migrated ability shared power " + upgraded);
        }
        await FreshFight();
        var bottom = _player.Creature.CombatState!.CreateCard<LibrarianStrike>(_player);
        await CardPileCmd.Add(bottom, PileType.Draw, CardPilePosition.Bottom);
        int before = _player.Creature.CombatState.HittableEnemies.First().CurrentHp;
        await Play(ModelDb.Card<ReadBackward>());
        Check(_player.Creature.CombatState.HittableEnemies.First().CurrentHp == before - 6,
            "retained BaseLib bottom-card action plays migrating attack");
        Check(bottom.Pile?.Type == PileType.Discard, "migrating auto-play uses native discard lifecycle");
        MainFile.Logger.Info($"RITSU_COEXIST_ACTIONS_PASS phase={Phase} manualActions={_actions} mixedBottomPlay=True");
    }
    private static async Task Screens(CardModel[] cards)
    {
        await FreshFight();
        foreach (string language in new[] { "zhs", "eng" })
        foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
        foreach (bool upgraded in new[] { false, true })
        {
            DisplayServer.WindowSetSize(size);
            LibrarianLanguage.Select(language);
            await ClearHand();
            foreach (var prototype in cards.Where(c => LibrarianRitsuCardRegistration.LegacyEntries.ContainsKey(c.GetType()))
                .Concat(new CardModel[] { ModelDb.Card<Trickle>(), ModelDb.Card<ReadBackward>() }))
            {
                var card = _player.Creature.CombatState!.CreateCard(prototype, _player);
                if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
                await CardPileCmd.Add(card, PileType.Hand);
                Check(!card.GetDescriptionForPile(PileType.Hand).Contains('{'), "actual hand text " + card.Id + " " + language);
            }
            for (int i = 0; i < 35; i++) await Frame();
            Check(NCombatRoom.Instance!.Ui.Hand.ActiveHolders.Count == 6
                && PileType.Hand.GetPile(_player).Cards.Count == 6, "six real visible holders match six current hand models");
            string path = Path.Combine(Output, $"hand-{language}-{size.X}x{size.Y}-{(upgraded ? "upgraded" : "base")}.png");
            var image = NGame.Instance!.GetViewport().GetTexture().GetImage();
            Check(image.SavePng(path) == Error.Ok, "native hand screenshot " + language + " " + size);
        }
        LibrarianLanguage.Select("zhs");
    }
    private static async Task PrepareDeck(CardModel[] models)
    {
        foreach (var prototype in models.Where(c => LibrarianRitsuCardRegistration.LegacyEntries.ContainsKey(c.GetType())))
        foreach (bool upgraded in new[] { false, true })
        {
            var card = _player.RunState.CreateCard(prototype, _player);
            if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
            await CardPileCmd.Add(card, PileType.Deck);
        }
        var immortal = (ImmortalSpark)_player.RunState.CreateCard(ModelDb.Card<ImmortalSpark>(), _player);
        immortal.PermanentIncrease = 7; immortal.UpgradeInternal(); immortal.FinalizeUpgradeInternal();
        await CardPileCmd.Add(immortal, PileType.Deck);
        File.WriteAllText(Path.Combine(Output, "deck-before-save.json"), JsonSerializer.Serialize(
            PileType.Deck.GetPile(_player).Cards.Select(Describe), new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void CheckDeck()
    {
        var deck = PileType.Deck.GetPile(_player).Cards;
        foreach (var pair in LibrarianRitsuCardRegistration.LegacyEntries)
        {
            Check(deck.Any(c => c.Id.Entry == pair.Value && !c.IsUpgraded) && deck.Any(c => c.Id.Entry == pair.Value && c.IsUpgraded),
                "real game save preserves base and upgrade " + pair.Value);
            Check(deck.Where(c => c.Id.Entry == pair.Value).All(c => c.GetType() == pair.Key), "save restores same concrete model " + pair.Value);
        }
        Check(deck.OfType<Trickle>().Any(), "retained BaseLib deck card restored");
        Check(deck.OfType<ImmortalSpark>().Any(c => c.PermanentIncrease == 7 && c.IsUpgraded), "retained BaseLib permanent saved value restored");
        File.WriteAllText(Path.Combine(Output, "deck-after-load.json"), JsonSerializer.Serialize(deck.Select(Describe), new JsonSerializerOptions { WriteIndented = true }));
        MainFile.Logger.Info($"RITSU_COEXIST_DECK_PASS phase={Phase} legacyEntry=True migratedBaseUpgrade=True retainedPermanentValue=7");
    }
    private static async Task Run()
    {
        try
        {
            Check(OS.GetUserDataDir().Contains("ritsu-card-coexist-20261007") && OS.GetUserDataDir().Contains("revision030-userdata"),
                "isolated own user directory");
            Directory.CreateDirectory(Output);
            var cards = Menu();
            if (Phase == "legacy")
            {
                var saved = SaveManager.Instance.LoadRunSave();
                Check(saved.Success && saved.SaveData is not null, "pre-migration native save read");
                var restored = RunState.FromSerializable(saved.SaveData!);
                await RunManager.Instance.SetUpSavedSingleplayer(restored, saved.SaveData!);
                await NGame.Instance!.LoadRun(restored, saved.SaveData!.PreFinishedRoom);
                await NGame.Instance.Transition.FadeIn();
                _player = restored.Players.Single();
                CheckDeck();
            }
            else
            {
                LibrarianUnlocks040.ApplyChoice(SaveManager.Instance.Progress, true);
                var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<LibrarianCharacter>(), true,
                    ActModel.GetDefaultList(), [], "RITSUCOEXIST", GameMode.Standard);
                _player = run.Players.Single();
            }
            await Actions();
            if (Phase != "legacy")
            {
                await Screens(cards);
                await PrepareDeck(cards);
            }
            await Settle();
            await SaveManager.Instance.SaveRun(null);
            var savedAgain = SaveManager.Instance.LoadRunSave();
            Check(savedAgain.Success && savedAgain.SaveData is not null, "native mixed run save");
            var restoredAgain = RunState.FromSerializable(savedAgain.SaveData!);
            await NGame.Instance!.ReturnToMainMenu();
            await RunManager.Instance.SetUpSavedSingleplayer(restoredAgain, savedAgain.SaveData!);
            await NGame.Instance.LoadRun(restoredAgain, savedAgain.SaveData!.PreFinishedRoom);
            await NGame.Instance.Transition.FadeIn();
            _player = restoredAgain.Players.Single();
            Check(RunManager.Instance.IsInProgress, "real menu to saved-run reload");
            CheckDeck();
            await FreshFight();
            await Play(ModelDb.Card<Spark>());
            await Play(ModelDb.Card<Trickle>());
            Check(Session.Orbs.Value(OrbKind.Fire) == 5 && Session.Orbs.Value(OrbKind.Tide) == 4,
                "both libraries play after actual save and load");
            MainFile.Logger.Info("SAVE_RELOAD_AUDIT_PASS revision=ritsu-coexist");
            MainFile.Logger.Info($"RITSU_COEXIST_AUDIT_PASS phase={Phase} checks={_checks} manualActions={_actions} menu=True newRun={(Phase != "legacy")} combat=True save=True reload=True multiclient=False");
            if (Phase == "legacy") MainFile.Logger.Info("RITSU_COEXIST_LEGACY_PASS oldNativeSave=True newProcess=True");
            await NGame.Instance.ReturnToMainMenu();
        }
        catch (Exception error) { MainFile.Logger.Error("RITSU_COEXIST_AUDIT_FAIL " + error); }
        await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
        NGame.Instance.Quit();
    }
}
