using Godot;
using HarmonyLib;
using Librarian.Core;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Powers;
using Librarian.LibrarianCode.Potions;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using System.Text.RegularExpressions;

namespace Librarian.Mechanics;

internal static class LibrarianHoverTips
{
    internal static HoverTip Tip(string key) => new(new LocString("static_hover_tips", $"LIBRARIAN_{key}.title"),
        new LocString("static_hover_tips", $"LIBRARIAN_{key}.description"), TipIcon(key));
    internal static string OrbKey(OrbKind kind) => "ORB_" + kind.ToString().ToUpperInvariant();
    internal static Texture2D OrbIcon(OrbKind kind) => GD.Load<Texture2D>(LibrarianOrbVfx.TexturePath(kind));
    internal static HoverTip OrbTip(OrbKind kind, string description) => new(
        new LocString("static_hover_tips", $"LIBRARIAN_{OrbKey(kind)}.title"), description, OrbIcon(kind));
    private static Texture2D? TipIcon(string key) => key switch
    {
        "FIRE" or "ORB_FIRE" => OrbIcon(OrbKind.Fire),
        "TIDE" or "ORB_TIDE" => OrbIcon(OrbKind.Tide),
        "GROWTH" or "ORB_GROWTH" => OrbIcon(OrbKind.Growth),
        _ => null
    };
    internal static readonly (string Key, string[] Words)[] Terms =
    [
        ("FIRE", ["燃火"]),
        ("TIDE", ["潮涌"]),
        ("GROWTH", ["生长"]),
        ("ACTIVATE", ["注魔", "activate", "Activate"]),
        ("SETTLE", ["结算", "settle", "Settle"]),
        ("WAVES", ["浪潮", "Waves"]),
        ("LOCK", ["锁定"]),
        ("EXTINGUISH", ["熄灭"]),
        ("FOREGROUND", ["前台"]),
        ("BACKGROUND", ["后台"]),
        ("EXTRA_SETTLE", ["额外结算"])
    ];
    internal static IEnumerable<IHoverTip> ForText(string text)
        => Expand(DirectForText(text));

    // Only these mod concepts may be omitted when they are reached through another
    // tip. Direct card/model requirements and every native tip always remain.
    internal static readonly HashSet<string> CompactNestedKeys = new(StringComparer.Ordinal)
    {
        "SETTLE", "FOREGROUND", "BACKGROUND", "ACTIVATE", "EXTINGUISH"
    };
    private static string ExtraWordPattern => LibrarianLanguage.Selected switch
    {
        "zhs" => "额外",
        "fra" => @"supplémentaire|additionnel|de nouveau|une fois de plus",
        "spa" => @"extra|adicional|una vez más|de nuevo|\d+\s+vez(?:es)?\s+más",
        "jpn" => "追加|もう1回|もう一度",
        "kor" => "추가|한 번 더|한번 더",
        _ => @"\bextra\b|\badditional\b"
    };

    private static string WithoutOrbNounPhrases(string text)
    {
        string[] names = ["FIRE", "TIDE", "GROWTH"];
        string titles = string.Join("|", names.Select(key => Regex.Escape(
            LibrarianLanguage.Text("static_hover_tips", $"LIBRARIAN_ORB_{key}.title"))));
        string? coordinated = LibrarianLanguage.Selected switch
        {
            "fra" => $@"\bOrbes?\s+de\s+\[gold\](?:{titles})\[/gold\](?:\s*(?:et|,)\s*(?:de\s+)?\[gold\](?:{titles})\[/gold\])+",
            "spa" => $@"\bOrbes?\s+de\s+\[gold\](?:{titles})\[/gold\](?:\s*(?:y|,)\s*(?:de\s+)?\[gold\](?:{titles})\[/gold\])+",
            "jpn" => $@"\[gold\](?:{titles})\[/gold\](?:\s*(?:と|、|及び)\s*\[gold\](?:{titles})\[/gold\])+\s*(?:の)?\s*オーブ",
            "kor" => $@"\[gold\](?:{titles})\[/gold\](?:\s*(?:과|와|,)\s*\[gold\](?:{titles})\[/gold\])+\s*구체",
            _ => null
        };
        if (coordinated is not null) text = Regex.Replace(text, coordinated, "", RegexOptions.IgnoreCase);
        string? pattern = LibrarianLanguage.Selected switch
        {
            "eng" => $@"(?:\[gold\](?:{titles})\[/gold\](?:\s*(?:,\s*(?:and|or)?|and|or|/)\s*)?)+\s+Orbs?\b",
            "fra" or "spa" => $@"\bOrbes?\s+(?:de\s+|de\s+la\s+)?\[gold\](?:{titles})\[/gold\]",
            "jpn" => $@"\[gold\](?:{titles})\[/gold\]\s*(?:の)?\s*オーブ",
            "kor" => $@"\[gold\](?:{titles})\[/gold\]\s*구체",
            _ => null
        };
        return pattern is null ? text : Regex.Replace(text, pattern, "", RegexOptions.IgnoreCase);
    }

