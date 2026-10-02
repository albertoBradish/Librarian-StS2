using HarmonyLib;
using Librarian.Core;
using Librarian.LibrarianCode.Cards;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using System.Text.RegularExpressions;

namespace Librarian.Mechanics;

internal static class LibrarianHoverTips
{
    internal static HoverTip Tip(string key) => new(new LocString("static_hover_tips", $"LIBRARIAN_{key}.title"),
        new LocString("static_hover_tips", $"LIBRARIAN_{key}.description"));
    internal static string OrbKey(OrbKind kind) => "ORB_" + kind.ToString().ToUpperInvariant();
    internal static readonly (string Key, string[] Words)[] Terms =
    [
        ("FIRE", ["燃火"]),
        ("TIDE", ["潮涌"]),
        ("GROWTH", ["生长"]),
        ("STRENGTHEN", ["强化", "Strengthen"]),
        ("ACTIVATE", ["注魔", "activate", "Activate"]),
        ("SETTLE", ["结算", "settle", "Settle"]),
        ("WAVES", ["浪潮", "Waves"])
    ];
    internal static IEnumerable<IHoverTip> ForText(string text)
    {
        var highlighted = Regex.Matches(text, @"\[gold\](.*?)\[/gold\]").Select(m => m.Groups[1].Value).ToArray();
        bool Matches(string key) => LibrarianLanguage.Format("KEYWORD_" + key).Split('|')
            .Any(word => highlighted.Contains(word, StringComparer.OrdinalIgnoreCase));
        if (Matches("BLOCK")) yield return HoverTipFactory.Static(StaticHoverTip.Block);
        if (Matches("HAND")) yield return Tip("HAND");
        if (Matches("DRAW_PILE")) yield return Tip("DRAW_PILE");
        foreach (var (key, _) in Terms)
            if (Matches(key)) yield return Tip(key);
    }
}

// Append to the native getter so concrete cards keep their existing power/energy tips.
[HarmonyPatch(typeof(CardModel), nameof(CardModel.HoverTips), MethodType.Getter)]
internal static class LibrarianCardHoverTips
{
    [HarmonyPostfix] private static void Postfix(CardModel __instance, ref IEnumerable<IHoverTip> __result)
    {
        if (__instance is not LibrarianCard) return;
        string text = __instance.GetDescriptionForPile(PileType.None);
        var tips = __result.Concat(LibrarianHoverTips.ForText(text));
        if (__instance.DynamicVars.Any(pair => pair.Key == "Energy")) tips = tips.Append(HoverTipFactory.ForEnergy(__instance));
        __result = tips.GroupBy(t => t.Id).Select(group => group.First()).ToArray();
    }
}
