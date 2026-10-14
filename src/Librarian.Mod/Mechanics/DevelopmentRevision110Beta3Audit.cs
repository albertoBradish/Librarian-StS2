using System.Text.RegularExpressions;
using Godot;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.HoverTips;

namespace Librarian.Mechanics;

internal static class DevelopmentRevision110Beta3Audit
{
    internal static async Task Run(Player player)
    {
        int checks = 0;
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("110 beta3: " + label);
            checks++; MainFile.Logger.Info("V110_BETA3_CHECK_PASS " + label);
        }
        string[] affected = ["TRANSCRIBE", "NOURISH", "BURN_THE_RIVER", "CALIBRATE", "REKINDLE", "DEEP_SEA_BARRIER", "FUEL_THE_FIRE", "READ_BACKWARD", "TO_BE_CONTINUED"];
        string[] handCards = ["TRANSCRIBE", "NOURISH", "CALIBRATE", "DEEP_SEA_BARRIER", "TO_BE_CONTINUED"];
        var models = ModelDb.CardPool<LibrarianCardPool>().AllCards.ToArray();
        Check(models.Length == 91, "91 active cards");
        var selected = affected.Select(id => models.Single(c => c.Id.Entry == "LIBRARIAN-" + id)).ToArray();
        string original = LibrarianLanguage.Selected;
        try
        {
            foreach (string lang in new[] { "zhs", "eng" })
            {
                LibrarianLanguage.Select(lang);
                foreach (var canonical in models)
                {
                    var card = canonical.ToMutable();
                    foreach (bool upgraded in new[] { false, true })
                    {
                        if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
                        string text = card.GetDescriptionForPile(PileType.None);
                        string plain = Regex.Replace(text, @"\[/?[^\]]+\]", "");
                        var tips = card.HoverTips.ToArray();
                        string id = card.Id.Entry["LIBRARIAN-".Length..];
                        Check(!text.Contains('{') && !text.Contains('}'), $"formatted {lang} {id} {upgraded}");
                        Check(tips.Select(t => t.Id).Distinct().Count() == tips.Length, $"unique tips {lang} {id} {upgraded}");
                        foreach (var term in new[] { (Key: "HAND", Word: lang == "zhs" ? "手牌" : "Hand", Expected: handCards.Contains(id)), (Key: "DRAW_PILE", Word: lang == "zhs" ? "抽牌堆" : "Draw Pile", Expected: affected.Contains(id) && id != "DEEP_SEA_BARRIER") })
                        {
                            bool hasTip = tips.Any(t => t.Id == LibrarianHoverTips.Tip(term.Key).Id);
                            Check(hasTip == term.Expected, $"tip scope {term.Key} {lang} {id} {upgraded}");
                            if (!term.Expected) continue;
                            string withoutGold = Regex.Replace(text, @"\[gold\].*?\[/gold\]", "");
                            Check(plain.Contains(term.Word, StringComparison.OrdinalIgnoreCase) && !withoutGold.Contains(term.Word, StringComparison.OrdinalIgnoreCase), $"gold {term.Key} {lang} {id} {upgraded}");
                            var tip = tips.OfType<HoverTip>().Single(t => t.Id == LibrarianHoverTips.Tip(term.Key).Id);
                            Check(tip.Title == term.Word && !string.IsNullOrWhiteSpace(tip.Description) && !tip.Description.Contains('{'), $"localized tip {term.Key} {lang} {id} {upgraded}");
                        }
                        if (id == "TRANSCRIBE")
                            Check(lang != "zhs" || (plain.Contains("手牌和抽牌堆底") && !plain.Contains("手牌和牌堆底")), "Transcribe draw bottom " + upgraded);
                    }
                }
                foreach (var canonical in new PowerModel[] { ModelDb.Power<FuelTheFirePower>(), ModelDb.Power<ToBeContinuedPower>() })
                {
                    var power = canonical.ToMutable();
                    await PowerCmd.Apply(new ThrowingPlayerChoiceContext(), power, player.Creature, 2, player.Creature, null);
                    var tips = power.HoverTips.ToArray();
                    Check(tips.Any(t => t.Id == LibrarianHoverTips.Tip("DRAW_PILE").Id), "live power draw tip " + lang + power.Id);
                    Check(tips.Any(t => t.Id == LibrarianHoverTips.Tip("HAND").Id) == (power is ToBeContinuedPower), "live power hand scope " + lang + power.Id);
                    Check(tips.OfType<HoverTip>().All(t => !t.Description.Contains('{') && !t.Description.Contains('}')), "live power formatted " + lang + power.Id);
                    await PowerCmd.Remove(power);
                }
            }
            await Capture(selected, Check);
        }
        finally { LibrarianLanguage.Select(original); }
        MainFile.Logger.Info($"V110_BETA3_AUDIT_PASS cards=91 phases=364 affected=9 checks={checks} screenshots=28 rulesChanged=False");
    }

    private static async Task Capture(CardModel[] canonical, Action<bool, string> check)
    {
        var layer = new CanvasLayer { Layer = 120 }; NGame.Instance!.AddChild(layer);
        var panel = new Control(); layer.AddChild(panel);
        var background = new ColorRect { Color = new Color("20272e") }; panel.AddChild(background);
        var nodes = new List<NCard>();
        foreach (var card in canonical) { var n = MegaCrit.Sts2.Core.Assets.PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>(); panel.AddChild(n); n.Model = card.ToMutable(); nodes.Add(n); }
        async Task Wait() => await NGame.Instance.ToSignal(NGame.Instance.GetTree().CreateTimer(.25), SceneTreeTimer.SignalName.Timeout);
        async Task Save(string name)
        {
            await NGame.Instance.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var img = NGame.Instance.GetViewport().GetTexture().GetImage();
            string dir = @"D:\Slay The Spire_Mod Dev\outputs\revision-v1.1.0-beta3\screenshots";
            System.IO.Directory.CreateDirectory(dir);
            check(img.SavePng(System.IO.Path.Combine(dir, name + ".png")) == Error.Ok, "capture " + name);
        }
        try
        {
            foreach (var resolution in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                DisplayServer.WindowSetSize(resolution); await Wait();
                var size = NGame.Instance.GetViewport().GetVisibleRect().Size; background.Size = size;
                foreach (string lang in new[] { "zhs", "eng" })
                foreach (bool upgraded in new[] { false, true })
                {
                    LibrarianLanguage.Select(lang);
                    foreach (var n in nodes)
                    {
                        if (upgraded && !n.Model.IsUpgraded) { n.Model.UpgradeInternal(); n.Model.FinalizeUpgradeInternal(); }
                        if (!upgraded && n.Model.IsUpgraded) n.Model.DowngradeInternal();
                        n.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
                        n.Scale = Vector2.One * Math.Min(size.X / 1200, size.Y / 650);
                    }
                    for (int group = 0; group < 3; group++)
                    {
                        for (int i = 0; i < nodes.Count; i++) { nodes[i].Visible = i / 3 == group; nodes[i].Position = new(size.X * ((i % 3) + .5f) / 3, size.Y / 2); }
                        await Wait(); await Save($"cards-{lang}-{resolution.X}-{upgraded}-{group}");
                    }
                    if (resolution.X != 1920) continue;
                    foreach (var n in nodes) n.Visible = false;
                    nodes[0].Visible = true; nodes[0].Position = new(size.X * .25f, size.Y / 2);
                    var owner = new Control { Position = new(size.X * .40f, size.Y * .3f), Size = new(20, 20) }; panel.AddChild(owner);
                    var set = NHoverTipSet.CreateAndShow(owner, nodes[0].Model.HoverTips, HoverTipAlignment.Right);
                    check(set is not null, "native Transcribe hover set " + lang + upgraded);
                    set!.GetParent().RemoveChild(set); panel.AddChild(set);
                    await Wait(); await Save($"transcribe-hover-{lang}-{upgraded}");
                    NHoverTipSet.Remove(owner); owner.QueueFree();
                }
            }
        }
        finally { layer.QueueFree(); DisplayServer.WindowSetSize(new(1280, 720)); await Wait(); }
    }
}
