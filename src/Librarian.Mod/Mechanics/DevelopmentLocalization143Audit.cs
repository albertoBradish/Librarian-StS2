using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Powers;
using Librarian.LibrarianCode.Relics;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Timeline;
using STS2RitsuLib.Settings;

namespace Librarian.Mechanics;

internal static partial class DevelopmentRuntimeAudit
{
    private static async Task Run143Locales()
    {
        string[] languages = ["zhs", "eng", "jpn", "kor", "spa", "fra"];
        var game = NGame.Instance ?? throw new InvalidOperationException("Game is unavailable");
        string originalGameLanguage = LocManager.Instance.Language;
        string vanillaStrike = new LocString("cards", "STRIKE_IRONCLAD.title").GetRawText();
        int checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("143 localization: " + name);
            checks++;
        }
        async Task Wait(double seconds = .2) => await game.ToSignal(game.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        string? captureDirectory = System.Environment.GetEnvironmentVariable("LIBRARIAN_143_CAPTURE_DIR");
        async Task Capture(string name)
        {
            if (string.IsNullOrEmpty(captureDirectory)) return;
            Directory.CreateDirectory(captureDirectory);
            await game.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = game.GetViewport().GetTexture().GetImage();
            Check(image.SavePng(Path.Combine(captureDirectory, name + ".png")) == Error.Ok, "capture " + name);
        }
        var choices = LibrarianLanguage.Options().Select(option => option.Value).ToArray();
        Check(languages.All(choices.Contains), "six bundled locale options");
        foreach (string language in languages)
            Check(LibrarianLanguage.Detect(language) == language, "game language detection " + language);
        Check(LibrarianLanguage.Detect("zht") == "zhs", "traditional Chinese uses simplified pack");
        if (System.Environment.GetEnvironmentVariable("LIBRARIAN_143_PHASE") == "restart")
        {
            Check(LibrarianLanguage.Selected == "fra", "French manual choice survives new process");
            Check(new LocString("cards", "LIBRARIAN-SPARK.title").GetRawText() != "Spark", "French text survives new process");
            MainFile.Logger.Info($"LOCALIZATION_143_RESTART_PASS checks={checks} selected={LibrarianLanguage.Selected}");
            game.Quit();
            return;
        }
        Check(ModSettingsRegistry.TryGetPage("Librarian", LibrarianSettings041.PageId, out var settings), "settings page registered");
        var entry = settings!.Sections.Single(section => section.Id == "language").Entries
            .OfType<ChoiceModSettingsEntryDefinition<string>>().Single(option => option.Id == "language");
        var section = settings.Sections.Single(section => section.Id == "language");
        Check(languages.All(language => entry.OptionsProvider!().Any(option => option.Value == language)), "all locales in native dropdown");
        string originalSavedGameLanguage = SaveManager.Instance.SettingsSave.Language ?? originalGameLanguage;
        string originalSelectedModLanguage = LibrarianLanguage.Selected;
        var languageLabels = new Dictionary<string, (string Section, string Choice)>
        {
            ["zhs"] = ("语言", "模组语言"), ["eng"] = ("Language", "Mod language"),
            ["jpn"] = ("言語", "MODの言語"), ["kor"] = ("언어", "모드 언어"),
            ["spa"] = ("Idioma", "Idioma del mod"), ["fra"] = ("Langue", "Langue du mod")
        };
        string[] instructions = ["简体中文 — 在此选择", "English — Choose", "日本語 — ここで",
            "한국어 — 여기에서", "Español — Elige", "Français — Choisissez"];
        try
        {
            foreach (string language in languages.Where(language => language != originalGameLanguage)
                         .Concat(languages.Where(language => language == originalGameLanguage)))
            {
                SaveManager.Instance.SettingsSave.Language = language;
                LocManager.Instance.SetLanguage(language);
                Check(LibrarianLanguage.Selected == language, "game setting synchronizes mod language " + language);
                Check(JsonSerializer.Deserialize<LibrarianLanguage.Preference>(File.ReadAllText(LibrarianLanguage.FilePath))!.Language == language,
                    "game locale switch saves mod language " + language);
                Check(section.Title!.Resolve() == languageLabels[language].Section, "matching locales show one section title " + language);
                Check(entry.Label.Resolve() == languageLabels[language].Choice, "matching locales show one choice label " + language);
                var lines = entry.Description!.Resolve().Split('\n');
                Check(lines.Length == instructions.Length
                    && instructions.All(prefix => lines.Count(line => line.StartsWith(prefix, StringComparison.Ordinal)) == 1),
                    "six complete language instructions " + language);
                if (language != "eng")
                {
                    entry.Binding.Write("eng");
                    entry.Binding.Save();
                    Check(section.Title.Resolve() == $"Language / {languageLabels[language].Section}", "both section languages " + language);
                    Check(entry.Label.Resolve() == $"Mod language / {languageLabels[language].Choice}", "both choice languages " + language);
                    Check(entry.Description.Resolve().StartsWith("English — Choose", StringComparison.Ordinal), "selected locale comes first " + language);
                    var opened = await ModSettingsNavigator.OpenByIdsAsync("Librarian", LibrarianSettings041.PageId,
                        sectionId: "language", options: new ModSettingsOpenOptions { Highlight = false, Focus = true });
                    Check(opened.Success, "language setting opens with mismatched mod locale " + language);
                    await Wait(.3);
                    var submenu = game.FindChildren("*", "", true, false).OfType<RitsuModSettingsSubmenu>().Last(node => node.Visible);
                    var visibleText = submenu.FindChildren("*", "", true, false).Where(node => node is Control control && control.IsVisibleInTree())
                        .Select(node => node switch
                        {
                            Label label => label.Text,
                            RichTextLabel rich => rich.GetParsedText(),
                            Button button => button.Text,
                            _ => ""
                        }).ToArray();
                    Check(visibleText.Contains(entry.Label.Resolve()), "both labels render in native settings " + language);
                    Check(visibleText.Any(text => instructions.All(prefix => text.Contains(prefix, StringComparison.Ordinal))),
                        "six instructions render together in native settings " + language);
                    if (language == "zhs") await Capture("language-entry-zhs-game-eng-mod");
                    if (submenu.GetParent() is NSubmenuStack stack && ReferenceEquals(stack.Peek(), submenu)) stack.Pop();
                    await Wait();
                    LocManager.Instance.SetLanguage(language);
                    Check(LibrarianLanguage.Selected == "eng", "same game locale preserves manual mod choice " + language);
                    LibrarianLanguage.Select(language);
                }
                if (language == "kor")
                {
                    LocManager.Instance.SetLanguage("eng");
                    Check(LibrarianLanguage.Selected == "kor", "temporary native language override preserves mod choice");
                    Check(entry.Label.Resolve() == languageLabels["kor"].Choice, "temporary override preserves the language label");
                    LocManager.Instance.SetLanguage("kor");
                }
            }
            MainFile.Logger.Info("LOCALIZATION_FIX1_SYNC_PASS locales=6 persisted=True native_override_safe=True labels=True");
        }
        finally
        {
            SaveManager.Instance.SettingsSave.Language = originalSavedGameLanguage;
            LocManager.Instance.SetLanguage(originalGameLanguage);
            LibrarianLanguage.Select(originalSelectedModLanguage);
        }
        if (System.Environment.GetEnvironmentVariable("LIBRARIAN_LANGUAGE_ENTRY_ONLY") == "1")
        {
            entry.Binding.Write("fra");
            entry.Binding.Save();
            MainFile.Logger.Info($"LANGUAGE_ENTRY_AUDIT_PASS checks={checks} locales=6");
            game.Quit();
            return;
        }
        var cards = ModelDb.CardPool<LibrarianCardPool>().AllCards.ToArray();
        Check(cards.Length == 91, "current active card count");
        LibrarianLanguage.Select("eng");
        var expectedCardTips = new Dictionary<string, string[]>();
        foreach (var canonical in cards)
        {
            var card = canonical.ToMutable();
            foreach (bool upgraded in new[] { false, true })
            {
                if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
                expectedCardTips[card.Id + "/" + upgraded] = LibrarianHoverTips.ForText(card.GetDescriptionForPile(PileType.None))
                    .Select(tip => tip.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            }
        }
        foreach (string language in languages)
        {
            entry.Binding.Write(language);
            entry.Binding.Save();
            Check(LibrarianLanguage.Selected == language, "settings binding selects " + language);
            Check(LocManager.Instance.Language == originalGameLanguage &&
                new LocString("cards", "STRIKE_IRONCLAD.title").GetRawText() == vanillaStrike,
                "native game language remains unchanged " + language);
            foreach (string table in LibrarianLanguage.Tables)
            {
                using var file = Godot.FileAccess.Open($"res://Librarian/localization/{language}/{table}.json", Godot.FileAccess.ModeFlags.Read);
                Check(file is not null, "packaged table " + language + "/" + table);
                var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(file!.GetAsText())!;
                foreach (var pair in entries)
                {
                    var text = new LocString(table, pair.Key);
                    foreach (string name in Regex.Matches(pair.Value, @"\{([A-Za-z_][A-Za-z0-9_]*)")
                                 .Select(match => match.Groups[1].Value).Distinct())
                    {
                        object value = name switch
                        {
                            "IfUpgraded" => new IfUpgradedVar(UpgradeDisplay.Upgraded),
                            "MissingCombatHistory" or "InCombat" => true,
                            "Version" => "1.4.3", "Email" => "test@example.com", "QQ" => "1850562239",
                            "Path" => "test/path", "Reason" => "test reason", "Progress" => "1/4",
                            "Durations" => "2, 3",
                            _ => pair.Value.Contains("{" + name + ":diff()") || pair.Value.Contains("{" + name + ":inverseDiff()")
                                ? new DynamicVar(name, 2m) : 2m
                        };
                        text.AddObj(name, value);
                    }
                    string formatted = text.GetFormattedText();
                    Check(!formatted.Contains('{') && !formatted.Contains('}') && (pair.Value.Length == 0 || !string.IsNullOrEmpty(formatted)),
                        "format " + language + "/" + table + "/" + pair.Key);
                }
            }
            foreach (var canonical in cards)
            {
                var card = canonical.ToMutable();
                foreach (bool upgraded in new[] { false, true })
                {
                    if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
                    string description = card.GetDescriptionForPile(PileType.None);
                    Check(!string.IsNullOrWhiteSpace(description) && !description.Contains('{') && !description.Contains('}'),
                        "card " + language + "/" + card.Id + "/" + upgraded);
                    var localizedTips = LibrarianHoverTips.ForText(description).Select(tip => tip.Id)
                        .OrderBy(id => id, StringComparer.Ordinal).ToArray();
                    if (language is "jpn" or "kor" or "spa" or "fra")
                        Check(localizedTips.SequenceEqual(expectedCardTips[card.Id + "/" + upgraded]),
                            "semantic card hover " + language + "/" + card.Id + "/" + upgraded);
                    foreach (var tip in LibrarianHoverTips.ForText(description))
                        Check(card.HoverTips.Any(actual => actual.Id == tip.Id), "card hover " + language + "/" + card.Id + "/" + tip.Id);
                }
            }
            foreach (var canonical in ModelDb.AllPowers.OfType<LibrarianPower>())
            {
                string description = canonical.GetDumbHoverTip().Description;
                Check(!description.Contains('{') && !description.Contains('}'), "power " + language + "/" + canonical.Id);
            }
            foreach (var relic in ModelDb.AllRelics.OfType<LibrarianRelic>())
                Check(!relic.DynamicDescription.GetFormattedText().Contains('{'), "relic " + language + "/" + relic.Id);
            MainFile.Logger.Info($"LOCALIZATION_143_LOCALE_PASS language={language} cards={cards.Length} tables={LibrarianLanguage.Tables.Length}");
        }
        LibrarianUnlocks040.ApplyChoice(SaveManager.Instance.Progress, true);
        if (!string.IsNullOrEmpty(captureDirectory))
        {
            var menu = game.MainMenu!;
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            foreach (string language in languages.Where(language => language is not ("eng" or "zhs")))
            {
                LibrarianLanguage.Select(language);
                var opened = await ModSettingsNavigator.OpenByIdsAsync("Librarian", LibrarianSettings041.PageId,
                    sectionId: "language", options: new ModSettingsOpenOptions { Highlight = false, Focus = true });
                Check(opened.Success, "settings opens " + language);
                await Wait(.5);
                await Capture("settings-" + language);
                var submenu = game.FindChildren("*", "", true, false).OfType<RitsuModSettingsSubmenu>().Last(node => node.Visible);
                if (submenu.GetParent() is NSubmenuStack stack && ReferenceEquals(stack.Peek(), submenu)) stack.Pop();
                await Wait();
                var screen = menu.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();
                screen.InitializeSingleplayer();
                menu.SubmenuStack.Push(screen);
                await Wait(.5);
                screen.FindChildren("*", "", true, false).OfType<NCharacterSelectButton>()
                    .Single(button => button.Character is LibrarianCharacter).Select();
                await Wait(.5);
                await Capture("selection-" + language);
                menu.SubmenuStack.Pop();
                await Wait();
            }
            LibrarianLanguage.Select("eng");
            foreach (var resolution in new[] { new Vector2I(1920, 1080), new Vector2I(1440, 900) })
            {
                DisplayServer.WindowSetSize(resolution);
                var screen = menu.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();
                screen.InitializeSingleplayer();
                menu.SubmenuStack.Push(screen);
                await Wait(.5);
                screen.FindChildren("*", "", true, false).OfType<NCharacterSelectButton>()
                    .Single(button => button.Character is LibrarianCharacter).Select();
                await Wait(.5);
                await Capture($"selection-eng-{resolution.X}x{resolution.Y}");
                menu.SubmenuStack.Pop();
                await Wait();
            }
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        }
        var run = await game.StartNewSingleplayerRun(ModelDb.Character<LibrarianCharacter>(), true,
            ActModel.GetDefaultList(), [], "LIBRARIAN_143_LOCALES", GameMode.Standard);
        _player = run.Players.Single();
        await FreshFight();
        Check(_player.Creature.CombatState is not null, "new run enters combat");
        foreach (string language in languages)
        {
            LibrarianLanguage.Select(language);
            foreach (var canonical in ModelDb.AllPowers.OfType<LibrarianPower>())
                Check(!canonical.GetDumbHoverTip().Description.Contains('{'), "combat power " + language + "/" + canonical.Id);
        }
        if (!string.IsNullOrEmpty(captureDirectory))
        {
            var layer = new CanvasLayer { Layer = 120 };
            game.AddChild(layer);
            var panel = new Control();
            layer.AddChild(panel);
            var background = new ColorRect { Color = new Color("20272e"), Size = game.GetViewport().GetVisibleRect().Size };
            panel.AddChild(background);
            try
            {
                foreach (string language in languages.Where(language => language is not ("eng" or "zhs")))
                {
                    LibrarianLanguage.Select(language);
                    var longestDescription = cards.MaxBy(card => card.ToMutable().GetDescriptionForPile(PileType.None).Length)!;
                    var longestTitle = cards.Where(card => card.Id.Entry != longestDescription.Id.Entry)
                        .MaxBy(card => card.ToMutable().Title.Length)!;
                    var selected = new[] { longestDescription, longestTitle };
                    var nodes = new List<NCard>();
                    foreach (var prototype in selected)
                    {
                        var card = _player.Creature.CombatState!.CreateCard(prototype, _player);
                        var node = MegaCrit.Sts2.Core.Assets.PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>();
                        panel.AddChild(node);
                        node.Model = card;
                        nodes.Add(node);
                    }
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        nodes[i].Scale = Vector2.One * .95f;
                        nodes[i].Position = new Vector2(440 + i * 400, 360);
                        nodes[i].UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
                    }
                    await Wait(.5);
                    await Capture("long-cards-" + language);
                    foreach (var node in nodes) node.QueueFree();
                    await Wait();
                }
            }
            finally { layer.QueueFree(); }
        }
        LibrarianLanguage.Select("fra");
        await SaveManager.Instance.SaveRun(null);
        var saved = SaveManager.Instance.LoadRunSave();
        Check(saved.Success && saved.SaveData is not null, "save file readable");
        var restored = RunState.FromSerializable(saved.SaveData!);
        await game.ReturnToMainMenu();
        await RunManager.Instance.SetUpSavedSingleplayer(restored, saved.SaveData!);
        await game.LoadRun(restored, saved.SaveData!.PreFinishedRoom);
        await game.Transition.FadeIn();
        Check(RunManager.Instance.IsInProgress && LibrarianLanguage.Selected == "fra", "save reload retains language");
        MainFile.Logger.Info($"LOCALIZATION_143_AUDIT_PASS checks={checks} locales=6 cards=91 menu=True combat=True save=True reload=True");
        await game.ReturnToMainMenu();
        await Wait(1);
        game.Quit();
    }
}