    // Gold marks emphasis, not a tooltip contract. Orb names in noun phrases
    // share words with gain actions; exclude those noun phrases first.
    private static IEnumerable<IHoverTip> DirectForText(string text)
    {
        text = WithoutOrbNounPhrases(text);
        var highlighted = Regex.Matches(text, @"\[gold\](.*?)\[/gold\]").ToArray();
        string[] settleAliases = LibrarianLanguage.Format("KEYWORD_SETTLE").Split('|');
        var extraSettlements = highlighted.Where(mark => settleAliases.Any(alias =>
                string.Equals(mark.Groups[1].Value, alias, StringComparison.OrdinalIgnoreCase)))
            .Where(mark =>
            {
                int start = text.LastIndexOfAny(['.', '!', '?', '。', '！', '？', ';', '\n'], Math.Max(0, mark.Index - 1)) + 1;
                int end = text.IndexOfAny(['.', '!', '?', '。', '！', '？', ';', '\n'], mark.Index + mark.Length);
                if (end < 0) end = text.Length;
                return Regex.IsMatch(text[start..end], ExtraWordPattern, RegexOptions.IgnoreCase);
            }).Select(mark => mark.Index).ToHashSet();
        bool Matches(string key) => LibrarianLanguage.Format("KEYWORD_" + key).Split('|')
            .Any(word => highlighted.Any(mark => string.Equals(mark.Groups[1].Value, word, StringComparison.OrdinalIgnoreCase)
                // An extra settlement is its own concept. Its highlighted verb is
                // not a second direct requirement for an ordinary settlement tip.
                && (key != "SETTLE" || !extraSettlements.Contains(mark.Index))));
        if (Matches("BLOCK")) yield return LibrarianLanguage.NativeTip(() => HoverTipFactory.Static(StaticHoverTip.Block));
        foreach (var (key, _) in Terms)
            if (Matches(key)) yield return Tip(key);
        if (extraSettlements.Count > 0)
            yield return Tip("EXTRA_SETTLE");
    }

    internal static IEnumerable<IHoverTip> Expand(IEnumerable<IHoverTip> roots)
    {
        // Preserve native instanced tips, empty IDs and smart-tip preference.
        // The visited set guards only newly followed static references.
        var expanded = IHoverTip.RemoveDupes(roots).ToList();
        var pending = new Queue<IHoverTip>(expanded);
        var visited = expanded.Where(t => !string.IsNullOrEmpty(t.Id)).Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var compactIds = LibrarianPreferences050.Current.CompactHoverTips
            ? CompactNestedKeys.Select(key => Tip(key).Id).ToHashSet(StringComparer.Ordinal) : null;
        while (pending.TryDequeue(out var tip))
        {
            if (tip is HoverTip described)
                foreach (var referenced in DirectForText(described.Description))
                    if (visited.Add(referenced.Id))
                    {
                        if (compactIds?.Contains(referenced.Id) != true) expanded.Add(referenced);
                        // Follow hidden nodes as well: their native and core mod
                        // descendants are still part of the explanation contract.
                        pending.Enqueue(referenced);
                    }
        }
        return IHoverTip.RemoveDupes(expanded);
    }
}

// Append to the native getter so concrete cards keep their existing power/energy tips.
[HarmonyPatch(typeof(CardModel), nameof(CardModel.HoverTips), MethodType.Getter)]
internal static class LibrarianCardHoverTips
{
    [HarmonyPostfix] private static void Postfix(CardModel __instance, ref IEnumerable<IHoverTip> __result)
    {
        if (__instance is not ILibrarianCard) return;
        string text = __instance.GetDescriptionForPile(PileType.None);
        var tips = __result.Concat(LibrarianHoverTips.ForText(text));
        if (__instance.DynamicVars.Any(pair => pair.Key == "Energy")) tips = tips.Append(HoverTipFactory.ForEnergy(__instance));
        __result = LibrarianHoverTips.Expand(tips).ToArray();
    }
}

[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.HoverTips), MethodType.Getter)]
internal static class LibrarianPowerHoverTips
{
    [HarmonyPostfix] private static void Postfix(PowerModel __instance, ref IEnumerable<IHoverTip> __result)
    {
        if (__instance is LibrarianPower) __result = LibrarianHoverTips.Expand(__result).ToArray();
    }
}

[HarmonyPatch(typeof(PotionModel), nameof(PotionModel.HoverTips), MethodType.Getter)]
internal static class LibrarianPotionHoverTips
{
    [HarmonyPostfix] private static void Postfix(PotionModel __instance, ref IEnumerable<IHoverTip> __result)
    {
        if (__instance is LibrarianPotion) __result = LibrarianHoverTips.Expand(__result).ToArray();
    }
}
