using System.IO;
using Godot;
using Librarian.LibrarianCode;
using MegaCrit.Sts2.addons.mega_text;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace Librarian.Mechanics;

internal static class DevelopmentRevision039TextAudit
{
    private static void Require(bool pass, string text)
    {
        if (!pass) throw new InvalidOperationException("039 selection text: " + text);
        MainFile.Logger.Info("VISUAL_039_TEXT_CHECK_PASS " + text);
    }
    private static async Task Wait()
        => await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(0.65), SceneTreeTimer.SignalName.Timeout);
    private static IEnumerable<Node> Descendants(Node root)
    {
        foreach (var child in root.GetChildren())
        { yield return child; foreach (var item in Descendants(child)) yield return item; }
    }
    private static IEnumerable<FontFile> Files(Font font)
    {
        if (font is FontFile file) yield return file;
        if (font is FontVariation variation)
            foreach (var item in Files(variation.BaseFont)) yield return item;
        foreach (var fallback in font.Fallbacks)
            foreach (var item in Files(fallback)) yield return item;
    }
    private static async Task Capture(string name)
    {
        const string output = @"D:\Slay The Spire_Mod Dev\.research\revision-v039-screenshots";
        Directory.CreateDirectory(output);
        var game = NGame.Instance!;
        await game.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = game.GetViewport().GetTexture().GetImage();
        Require(image.SavePng(Path.Combine(output, name + ".png")) == Error.Ok, "screenshot " + name);
    }

    internal static async Task Run(NMainMenu menu)
    {
        if (!DevelopmentVisualAudit.Enabled) return;
        Require(OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase), "isolated profile");
        Require(DisplayServer.GetName() != "headless", "real rendering");
        Require(LocManager.Instance.Language == "zhs", "Chinese locale under test");
        Require(!menu.SubmenuStack.SubmenusOpen, "empty menu stack");
        var screen = menu.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();
        screen.InitializeSingleplayer(); menu.SubmenuStack.Push(screen);
        var window = screen.GetWindow(); var originalSize = window.Size;
        try
        {
            await Wait();
            var buttons = Descendants(screen).OfType<NCharacterSelectButton>().ToArray();
            var native = buttons.Single(b => b.Character is Ironclad);
            var librarian = buttons.Single(b => b.Character is LibrarianCharacter);
            var labels = LibrarianCharacterText.LabelPaths.Select(p => screen.GetNode<Control>(p)).ToArray();
            foreach (var resolution in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                window.Size = resolution; native.Select(); await Wait();
                var originalFonts = labels.SelectMany(label => LibrarianCharacterText.FontSlots(label)
                    .Select(slot => (Label: label, Slot: slot, Font: label.GetThemeFont(slot), HadOverride: label.HasThemeFontOverride(slot)))) .ToArray();
                var nativeFiles = originalFonts.SelectMany(v => Files(v.Font)).Distinct()
                    .Select(f => (File: f, Msdf: f.MultichannelSignedDistanceField, Sampling: f.Oversampling)).ToArray();
                var fontSizes = labels.Select(label => label.GetThemeFontSize(label is RichTextLabel ? "normal_font_size" : "font_size")).ToArray();
                await Capture($"039-native-text-{resolution.X}x{resolution.Y}");
                librarian.Select(); await Wait();
                var initialFonts = originalFonts.Select(v => v.Label.GetThemeFont(v.Slot)).ToArray();
                await Capture($"039-librarian-text-{resolution.X}x{resolution.Y}");
                for (int i = 0; i < labels.Length; i++)
                {
                    var label = labels[i];
                    Require(label.Scale.IsEqualApprox(Vector2.One), "unscaled native label " + label.GetPath());
                    int actualSize = label.GetThemeFontSize(label is RichTextLabel ? "normal_font_size" : "font_size");
                    MainFile.Logger.Info($"VISUAL_039_TEXT_SIZE label={label.GetPath()} nativeIroncladSize={fontSizes[i]} librarianSize={actualSize} box={label.Size}");
                    if (label is MegaRichTextLabel { AutoSizeEnabled: true } auto)
                    {
                        // Native relic descriptions fit different content into a bounded box (12..24).
                        // Ironclad's shorter text is not a valid same-size baseline for another relic.
                        Require(actualSize >= auto.MinFontSize && actualSize <= auto.MaxFontSize,
                            $"native automatic size range {auto.MinFontSize}..{auto.MaxFontSize} actual={actualSize}");
                        MainFile.Logger.Info($"VISUAL_039_TEXT_FIT label={label.GetPath()} contentHeight={auto.GetContentHeight()} boxHeight={auto.Size.Y} text={auto.GetParsedText()}");
                        Require(auto.GetContentHeight() <= auto.Size.Y + 1, "automatically sized content fits native box " + label.GetPath());
                    }
                    else Require(actualSize == fontSizes[i], "fixed native font size " + label.GetPath());
                }
                foreach (var entry in originalFonts)
                {
                    var font = entry.Label.GetThemeFont(entry.Slot);
                    Require(font != entry.Font && font is FontVariation variation && variation.SpacingGlyph == -1,
                        "private one-pixel tracking " + entry.Label.GetPath() + "/" + entry.Slot);
                    Require(font.GetFontName() == entry.Font.GetFontName(), "same native locale-selected font face");
                    Require(Files(font).Any() && Files(font).All(f => !f.MultichannelSignedDistanceField && f.Oversampling == 3), "raster atlas oversampling 3");
                    const string sample = "图书管理员魔法书卷";
                    var before = entry.Font.GetStringSize(sample, fontSize: 24);
                    var after = font.GetStringSize(sample, fontSize: 24);
                    Require(after.X < before.X && after.X > before.X * 0.9f, "modest CJK tracking reduction " + entry.Slot);
                    Require(Math.Abs(after.Y - before.Y) < 0.1f, "native line height preserved " + entry.Slot);
                    MainFile.Logger.Info($"VISUAL_039_TEXT_FONT resolution={resolution} label={entry.Label.GetPath()} slot={entry.Slot} source={entry.Font.ResourcePath} face={font.GetFontName()} nativeWidth={before.X} localWidth={after.X} localFiles={string.Join(',', Files(font).Select(f => f.GetFontName()))} globalScale={entry.Label.GetGlobalTransform().Scale}");
                }
                var description = screen.GetNode<RichTextLabel>(LibrarianCharacterText.LabelPaths[1]);
                var expected = new LocString("characters", librarian.Character.CharacterSelectDesc).GetFormattedText();
                Require(description.Text == expected, "user character description unchanged");
                Require(description.GetContentHeight() <= description.Size.Y + 1, "description fits native box");
                native.Select(); await Wait();
                foreach (var entry in originalFonts)
                    Require(entry.Label.GetThemeFont(entry.Slot) == entry.Font && entry.Label.HasThemeFontOverride(entry.Slot) == entry.HadOverride, "other character fully restores native font and override presence " + entry.Slot);
                foreach (var entry in nativeFiles)
                    Require(entry.File.MultichannelSignedDistanceField == entry.Msdf && entry.File.Oversampling == entry.Sampling, "shared native font not mutated");
                librarian.Select(); await Wait();
                for (int i = 0; i < originalFonts.Length; i++)
                    Require(originalFonts[i].Label.GetThemeFont(originalFonts[i].Slot) == initialFonts[i], "repeat selection reuses private font");
            }
            MainFile.Logger.Info("REVISION_039_TEXT_AUDIT_PASS labels=4 nativeLayout=true nativeRestore=true resolutions=2 screenshots=4");
        }
        finally
        {
            window.Size = originalSize;
            if (menu.SubmenuStack.Peek() == screen) menu.SubmenuStack.Pop();
            await Wait();
        }
    }
}

