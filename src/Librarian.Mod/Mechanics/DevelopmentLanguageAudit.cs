using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;
using HarmonyLib;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Powers;
using Librarian.LibrarianCode.Relics;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Timeline;
using STS2RitsuLib.Settings;

namespace Librarian.Mechanics;

internal static partial class DevelopmentRuntimeAudit
{
    private static async Task RunLanguageAudit()
    {
        int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException("Language audit: " + label); checks++; MainFile.Logger.Info("LANGUAGE_CHECK_PASS " + label); }
        async Task Wait(double seconds = .25) => await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        string output = System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT") ?? @"D:\Slay The Spire_Mod Dev\outputs\revision-v1.0.0-stable\audit-history\localization-v1.0\screenshots";
        async Task Capture(string name)
        {
            Directory.CreateDirectory(output);
            await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
            Check(image.SavePng(Path.Combine(output, name + ".png")) == Error.Ok, "capture " + name);
        }
        Check(OS.GetUserDataDir().Contains("revision030-userdata"), "dedicated isolated profile");
        var menu = NGame.Instance!.MainMenu!;
        menu.GetNodeOrNull<LibrarianUpdateNotice051>("LibrarianUpdateNoticeScheduler")?.SetProcess(false);
        await Wait();
        if (System.Environment.GetEnvironmentVariable("LIBRARIAN_LANGUAGE_PHASE") == "restart")
        {
            Check(LibrarianLanguage.Selected == "eng", "manual English persists across process restart");
            Check(new LocString("cards", "LIBRARIAN-SPARK.title").GetFormattedText() == "Spark", "persisted language supplies model text");
            // Let the existing startup preload/fade finish; starting another preload
            // here unloads materials still owned by the in-flight native transition.
            await Wait(5);
            await Capture("restart-menu");
            MainFile.Logger.Info("LANGUAGE_RESTART_AUDIT_PASS");
            return;
        }
        Check(LibrarianLanguage.Selected == LibrarianLanguage.Detect(LocManager.Instance.Language), "first use detects actual game language");
        Check(File.Exists(LibrarianLanguage.FilePath), "first choice persisted");
        Check(ModSettingsRegistry.TryGetPage("Librarian", LibrarianSettings041.PageId, out var page), "RitsuLib page exists");
        var section = page!.Sections.Single(s => s.Id == "language");
        var entry = section.Entries.OfType<ChoiceModSettingsEntryDefinition<string>>().Single(e => e.Id == "language");
        Check(entry.Presentation == ModSettingsChoicePresentation.Dropdown, "native dropdown presentation");
        Check(entry.OptionsProvider!().Select(o => o.Value).Contains("zhs") && entry.OptionsProvider!().Select(o => o.Value).Contains("eng"), "both bundled packs available");
        string originalGameLanguage = LocManager.Instance.Language;
        string originalVanillaStrike = new LocString("cards", "STRIKE_IRONCLAD.title").GetRawText();
        LibrarianUnlocks040.ApplyChoice(SaveManager.Instance.Progress, true);
        foreach (string language in new[] { "zhs", "eng" })
        {
            entry.Binding.Write(language); entry.Binding.Save();
            Check(LibrarianLanguage.Selected == language, "RitsuLib binding selects " + language);
            Check(LocManager.Instance.Language == originalGameLanguage && new LocString("cards", "STRIKE_IRONCLAD.title").GetRawText() == originalVanillaStrike, "vanilla locale unchanged " + language);
            var opened = await ModSettingsNavigator.OpenByIdsAsync("Librarian", LibrarianSettings041.PageId, sectionId: "language", options: new ModSettingsOpenOptions { Highlight = false, Focus = true });
            Check(opened.Success, "native settings opens " + language);
            await Wait(.5); await Capture("settings-" + language);
            var submenu = NGame.Instance.FindChildren("*", "", true, false).OfType<RitsuModSettingsSubmenu>().Last(n => n.Visible);
            var dropdown = submenu.FindChildren("*", "", true, false).OfType<ModSettingsDropdownChoiceControl<string>>()
                .Single(d => d.GetChildren().OfType<Button>().Any(b => b.Text is "English" or "简体中文"));
            dropdown.GetChildren().OfType<Button>().First().EmitSignal(BaseButton.SignalName.Pressed);
            await Wait(); await Capture("dropdown-" + language);
            string alternateLabel = language == "zhs" ? "English" : "简体中文";
            var choiceButton = NGame.Instance.FindChildren("*", "", true, false).OfType<ModSettingsMiniButton>()
                .Single(b => b.IsVisibleInTree() && b.Text == alternateLabel);
            choiceButton.EmitSignal(BaseButton.SignalName.Pressed); await Wait(.5);
            Check(LibrarianLanguage.Selected == (language == "zhs" ? "eng" : "zhs"), "dropdown button switches language " + language);
            entry.Binding.Write(language); entry.Binding.Save();
            if (submenu.GetParent() is NSubmenuStack stack && ReferenceEquals(stack.Peek(), submenu)) stack.Pop();
            await Wait();
            var popup = LibrarianUpdateNotice051.Show(LibrarianUpdateNotice051.CurrentVersion!);
            Check(popup is not null, "version popup opens " + language);
            await Wait();
            string body = popup!.GetNode<NVerticalPopup>("VerticalPopup").GetNode<RichTextLabel>("Description").GetParsedText();
            Check(language == "zhs" ? body.Contains("aery_bradish@163.com") && body.Contains("1850562239") : body.Contains("lrq1850562239@gmail.com") && !body.Contains("QQ") && !body.Contains("163.com"), "language specific contact " + language);
            await Capture("notice-" + language); NModalContainer.Instance!.Clear(); await Wait();
            var models = ModelDb.CardPool<LibrarianCardPool>().AllCards.ToArray();
            Check(models.Length == 91, "active card count " + language);
            var epoch = new Librarian2Epoch();
            void CheckUnlock(int number, IEnumerable<AbstractModel> unlocked, string nativeMethod)
            {
                var expected = unlocked.Select(model => (string)AccessTools.Method(typeof(EpochModel), nativeMethod).Invoke(epoch, [model])!).ToArray();
                string actual = new LocString("epochs", LibrarianUnlocks040.Id(number) + ".unlockText").GetRawText();
                var items = (language == "zhs" ? actual[3..^1].Split('、') : actual[10..^1].Split(", ")).ToArray();
                Check(items.SequenceEqual(expected), "unlock rarity tags match native models " + language + number);
            }
            for (int group = 0; group < 3; group++)
                CheckUnlock(new[] { 2, 5, 7 }[group], LibrarianUnlocks040.CardGroups[group].Select(id => models.Single(m => m.Id.Entry == id)), "GetColoredCardName");
            for (int group = 0; group < 2; group++)
                CheckUnlock(new[] { 3, 6 }[group], LibrarianUnlocks040.RelicGroups[group].Select(id => ModelDb.AllRelics.Single(m => m.Id.Entry == id)), "GetColoredRelicName");
            CheckUnlock(4, ModelDb.PotionPool<LibrarianPotionPool>().AllPotions, "GetColoredPotionName");
            foreach (var canonical in models)
            {
                var card = canonical.ToMutable();
                foreach (bool upgraded in new[] { false, true })
                {
                    if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
                    string text = card.GetDescriptionForPile(PileType.None);
                    Check(!string.IsNullOrWhiteSpace(text) && !text.Contains('{') && !text.Contains('}'), $"card formatted {language} {card.Id} {upgraded}");
                    if (language == "eng") Check(!Regex.IsMatch(text + card.Title, "[一-龥]"), $"English card clean {card.Id} {upgraded}");
                    foreach (var tip in LibrarianHoverTips.ForText(text)) Check(card.HoverTips.Any(t => t.Id == tip.Id), "keyword hover " + card.Id + " " + tip.Id);
                }
            }
            foreach (string table in LibrarianLanguage.Tables)
            {
                using var file = Godot.FileAccess.Open($"res://Librarian/localization/{language}/{table}.json", Godot.FileAccess.ModeFlags.Read);
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(file.GetAsText())!;
                foreach (var pair in data)
                {
                    var text = new LocString(table, pair.Key);
                    foreach (string name in Regex.Matches(pair.Value, @"\{([A-Za-z_][A-Za-z0-9_]*)").Select(m => m.Groups[1].Value).Distinct())
                    {
                        object value = name switch { "IfUpgraded" => new IfUpgradedVar(UpgradeDisplay.Upgraded), "MissingCombatHistory" or "InCombat" => true, "Version" => "1.0.0", "Email" => "test@example.com", "QQ" => "1850562239", "Path" => "test/path", "Reason" => "test reason", "Progress" => "1/4", "Durations" => "2, 3", _ => pair.Value.Contains("{" + name + ":diff()}") ? new DynamicVar(name, 2m) : 2m };
                        text.AddObj(name, value);
                    }
                    string result = text.GetFormattedText();
                    Check(!result.Contains('{') && !result.Contains('}'), "all template formatted " + language + " " + pair.Key);
                }
            }
            foreach (int amount in new[] { 1, 2 })
                Check(LibrarianLanguage.Format("LOCK_TURNS", ("Turns", amount)) == (language == "zhs" ? $"锁定：剩余{amount}回合" : $"Locked for {amount} " + (amount == 1 ? "turn" : "turns")), "independent plural culture " + language + amount);
            var selectScreen = menu.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();
            selectScreen.InitializeSingleplayer(); menu.SubmenuStack.Push(selectScreen);
            await Wait(.5);
            selectScreen.FindChildren("*", "", true, false).OfType<NCharacterSelectButton>().Single(b => b.Character is LibrarianCharacter).Select();
            await Wait(1); await Capture("character-" + language);
            menu.SubmenuStack.Pop(); await Wait();
        }
        // A runtime game-locale change must not replace an explicitly chosen mod language.
        LocManager.Instance.SetLanguage(originalGameLanguage == "eng" ? "zhs" : "eng");
        Check(LibrarianLanguage.Selected == "eng" && new LocString("cards", "LIBRARIAN-SPARK.title").GetRawText() == "Spark", "game language changes do not overwrite saved selection");
        LocManager.Instance.SetLanguage(originalGameLanguage);
        string custom = Path.Combine(LibrarianLanguage.PackDirectory, "audit-vone");
        Check(!Directory.Exists(custom), "custom fixture does not overwrite existing translation");
        Directory.CreateDirectory(custom);
        try
        {
            File.WriteAllText(Path.Combine(custom, "pack.json"), "{\"Name\":\"Audit Translation\",\"Culture\":\"en-US\"}");
            File.WriteAllText(Path.Combine(custom, "cards.json"), "{\"LIBRARIAN-SPARK.title\":\"Audit Spark\"}");
            LibrarianLanguage.Reload();
            Check(entry.OptionsProvider!().Any(o => o.Value == "audit-vone"), "new pack discovered in existing dropdown");
            entry.Binding.Write("audit-vone"); entry.Binding.Save();
            Check(new LocString("cards", "LIBRARIAN-SPARK.title").GetRawText() == "Audit Spark", "custom translation overrides own key");
            Check(new LocString("cards", "LIBRARIAN-LIBRARIAN_STRIKE.title").GetRawText() == "Strike", "incomplete pack falls back to English");
            File.WriteAllText(Path.Combine(custom, "cards.json"), "{\"LIBRARIAN-SPARK.title\":\"Reloaded Spark\"}");
            LibrarianLanguage.Reload();
            Check(new LocString("cards", "LIBRARIAN-SPARK.title").GetRawText() == "Reloaded Spark", "edited pack reloads without restart");
            File.WriteAllText(Path.Combine(custom, "cards.json"), "{\"LIBRARIAN-SPARK.description\":\"{Fire:formatter_that_does_not_exist():value}\"}");
            LibrarianLanguage.Reload();
            var badFormatCard = ModelDb.CardPool<LibrarianCardPool>().AllCards.Single(c => c.Id.Entry == "LIBRARIAN-SPARK").ToMutable();
            Check(badFormatCard.GetDescriptionForPile(PileType.None).Contains("Fire") && !string.IsNullOrWhiteSpace(LibrarianLanguage.Status), "invalid external formatter recovers to bundled entry");
            File.WriteAllText(Path.Combine(custom, "cards.json"), "{\"LIBRARIAN-SPARK.description\":\"Lost variable\"}");
            LibrarianLanguage.Reload();
            Check(LibrarianLanguage.Selected == "eng" && !string.IsNullOrWhiteSpace(LibrarianLanguage.Status), "invalid parameter pack rejected with fallback and visible status");
        }
        finally
        {
            File.Delete(Path.Combine(custom, "cards.json")); File.Delete(Path.Combine(custom, "pack.json")); Directory.Delete(custom);
            LibrarianLanguage.Reload(); LibrarianLanguage.Select("eng");
        }
        LibrarianLanguage.ExportTemplates();
        Check(File.Exists(Path.Combine(LibrarianLanguage.TemplateDirectory, "eng", "cards.json")), "translation template exported");
        LibrarianUnlocks040.ApplyChoice(SaveManager.Instance.Progress, true);
        var run = await NGame.Instance.StartNewSingleplayerRun(ModelDb.Character<LibrarianCharacter>(), true, ActModel.GetDefaultList(), [], "LIBRARIAN_LANGUAGE", GameMode.Standard);
        _player = run.Players.Single(); await FreshFight();
        foreach (string language in new[] { "zhs", "eng" })
        {
            LibrarianLanguage.Select(language);
            foreach (var canonical in ModelDb.AllPowers.OfType<LibrarianPower>())
            {
                var power = canonical.ToMutable();
                await PowerCmd.Apply(Context, power, _player.Creature, 2, _player.Creature, null);
                var text = power.HoverTips.OfType<HoverTip>().First().Description;
                Check(!text.Contains('{') && !text.Contains('}') && (language != "eng" || !Regex.IsMatch(text, "[一-龥]")), "live power " + language + " " + canonical.Id);
                await PowerCmd.Remove(power);
            }
            foreach (var relic in ModelDb.AllRelics.OfType<LibrarianRelic>())
                Check(!relic.DynamicDescription.GetFormattedText().Contains('{'), "relic description " + language + " " + relic.Id);
            await Wait(3);
            await Capture("combat-" + language);
        }
        var layer = new CanvasLayer { Layer = 120 }; NGame.Instance.AddChild(layer);
        var panel = new Control(); layer.AddChild(panel);
        var background = new ColorRect { Color = new Color("20272e") }; panel.AddChild(background);
        var showcase = new List<NCard>();
        foreach (string id in new[] { "SPACETIME_TWIST", "EARTH_COLLAPSE", "TRANSCRIBE", "CROP_ROTATION" })
        {
            var prototype = ModelDb.CardPool<LibrarianCardPool>().AllCards.Single(c => c.Id.Entry == "LIBRARIAN-" + id);
            var card = _player.Creature.CombatState!.CreateCard(prototype, _player);
            var node = MegaCrit.Sts2.Core.Assets.PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>();
            panel.AddChild(node); node.Model = card; showcase.Add(node);
        }
        try
        {
            foreach (var resolution in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                DisplayServer.WindowSetSize(resolution); await Wait();
                var size = NGame.Instance.GetViewport().GetVisibleRect().Size; background.Size = size;
                for (int i = 0; i < showcase.Count; i++) { showcase[i].Scale = Vector2.One * Math.Min(size.X / 1480, size.Y / 570); showcase[i].Position = new(size.X * (i + .5f) / 4, size.Y / 2); }
                foreach (string language in new[] { "zhs", "eng" })
                {
                    LibrarianLanguage.Select(language);
                    foreach (bool upgraded in new[] { false, true })
                    {
                        foreach (var node in showcase)
                        {
                            if (upgraded && !node.Model.IsUpgraded) { node.Model.UpgradeInternal(); node.Model.FinalizeUpgradeInternal(); }
                            if (!upgraded && node.Model.IsUpgraded) node.Model.DowngradeInternal();
                            node.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
                            Check(!Regex.IsMatch(node.GetNode<Label>("%TypeLabel").Text, language == "eng" ? "[一-龥]" : "[A-Za-z]"), "card type follows mod language " + language);
                        }
                        await Wait(); await Capture($"cards-{resolution.X}-{language}-{(upgraded ? "upgraded" : "base")}");
                    }
                }
            }
        }
        finally { layer.QueueFree(); DisplayServer.WindowSetSize(new Vector2I(1280, 720)); await Wait(); }
        LibrarianLanguage.Select("eng");
        await SaveManager.Instance.SaveRun(null);
        var saved = SaveManager.Instance.LoadRunSave();
        Check(saved.Success && saved.SaveData is not null, "actual run save read");
        var restored = RunState.FromSerializable(saved.SaveData!);
        await NGame.Instance.ReturnToMainMenu();
        await RunManager.Instance.SetUpSavedSingleplayer(restored, saved.SaveData!);
        await NGame.Instance.LoadRun(restored, saved.SaveData!.PreFinishedRoom);
        await NGame.Instance.Transition.FadeIn();
        Check(RunManager.Instance.IsInProgress && LibrarianLanguage.Selected == "eng", "menu new run combat save reload retains English");
        await Capture("reload-eng");
        await NGame.Instance.ReturnToMainMenu(); await Wait(1);
        MainFile.Logger.Info($"LANGUAGE_RUNTIME_AUDIT_PASS checks={checks} cards=91 menu=True combat=True save=True reload=True");
    }
}
