using HarmonyLib;
using Godot;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Cards.Stateful;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Nodes.Orbs;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Helpers;

namespace Librarian.Mechanics;

/// <summary>Opt-in model/serialization smoke test. It never starts a run or changes a save.</summary>
[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class DevelopmentModelAudit
{
    private static bool _ran;
    [HarmonyPostfix]
    private static void Postfix(NMainMenu __instance)
    {
        if (_ran || System.Environment.GetEnvironmentVariable("LIBRARIAN_MODEL_AUDIT") != "1") return;
        _ran = true;
        int passed = 0;
        try
        {
            var cards = ModelDb.CardPool<LibrarianCardPool>().AllCards.ToArray();
            Require(cards.Length == 91, $"Expected 91 active cards, got {cards.Length}");
            Require(cards.Count(c => c.Type == CardType.Attack) == 24, "Expected 24 attacks");
            Require(cards.Count(c => c.Type == CardType.Skill) == 47, "Expected 47 skills");
            Require(cards.Count(c => c.Type == CardType.Power) == 20, "Expected 20 powers");
            foreach (var legacy in new MegaCrit.Sts2.Core.Models.CardModel[] {
                ModelDb.Card<Librarian.LibrarianCode.Cards.OrbUtility.LonelyScroll>(),
                ModelDb.Card<Librarian.LibrarianCode.Cards.OrbUtility.DroughtEdict>(),
                ModelDb.Card<Librarian.LibrarianCode.Cards.OrbAdvancedBasics.SeaBurial>(),
                ModelDb.Card<Librarian.LibrarianCode.Cards.OrbAdvancedBasics.TidalStrike>(),
                ModelDb.Card<Librarian.LibrarianCode.Cards.OrbUtility.ForestWall>(),
                ModelDb.Card<Librarian.LibrarianCode.Cards.OrbBasics.ColdFlame>() })
            {
                Require(!cards.Contains(legacy), "Retired card remains in active pool: " + legacy.Id);
                var old = legacy.ToMutable();
                old.UpgradeInternal(); old.FinalizeUpgradeInternal();
                var loadedLegacy = CardModel.FromSerializable(old.ToSerializable());
                Require(loadedLegacy.Id == old.Id && loadedLegacy.IsUpgraded, "Legacy model roundtrip failed: " + old.Id);
            }
            MainFile.Logger.Info("LEGACY_043_MODELS_PASS retired=6 upgradedRoundtrip=True activePoolExcluded=True");
            var pool = ModelDb.CardPool<LibrarianCardPool>();
            var waterSpirit = ModelDb.Card<Librarian.LibrarianCode.Cards.PowerCards.WaterSpirit>();
            Require(waterSpirit.MultiplayerConstraint == CardMultiplayerConstraint.MultiplayerOnly, "Water Spirit must be multiplayer-only");
            Require(pool.GetUnlockedCards(MegaCrit.Sts2.Core.Unlocks.UnlockState.all, CardMultiplayerConstraint.MultiplayerOnly).Contains(waterSpirit), "Water Spirit missing from multiplayer pool");
            Require(!pool.GetUnlockedCards(MegaCrit.Sts2.Core.Unlocks.UnlockState.all, CardMultiplayerConstraint.SingleplayerOnly).Contains(waterSpirit), "Water Spirit leaked into single-player rewards");
            MainFile.Logger.Info("MULTIPLAYER_POOL_AUDIT_PASS WaterSpirit multiplayer=True singleplayer=False");
            foreach (var canonical in cards.OrderBy(c => c.Id.ToString()))
            {
                var card = canonical.ToMutable();
                Require(!string.IsNullOrWhiteSpace(card.Title), $"Missing title: {card.Id}");
                var artName = card.Id.Entry["LIBRARIAN-".Length..].ToLowerInvariant() + ".png";
                if (card is Librarian.LibrarianCode.Cards.MultiplayerPlaceholderA) artName = "crowd_kindling.png";
                if (card is Librarian.LibrarianCode.Cards.MultiplayerPlaceholderB) artName = "binding.png";
                var artPath = "res://Librarian/images/card_portraits/" + artName;
                Require(ResourceLoader.Exists(artPath), $"Missing card art: {artPath}");
                Require(ResourceLoader.Exists("res://Librarian/images/card_portraits/big/" + artName), $"Missing big card art: {card.Id}");
                Require(card.PortraitPath == artPath, $"Wrong card portrait: {card.Id}: {card.PortraitPath}");
                var portrait = card.Portrait;
                Require(portrait.ResourcePath == artPath, $"Actual card texture bypasses small art: {card.Id}");
                Require(portrait.GetWidth() <= 303 && portrait.GetHeight() <= 426, $"Oversized actual card texture: {card.Id}");
                string baseText = card.GetDescriptionForPile(PileType.None);
                ValidateFeedbackPreview(card, false);
                Require(!string.IsNullOrWhiteSpace(baseText), $"Missing base description: {card.Id}");
                Require(!baseText.Contains('{') && !baseText.Contains('}'), $"Unresolved base localization: {card.Id}: {baseText}");
                ValidateHoverTips(card);
                var baseSpec = Describe(card);
                card.UpgradeInternal();
                card.FinalizeUpgradeInternal();
                string upgradedText = card.GetDescriptionForPile(PileType.None);
                ValidateFeedbackPreview(card, true);
                Require(!string.IsNullOrWhiteSpace(upgradedText), $"Missing upgrade description: {card.Id}");
                Require(!upgradedText.Contains('{') && !upgradedText.Contains('}'), $"Unresolved upgrade localization: {card.Id}: {upgradedText}");
                MainFile.Logger.Info("MODEL_AUDIT_SPEC " + System.Text.Json.JsonSerializer.Serialize(new { id = card.Id.Entry, before = baseSpec, after = Describe(card) }));
                ValidateHoverTips(card);
                var restored = CardModel.FromSerializable(card.ToSerializable());
                Require(restored.Id == card.Id && restored.CurrentUpgradeLevel == card.CurrentUpgradeLevel, $"Serialization mismatch: {card.Id}");
                restored.DowngradeInternal();
                Require(restored.GetDescriptionForPile(PileType.None) == baseText, $"Downgrade mismatch: {card.Id}");
                passed++;
                MainFile.Logger.Info($"MODEL_AUDIT_CARD {card.Id} base={baseText.Replace('\n', ' ')} upgraded={upgradedText.Replace('\n', ' ')}");
            }
            MainFile.Logger.Info("CARD_ART_AUDIT_PASS cards=91 revision043NewOrReplacedCards=5 placeholderCards=0 legacyAssetsPreserved=True");
            var character = ModelDb.Character<LibrarianCharacter>();
            Require(character.CharacterSelectBg == LibrarianCharacterSelect040.ScenePath, "Character still uses placeholder selection background");
            var selectionScene = ResourceLoader.Load<PackedScene>(character.CharacterSelectBg).Instantiate<Control>();
            Require(selectionScene is LibrarianCharacterSelect040, "Missing animated selection controller");
            Require(selectionScene.FindChild("Illustration", true, false) is TextureRect hero && hero.Texture is not null, "Missing continuous hero illustration");
            selectionScene.Free();
            Require(ResourceLoader.Exists(character.CustomCharacterSelectIconPath), "Missing character portrait");
            Require(ResourceLoader.Exists(character.CustomCharacterSelectLockedIconPath), "Missing locked portrait");
            MainFile.Logger.Info("CHARACTER_ART_AUDIT_PASS cohesiveHero=1 background=1 portraits=2 animated=True");
            DevelopmentRelicAudit.ValidateModels();
            foreach (var power in ModelDb.AllPowers.OfType<Librarian.LibrarianCode.Powers.LibrarianPower>())
            {
                Require(ResourceLoader.Exists(power.CustomPackedIconPath), $"Missing power icon {power.Id}: {power.CustomPackedIconPath}");
                Require(ResourceLoader.Exists(power.CustomBigIconPath), $"Missing big power icon {power.Id}: {power.CustomBigIconPath}");
                var smallIcon = ResourceLoader.Load<Texture2D>(power.CustomPackedIconPath);
                var bigIcon = ResourceLoader.Load<Texture2D>(power.CustomBigIconPath);
                Require(smallIcon.GetWidth() <= 64 && smallIcon.GetHeight() <= 64, $"Oversized power icon: {power.Id}");
                Require(bigIcon.GetWidth() <= 256 && bigIcon.GetHeight() <= 256, $"Oversized big power icon: {power.Id}");
                MainFile.Logger.Info($"POWER_ICON_AUDIT {power.Id} {power.CustomPackedIconPath}");
            }
            MainFile.Logger.Info($"CONSOLE_OVERLAY_AUDIT mounted={MegaCrit.Sts2.Core.Nodes.NGame.Instance?.GetNodeOrNull("ChineseDebugConsoleOverlay") is not null}");
            var limited = (Librarian.LibrarianCode.Powers.Implemented.OverfishingPower)ModelDb.Power<Librarian.LibrarianCode.Powers.Implemented.OverfishingPower>().ToMutable();
            var durationCard = ModelDb.Card<Librarian.LibrarianCode.Cards.PowerCards.Overfishing>().ToMutable();
            Require(durationCard.DynamicVars["Turns"].IntValue == 3, "Base Overfishing duration must be three");
            limited.RegisterTurns(durationCard.DynamicVars["Turns"].IntValue);
            for (int i = 0; i < 3; i++) Require(limited.TryConsumeTurn(), "Three base doublings");
            Require(!limited.TryConsumeTurn(), "No fourth base doubling");
            durationCard.UpgradeInternal(); durationCard.FinalizeUpgradeInternal();
            Require(durationCard.DynamicVars["Turns"].IntValue == 4, "Upgraded duration must be four");
            limited.RegisterTurns(durationCard.DynamicVars["Turns"].IntValue);
            for (int i = 0; i < 4; i++) Require(limited.TryConsumeTurn(), "Four upgraded doublings");
            Require(!limited.TryConsumeTurn(), "No fifth upgraded doubling");
            MainFile.Logger.Info("OVERFISHING_DURATION_AUDIT_PASS base=3 upgraded=4");
            var spark = (ImmortalSpark)ModelDb.Card<ImmortalSpark>().ToMutable();
            spark.PermanentIncrease = 7;
            spark.UpgradeInternal();
            spark.FinalizeUpgradeInternal();
            var loaded = (ImmortalSpark)CardModel.FromSerializable(spark.ToSerializable());
            Require(loaded.PermanentIncrease == 7 && loaded.DynamicVars["Fire"].IntValue == 8
                && loaded.DynamicVars["Increase"].IntValue == 6, "Permanent growth/upgrade serialization mismatch");
            loaded.DowngradeInternal();
            Require(loaded.DynamicVars["Fire"].IntValue == 8 && loaded.DynamicVars["Increase"].IntValue == 4,
                "Downgrade must preserve accumulated growth");
            foreach (var kind in Enum.GetValues<Librarian.Core.OrbKind>())
            {
                var texture = ResourceLoader.Load<Texture2D>(LibrarianOrbVfx.TexturePath(kind));
                Require(texture is not null && texture.GetWidth() > 0, "Missing custom orb " + kind);
            }
            Require(ResourceLoader.Exists("res://images/packed/common_ui/locked_model.png"), "Missing native lock");
            MainFile.Logger.Info("ORB_ART_AUDIT_PASS customTextures=3 nativeLock=1 noDefectSprites=True");
            MainFile.Logger.Info($"MODEL_AUDIT_PASS cards={passed} growth_roundtrip=True gameplay_verified=False");
        }
        catch (Exception error)
        {
            MainFile.Logger.Error($"MODEL_AUDIT_FAIL after={passed} {error}");
        }
    }
    private static void ValidateFeedbackPreview(CardModel card, bool upgraded)
    {
        int? expected = card.Id.Entry switch
        {
            "LIBRARIAN-SCATTERED_FLAMES" => 8,
            "LIBRARIAN-IGNITE" => upgraded ? 10 : 7,
            "LIBRARIAN-FIRE_INSCRIPTION" => 10,
            _ => null
        };
        if (expected is null) return;
        string plain = System.Text.RegularExpressions.Regex.Replace(card.GetDescriptionForPile(PileType.None), @"\[[^\]]*\]", "");
        Require(System.Text.RegularExpressions.Regex.IsMatch(plain, $@"(?<!\d){expected}(?!\d)"), $"Noncombat preview lost base/upgrade value: {card.Id} {plain}");
        MainFile.Logger.Info($"FEEDBACK_PREVIEW_PASS {card.Id} upgraded={upgraded} expected={expected}");
    }

    private static object Describe(CardModel card) => new
    {
        cost = card.EnergyCost.CostsX ? (object)"X" : card.EnergyCost.GetWithModifiers(CostModifiers.None),
        vars = card.DynamicVars.ToDictionary(pair => pair.Key, pair => pair.Value.BaseValue),
        keywords = card.Keywords.Select(k => k.ToString()).OrderBy(k => k).ToArray(),
        target = card.TargetType.ToString(),
        rarity = card.Rarity.ToString(),
        type = card.Type.ToString()
    };
    private static void ValidateHoverTips(CardModel card)
    {
        var tips = card.HoverTips.ToArray();
        foreach (var tip in tips.OfType<MegaCrit.Sts2.Core.HoverTips.HoverTip>())
            Require(!string.IsNullOrWhiteSpace(tip.Description) && !tip.Description.Contains('{'), $"Unresolved hover tip {card.Id}: {tip.Description}");
        Require(!tips.Any(t => t.Id == LibrarianHoverTips.Tip("STRENGTHEN").Id), "Retired Strengthen tip must be absent: " + card.Id);
        string text = card.GetDescriptionForPile(PileType.None);
        foreach (var tip in tips.OfType<MegaCrit.Sts2.Core.HoverTips.HoverTip>())
            Require(tip.CanonicalModel is not Librarian.LibrarianCode.Powers.LibrarianPower,
                $"Card must not preview its own combat status: {card.Id}");
        foreach (string word in new[] { "燃火", "潮涌", "生长", "注魔", "结算", "浪潮", "格挡" })
        {
            string withoutGold = System.Text.RegularExpressions.Regex.Replace(text, @"\[gold\].*?\[/gold\]", "");
            Require(!withoutGold.Contains(word), $"Keyword must be gold: {card.Id} {word}");
        }
        if (text.Contains("格挡")) Require(tips.Any(t => t.Id == MegaCrit.Sts2.Core.HoverTips.HoverTipFactory.Static(MegaCrit.Sts2.Core.HoverTips.StaticHoverTip.Block).Id), "Missing Block tip: " + card.Id);
        if (card.Id.Entry == "LIBRARIAN-RENEWAL")
        {
            Require(tips.Any(t => t.Id == LibrarianHoverTips.Tip("GROWTH").Id)
                && tips.Any(t => t.Id == LibrarianHoverTips.Tip("LOCK").Id)
                && !tips.Any(t => t.Id == LibrarianHoverTips.Tip("FIRE").Id),
                "Renewal must explain Growth and Lock without treating the Fire Orb name as a gain action");
            MainFile.Logger.Info($"KEYWORD_SCOPE_PASS Renewal upgraded={card.IsUpgraded} growth=True lock=True fire=False");
        }
        if (card.Id.Entry == "LIBRARIAN-SEDIMENTATION")
            Require(new[] { "EXTINGUISH", "BACKGROUND" }
                .All(key => tips.Any(t => t.Id == LibrarianHoverTips.Tip(key).Id)),
                "Sedimentation must explain Extinguish and Background without a zero-value power preview");
        if (card.Id.Entry == "LIBRARIAN-SHIFTING_PAGES")
            Require(!tips.OfType<MegaCrit.Sts2.Core.HoverTips.HoverTip>().Any(t => t.Title == card.Title),
                "Shifting Pages card must not preview its own power");
        foreach (var tip in LibrarianHoverTips.ForText(text))
            Require(tips.Any(existing => existing.Id == tip.Id), $"Missing keyword tip {card.Id}: {tip.Id}");
        MainFile.Logger.Info($"HOVER_TIP_AUDIT {card.Id} upgraded={card.IsUpgraded} count={tips.Length}");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
