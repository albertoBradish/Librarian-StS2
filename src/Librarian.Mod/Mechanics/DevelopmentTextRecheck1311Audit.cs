using System.IO;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Entities.Cards;
using Librarian.LibrarianCode.Powers;
using Librarian.LibrarianCode.Powers.Implemented;
using Librarian.LibrarianCode.Relics;
using Librarian.LibrarianCode.Potions;

namespace Librarian.Mechanics;

/// <summary>Opt-in formatting/tooltip checks only; no screenshots or gameplay rule changes.</summary>
internal static class DevelopmentTextRecheck1311Audit
{
    internal static void Run(Player player, string output)
    {
        string selected = LibrarianLanguage.Selected;
        var rows = new List<object>();
        var contexts = new List<object>();
        int checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("1311 text recheck: " + label);
            checks++;
        }
        string Text(HoverTip tip, string label)
        {
            Check(!tip.Description.Contains('{') && !tip.Description.Contains('}'), label);
            return tip.Description;
        }
        try
        {
            foreach (string language in new[] { "zhs", "eng" })
            {
                LibrarianLanguage.Select(language);
                foreach (var canonical in ModelDb.CardPool<LibrarianCardPool>().AllCards.Append(ModelDb.Card<StudyNotes>()))
                foreach (bool upgraded in new[] { false, true })
                {
                    var card = canonical.ToMutable();
                    card.Owner = player;
                    if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
                    foreach (var pile in new[] { PileType.Hand, PileType.Play })
                    {
                        string text = card.GetDescriptionForPile(pile);
                        Check(!text.Contains('{') && !text.Contains('}'), "card context " + card.Id + " " + pile);
                        if (card.Id.Entry == "LIBRARIAN-ZERO_SEARCH")
                            Check(text.Contains(language == "zhs" ? "共抽" : "in total"), "Zero Search combat preview branch");
                        contexts.Add(new { language, id = card.Id.Entry, upgraded, pile = pile.ToString(), text });
                    }
                }
                foreach (var canonical in ModelDb.AllPowers.OfType<LibrarianPower>())
                {
                    string generic = Text(canonical.GetDumbHoverTip(), "generic " + canonical.Id);
                    var instance = canonical.ToMutable();
                    instance.ApplyInternal(player.Creature, 3, silent: true);
                    try
                    {
                        if (instance is OverfishingPower notes) { notes.NormalNotes = 1; notes.UpgradedNotes = 2; }
                        var tips = instance.HoverTips.OfType<HoverTip>().ToArray();
                        foreach (var tip in tips) Text(tip, "instance " + canonical.Id);
                        rows.Add(new { language, kind = "power", id = canonical.Id.Entry, generic,
                            instance = tips.Select(t => t.Description).ToArray() });
                    }
                    finally { instance.RemoveInternal(); }
                }
                foreach (var relic in ModelDb.AllRelics.OfType<LibrarianRelic>())
                {
                    var tip = relic.HoverTip;
                    string text = Text(tip, "relic " + relic.Id);
                    foreach (var extra in relic.HoverTips.OfType<HoverTip>()) Text(extra, "relic tip " + relic.Id);
                    rows.Add(new { language, kind = "relic", id = relic.Id.Entry, text });
                }
                foreach (var potion in ModelDb.AllPotions.OfType<LibrarianPotion>())
                {
                    string text = Text(potion.HoverTip, "potion " + potion.Id);
                    var tips = potion.HoverTips.ToArray();
                    foreach (var tip in tips.OfType<HoverTip>()) Text(tip, "potion tip " + potion.Id);
                    if (potion is FluidForbiddenFruit)
                        Check(tips.Any(t => t.Id == LibrarianHoverTips.Tip("EXTRA_SETTLE").Id), "fruit extra settlement tooltip");
                    rows.Add(new { language, kind = "potion", id = potion.Id.Entry, text,
                        tips = tips.Select(t => t.Id).ToArray() });
                }
            }
        }
        finally { LibrarianLanguage.Select(selected); }
        File.WriteAllText(Path.Combine(output, "formatted-other-models.json"), JsonSerializer.Serialize(rows));
        File.WriteAllText(Path.Combine(output, "formatted-contexts.json"), JsonSerializer.Serialize(contexts));
        MainFile.Logger.Info($"TEXT_RECHECK1311_PASS checks={checks} modelDescriptions={rows.Count} cardContexts={contexts.Count} screenshots=0");
    }
}
