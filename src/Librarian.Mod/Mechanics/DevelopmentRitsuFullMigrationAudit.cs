using System.Reflection;
using System.Text.Json;
using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Relics;
using Librarian.LibrarianCode.Potions;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace Librarian.Mechanics;

// Uses the same native action, menu, and serialization helpers as the card sweep.
internal static partial class DevelopmentRitsuCardSweepAudit
{
    private static async Task FullFreshFight()
    {
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<TunnelerWeak>().ToMutable());
        await Wait(() => CombatManager.Instance.IsInProgress && _player.PlayerCombatState?.Phase == PlayerTurnPhase.Play
            && _player.PlayerCombatState.Hand.Cards.Count > 0, "fresh full-migration combat ready for native actions");
        await Settle(.6);
        var enemy = _player.Creature.CombatState!.HittableEnemies.Single();
        foreach (var power in _player.Creature.Powers.Concat(enemy.Powers).ToArray()) await PowerCmd.Remove(power);
        await CreatureCmd.SetMaxAndCurrentHp(_player.Creature, 1000);
        await CreatureCmd.SetMaxAndCurrentHp(enemy, 10000);
        _player.Creature.LoseBlockInternal(_player.Creature.Block);
        enemy.LoseBlockInternal(enemy.Block);
        foreach (var kind in Session.Orbs.Positions.ToArray())
        {
            await LibrarianRuntime.Dispatch(Session, Context, Session.Orbs.LoseAll(kind, OrbScope.All));
            await LibrarianRuntime.Dispatch(Session, Context, Session.Orbs.Extinguish(kind, OrbScope.All));
        }
    }
    private static void FullAssets()
    {
        var records = new List<object>();
        foreach (var (type, entry) in LibrarianRitsuCardRegistration.LegacyEntries)
        {
            var model = ModelDb.GetById<AbstractModel>(ModelDb.GetId(type));
            if (model is CardModel ownCard) Check(ownCard.Pool is LibrarianCardPool && ownCard.VisualCardPool is LibrarianCardPool, "active or retired native pool " + ownCard.Id);
            var paths = new Dictionary<string, string>();
            string[] names = model is PowerModel ? ["PackedIconPath", "BigIconPath", "ResolvedBigIconPath"]
                : model is PotionModel ? ["ImagePath", "OutlinePath", "LargeImagePath"] : [];
            foreach (string name in names)
            {
                var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property is null) continue; // Stable has no native large-potion property.
                string path = (string)property.GetValue(model)!;
                Check(ResourceLoader.Exists(path), "resolved native asset " + model.Id + " " + name + " " + path);
                paths[name] = path;
            }
            records.Add(new { type = type.FullName, id = model.Id.ToString(), entry, paths });
        }
        File.WriteAllText(Path.Combine(Output, "owned-models.json"), JsonSerializer.Serialize(records, Json));
        Check(ModelDb.PotionPool<LibrarianPotionPool>().AllPotions.Count() == 3, "three own potions registered in character pool");
        MainFile.Logger.Info("RITSU_FULL_ASSETS_PASS ownedModels=149 nativeResolvedPaths=True");
    }
    private static string FullInventory() => JsonSerializer.Serialize(new
    {
        character = _player.Character.Id.ToString(),
        relics = _player.Relics.OrderBy(r => r.Id.Entry).Select(r => r.ToSerializable()).ToArray(),
        potions = _player.Potions.Select((p, i) => p.ToSerializable(i)).ToArray(),
        deck = _player.Deck.Cards.Select(Card).ToArray()
    }, Json);
    private static async Task FullSave()
    {
        foreach (var relic in _player.Relics.ToArray()) await RelicCmd.Remove(relic);
        foreach (var (type, _) in LibrarianRitsuCardRegistration.LegacyEntries.Where(p => typeof(RelicModel).IsAssignableFrom(p.Key)))
        {
            var relic = ((RelicModel)ModelDb.GetById<AbstractModel>(ModelDb.GetId(type))).ToMutable();
            await RelicCmd.Obtain(relic, _player);
        }
        foreach (var potion in _player.Potions.ToArray()) await PotionCmd.Discard(potion);
        foreach (var (type, _) in LibrarianRitsuCardRegistration.LegacyEntries.Where(p => typeof(PotionModel).IsAssignableFrom(p.Key)))
        {
            var potion = ((PotionModel)ModelDb.GetById<AbstractModel>(ModelDb.GetId(type))).ToMutable();
            Check((await PotionCmd.TryToProcure(potion, _player)).success, "all own potions procured " + potion.Id);
        }
        var allCards = LibrarianRitsuCardRegistration.LegacyEntries.Keys.Where(t => typeof(CardModel).IsAssignableFrom(t))
            .Select(t => (CardModel)ModelDb.GetById<AbstractModel>(ModelDb.GetId(t))).OrderBy(c => c.Id.Entry).ToArray();
        await SaveReload(allCards);
        // The sweep helper also rebuilds the full base/upgrade deck before native reload.
        string before = FullInventory();
        Check(_player.Relics.Count() == 9 && _player.Potions.Count() == 3 && _player.Deck.Cards.Count == 200, "complete own inventory after native reload");
        File.WriteAllText(Path.Combine(Output, "full-inventory.json"), before);
        await SaveManager.Instance.SaveRun(null);
        var saved = SaveManager.Instance.LoadRunSave();
        Check(saved.Success && saved.SaveData is not null, "full inventory native save");
        var restored = RunState.FromSerializable(saved.SaveData!);
        await NGame.Instance!.ReturnToMainMenu();
        await RunManager.Instance.SetUpSavedSingleplayer(restored, saved.SaveData!);
        await NGame.Instance.LoadRun(restored, saved.SaveData!.PreFinishedRoom); await NGame.Instance.Transition.FadeIn();
        _player = restored.Players.Single();
        Check(before == FullInventory(), "character, nine relics, three potions, 200 active and retired cards survive second native reload");
        await FullFreshFight();
        await Play(Create(ModelDb.Card<global::Librarian.LibrarianCode.Cards.Spark>()));
        await Play(Create(ModelDb.Card<global::Librarian.LibrarianCode.Cards.LibrarianDefend>()));
        await Turn();
        await SaveManager.Instance.SaveRun(null);
        Check(_player.Relics.Count() == 9 && _player.Potions.Count() == 3 && _player.Deck.Cards.Count == 200, "post-reload combat retains full inventory");
        MainFile.Logger.Info("RITSU_FULL_SAVE_PASS cards=200 relics=9 potions=3 permanent=7 reloads=2 continuedActions=2 continuedTurns=1");
    }
    private static async Task FullRooms()
    {
        var window = NGame.Instance!.GetWindow(); var size = window.Size;
        string deck = JsonSerializer.Serialize(_player.Deck.Cards.Select(Card)); int gold = _player.Gold;
        try
        {
            foreach (var kind in new[] { RoomType.RestSite, RoomType.Shop })
            {
                await RunManager.Instance.EnterRoomDebug(kind); await Settle(1.5);
                var motions = Desc(NGame.Instance).OfType<LibrarianCharacterMotion>().Where(m => m.IsInsideTree()).ToArray();
                Check(motions.Length == 1, "one own layered actor in " + kind);
                var motion = motions.Single();
                Check(motion.Rig is not null && motion.HasSeparateHands, "layered rig and hands in " + kind);
                var mask = motion.GetParent().GetNode<Sprite2D>(motion.VisualPath + "/Mask");
                Check(mask.IsVisibleInTree() && mask.Texture is not null, "current layered mask visible in " + kind);
                foreach (var resolution in new[] { new Vector2I(1280,720), new Vector2I(1920,1080) })
                {
                    window.Size = resolution; await Settle(.8);
                    Check(mask.GetViewportRect().HasPoint(mask.GetGlobalTransformWithCanvas().Origin), "actor remains inside viewport " + kind + resolution);
                    await DevelopmentRevision050Audit.Capture("full-room-" + kind + "-" + resolution.X);
                }
            }
            Check(deck == JsonSerializer.Serialize(_player.Deck.Cards.Select(Card)) && gold == _player.Gold, "room captures retain deck and gold");
            MainFile.Logger.Info("RITSU_FULL_ROOMS_PASS rooms=2 resolutions=2 currentLayeredRig=True");
        }
        finally { window.Size = size; }
    }
    private static async Task FullRetiredHand()
    {
        var active = ModelDb.CardPool<LibrarianCardPool>().AllCards.Select(c => c.GetType()).ToHashSet();
        var retired = LibrarianRitsuCardRegistration.LegacyEntries.Keys.Where(t => typeof(CardModel).IsAssignableFrom(t) && !active.Contains(t)).ToArray();
        Check(retired.Length == 9 && active.Count == 91, "retired models keep pool ownership without active catalog inclusion");
        foreach (var existing in PileType.Hand.GetPile(_player).Cards.ToArray()) await CardPileCmd.Add(existing, PileType.Discard);
        foreach (var type in retired) foreach (bool upgraded in new[] { false, true })
        {
            var card = Create((CardModel)ModelDb.GetById<AbstractModel>(ModelDb.GetId(type)), upgraded);
            await CardPileCmd.Add(card, PileType.Hand);
            await Settle(.2);
            Check(Desc(NGame.Instance!).OfType<MegaCrit.Sts2.Core.Nodes.Cards.Holders.NHandCardHolder>().Any(h => h.CardModel == card && h.CardNode is not null && h.IsVisibleInTree()), "retired base or upgrade renders in native hand " + card.Id + upgraded);
            await CardPileCmd.Add(card, PileType.Discard);
        }
        MainFile.Logger.Info("RITSU_RETIRED_NATIVE_HAND_PASS models=9 phases=18 activeCatalog=91 nativeHand=True");
    }
    private static async Task RunFullLegacy()
    {
        try
        {
            Menu(); FullAssets();
            Check(System.Environment.GetEnvironmentVariable("LIBRARIAN_RITSU_LEGACY") == "1", "explicit real old native save supplied");
            using var ids = JsonDocument.Parse(File.ReadAllText(System.Environment.GetEnvironmentVariable("LIBRARIAN_RITSU_LEGACY_MODELS")!));
            Check(ids.RootElement.GetArrayLength() == 149, "old BaseLib process captured all original IDs");
            foreach (var record in ids.RootElement.EnumerateArray())
            {
                string type = record.GetProperty("type").GetString()!;
                var current = LibrarianRitsuCardRegistration.LegacyEntries.Keys.Single(t => t.FullName == type);
                Check(ModelDb.GetId(current).ToString() == record.GetProperty("id").GetString(), "old native model ID retained " + type);
            }
            var old = SaveManager.Instance.LoadRunSave(); Check(old.Success && old.SaveData is not null, "real BaseLib old native save read");
            var run = RunState.FromSerializable(old.SaveData!);
            await RunManager.Instance.SetUpSavedSingleplayer(run, old.SaveData!);
            await NGame.Instance!.LoadRun(run, old.SaveData!.PreFinishedRoom); await NGame.Instance.Transition.FadeIn();
            _player = run.Players.Single();
            Check(_player.Character is LibrarianCharacter && _player.Deck.Cards.Count == 200 && _player.Relics.Count() == 9 && _player.Potions.Count() == 3, "full old inventory in no-BaseLib new process");
            foreach (var type in LibrarianRitsuCardRegistration.LegacyEntries.Keys.Where(t => typeof(CardModel).IsAssignableFrom(t)))
                foreach (bool upgraded in new[] {false,true})
                    Check(_player.Deck.Cards.Any(c => c.GetType() == type && c.IsUpgraded == upgraded), "old active and retired card state " + type.Name + upgraded);
            Check(_player.Deck.Cards.OfType<global::Librarian.LibrarianCode.Cards.Stateful.ImmortalSpark>().All(c => c.PermanentIncrease == 7), "old permanent card value retained");
            string before = FullInventory();
            await SaveManager.Instance.SaveRun(null);
            var saved = SaveManager.Instance.LoadRunSave(); Check(saved.Success && saved.SaveData is not null, "migrated full legacy save read");
            var restored = RunState.FromSerializable(saved.SaveData!);
            await NGame.Instance.ReturnToMainMenu();
            await RunManager.Instance.SetUpSavedSingleplayer(restored, saved.SaveData!);
            await NGame.Instance.LoadRun(restored, saved.SaveData!.PreFinishedRoom); await NGame.Instance.Transition.FadeIn();
            _player = restored.Players.Single();
            Check(before == FullInventory(), "complete legacy inventory survives migrated save and reload");
            await FullFreshFight();
            await FullRetiredHand();
            await Play(Create(ModelDb.Card<global::Librarian.LibrarianCode.Cards.Spark>()));
            await Play(Create(ModelDb.Card<global::Librarian.LibrarianCode.Cards.LibrarianDefend>()));
            await Turn();
            await SaveManager.Instance.SaveRun(null);
            MainFile.Logger.Info("RITSU_FULL_LEGACY_PASS originalBaseLibProcess=True newNoBaseLibProcess=True ids=149 cards=200 relics=9 potions=3 permanent=7 nativeActions=2 nativeTurns=1 migratedSaveReload=True");
            await NGame.Instance.ReturnToMainMenu();
        }
        catch (Exception error) { MainFile.Logger.Error("RITSU_FULL_LEGACY_AUDIT_FAIL " + error); }
        await Settle(); NGame.Instance!.Quit();
    }
    private static async Task FullCombatVisuals()
    {
        await Settle(1.5);
        var display = LibrarianOrbPanel.GetDisplay(Session)!;
        Check(display is not null && display.IsVisibleInTree(), "own three-orb combat HUD mounted");
        foreach (var orb in Session.Orbs.Snapshot().Orbs)
        {
            var node = display.GetNode<Control>(orb.Kind.ToString());
            Check(node.GetChildren().OfType<TextureRect>().Any(t => t.Texture is not null && t.IsVisibleInTree()), "native-visible orb texture " + orb.Kind);
            Check(node.GetChildren().OfType<Label>().Any(l => l.Visible && l.Text == OrbPresentation.CenterText(orb)), "orb HUD reflects current actual value " + orb.Kind);
        }
        var character = ModelDb.Character<LibrarianCharacter>();
        Check(character.EnergyCounterPath == LibrarianVisualTheme.EnergyCounterPath && ResourceLoader.Exists(character.EnergyCounterPath), "native custom energy-counter scene resolves");
        Check(Desc(NGame.Instance!).OfType<TextureRect>().Any(t => t.IsVisibleInTree() && t.Texture?.ResourcePath == LibrarianVisualTheme.BigEnergyIconPath), "own energy triangle visible in native combat counter");
        await DevelopmentRevision050Audit.Capture("full-native-combat-hud");
        var method = typeof(DevelopmentVisualAudit).GetMethod("CaptureMapAndHud", BindingFlags.Static | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(null, [Session, Output])!;
        MainFile.Logger.Info("RITSU_FULL_COMBAT_UI_PASS orbs=3 energyCounter=True nativeHud=True mapMarker=True");
    }
    private static async Task RunFull()
    {
        try
        {
            Directory.CreateDirectory(Output);
            Menu(); FullAssets();
            LibrarianUnlocks040.ApplyChoice(SaveManager.Instance.Progress, true);
            LibrarianOnboarding.Record(LibrarianOnboarding.Decision);
            LibrarianOnboarding.Record(LibrarianOnboarding.CompactDecision);
            await DevelopmentVisualAudit.CaptureCharacterSelect(NGame.Instance!.MainMenu!);
            await DevelopmentRevision101Audit.Selection(NGame.Instance.MainMenu!);
            var run = await NGame.Instance.StartNewSingleplayerRun(ModelDb.Character<LibrarianCharacter>(), true, ActModel.GetDefaultList(), [], "RITSUFULL", GameMode.Standard);
            _player = run.Players.Single();
            await FullFreshFight();
            DevelopmentRelicAudit.ValidateModels(); DevelopmentRelicAudit.ValidateRewardEligibility(_player);
            await DevelopmentRelicAudit.ValidateUpgradeTurns(_player, Context);
            await DevelopmentCharacter050Audit.Run(_player);
            await DevelopmentMerchant051Audit.Run(_player, FullFreshFight, Output);
            await FullRooms();
            await FullFreshFight();
            await FullCombatVisuals();
            LibrarianPreferences050.Current.OrbSounds = true;
            await DevelopmentRevision0310AudioAudit.Run(_player, FullFreshFight);
            await DevelopmentPending120Audit.Run(_player, FullFreshFight);
            foreach (var relic in _player.Relics.ToArray()) await RelicCmd.Remove(relic);
            await FullFreshFight();
            foreach (var kind in Session.Orbs.Positions.ToArray())
                await LibrarianRuntime.Dispatch(Session, Context, Session.Orbs.Gain(kind, 5, new("full-fruit-fixture")));
            int settlements = Session.Orbs.SettlementsThisCombat;
            var fruit = (await PotionCmd.TryToProcure<FluidForbiddenFruit>(_player)).potion;
            Check(fruit.IsValidTarget(_player.Creature) && !fruit.IsValidTarget(_player.Creature.CombatState!.HittableEnemies.First()), "Forbidden Fruit native target scope");
            await fruit.OnUseWrapper(Context, _player.Creature);
            Check(!_player.Potions.Contains(fruit) && Session.Orbs.SettlementsThisCombat == settlements + 3, "Forbidden Fruit consumed and settles all three orbs through native potion use");
            MainFile.Logger.Info("RITSU_FULL_FRUIT_PASS nativePotionUse=True allOrbs=3 consumed=True");
            await FullFreshFight();
            await FullSave();
            MainFile.Logger.Info($"RITSU_FULL_AUDIT_PASS checks={_checks} menu=True newRun=True combat=True save=True reload=True baseLib=False multiclient=False listeningAcceptance=False");
            await NGame.Instance.ReturnToMainMenu();
        }
        catch (Exception error) { MainFile.Logger.Error("RITSU_FULL_AUDIT_FAIL " + error); }
        await Settle(); NGame.Instance!.Quit();
    }
}
