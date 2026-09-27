using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace Librarian.Mechanics;

/// <summary>Private CJK raster fonts for this character's information panel only.</summary>
[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.SelectCharacter))]
internal static class LibrarianCharacterText
{
    internal static readonly string[] LabelPaths =
    [
        "InfoPanel/VBoxContainer/Name",
        "InfoPanel/VBoxContainer/DescriptionLabel",
        "InfoPanel/VBoxContainer/Relic/Name/RichTextLabel",
        "InfoPanel/VBoxContainer/Relic/Description"
    ];
    private sealed class LabelFonts
    {
        internal readonly Dictionary<string, (Font Original, Font Raster, bool HadOverride)> Values = new();
    }
    private static readonly ConditionalWeakTable<Control, LabelFonts> Originals = new();
    internal static IEnumerable<string> FontSlots(Control label) => label is RichTextLabel
        ? new[] { "normal_font", "bold_font", "italics_font" }
        : new[] { "font" };

    [HarmonyPrefix]
    private static void Restore(NCharacterSelectScreen __instance)
    {
        foreach (string path in LabelPaths)
        {
            var label = __instance.GetNode<Control>(path);
            if (!Originals.TryGetValue(label, out var saved)) continue;
            foreach (var (slot, entry) in saved.Values)
            {
                // A native locale refresh takes precedence over our previous private override.
                if (label.GetThemeFont(slot) != entry.Raster) continue;
                if (entry.HadOverride) label.AddThemeFontOverride(slot, entry.Original);
                else label.RemoveThemeFontOverride(slot);
            }
        }
    }

    [HarmonyPostfix]
    private static void Apply(NCharacterSelectScreen __instance, CharacterModel characterModel)
    {
        if (characterModel is not LibrarianCharacter || LocManager.Instance.Language != "zhs") return;
        foreach (string path in LabelPaths)
        {
            var label = __instance.GetNode<Control>(path);
            var saved = Originals.GetOrCreateValue(label);
            foreach (string slot in FontSlots(label))
            {
                var original = label.GetThemeFont(slot);
                if (!saved.Values.TryGetValue(slot, out var entry) || entry.Original != original)
                {
                    // Preserve locale-selected Noto/Source Han faces, 0.95 transform and baseline.
                    // One pixel of tracking is removed, without scaling the glyphs or the control.
                    var raster = new FontVariation { BaseFont = CopyForRaster(original), SpacingGlyph = -1 };
                    entry = (original, raster, label.HasThemeFontOverride(slot));
                    saved.Values[slot] = entry;
                    MainFile.Logger.Info($"CHARACTER_TEXT_039_FONT label={path} slot={slot} source={original.ResourcePath} face={original.GetFontName()} spacing=-1 oversampling=3");
                }
                label.AddThemeFontOverride(slot, entry.Raster);
            }
        }
        MainFile.Logger.Info("CHARACTER_TEXT_039_APPLIED scope=LibrarianInfoPanel locale=zhs privateRaster=true tracking=-1");
    }

    private static Font CopyForRaster(Font source)
    {
        if (source is FontVariation variation)
        {
            var local = (FontVariation)variation.Duplicate();
            local.BaseFont = CopyForRaster(variation.BaseFont);
            local.Fallbacks = new Godot.Collections.Array<Font>(variation.Fallbacks.Select(CopyForRaster));
            return local;
        }
        if (source is not FontFile file) return source;
        var font = (FontFile)file.Duplicate();
        font.Fallbacks = new Godot.Collections.Array<Font>(file.Fallbacks.Select(CopyForRaster));
        font.MultichannelSignedDistanceField = false;
        font.Antialiasing = TextServer.FontAntialiasing.Gray;
        font.Hinting = TextServer.Hinting.None;
        font.SubpixelPositioning = TextServer.SubpixelPositioning.OneQuarter;
        font.KeepRoundingRemainders = true;
        font.Oversampling = 3;
        return font;
    }
}

