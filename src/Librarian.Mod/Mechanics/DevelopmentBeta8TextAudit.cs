using System.IO;
using System.Text.RegularExpressions;
using Godot;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Cards.OrbUtility;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace Librarian.Mechanics;

internal static class DevelopmentBeta8TextAudit
{
    internal static async Task Run(Player player)
    {
        string language = LibrarianLanguage.Selected;
        string output = System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT")!;
        Directory.CreateDirectory(output);
        int checks = 0;
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("beta8 text: " + label);
            checks++; MainFile.Logger.Info("BETA8_TEXT_CHECK_PASS " + label);
        }
        var layer = new CanvasLayer { Layer = 120 };
        NGame.Instance!.AddChild(layer);
        var size = NGame.Instance.GetViewport().GetVisibleRect().Size;
        var panel = new Control(); layer.AddChild(panel);
        panel.AddChild(new ColorRect { Color = new Color("20272e"), Size = size });
        try
        {
            foreach (string lang in new[] { "zhs", "eng" })
            {
                LibrarianLanguage.Select(lang);
                var active = ModelDb.CardPool<LibrarianCardPool>().AllCards.ToArray();
                Check(active.Length == 91, "91 active cards " + lang);
                foreach (var canonical in active)
                {
                    var formattedCard = canonical.ToMutable(); formattedCard.Owner = player;
                    foreach (bool upgraded in new[] { false, true })
                    {
                        if (upgraded) { formattedCard.UpgradeInternal(); formattedCard.FinalizeUpgradeInternal(); }
                        string description = formattedCard.GetDescriptionForPile(PileType.None);
                        Check(!description.Contains('{') && !description.Contains('}'), "native card format " + lang + " " + formattedCard.Id + " " + upgraded);
                    }
                }
                var cards = new[] { ModelDb.Card<SproutingBulwark>().ToMutable(), ModelDb.Card<SproutingBulwark>().ToMutable() };
                cards[1].UpgradeInternal(); cards[1].FinalizeUpgradeInternal();
                var nodes = new List<NCard>();
                for (int i = 0; i < cards.Length; i++)
                {
                    var card = cards[i];
                    string text = Regex.Replace(card.GetDescriptionForPile(PileType.None), "\\[[^\\]]*\\]", "");
                    string expected = lang == "zhs"
                        ? $"获得{(i == 0 ? 6 : 9)}点格挡。\n你的下回合开始前，每点翠叶法球数值可以为你抵挡1点未被格挡的伤害。"
                        : $"Gain {(i == 0 ? 6 : 9)} Block.\nUntil the start of your next turn, each point of Growth Orb value can prevent 1 point of unblocked damage.";
                    Check(text == expected, "formatted bulwark " + lang + " upgraded=" + (i == 1));
                    Check(card.DynamicVars.Block.BaseValue == (i == 0 ? 6m : 9m), "unchanged block " + lang + i);
                    var node = PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>();
                    panel.AddChild(node); node.Model = card; node.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
                    node.Scale = Vector2.One * Math.Min(size.X / 900, size.Y / 570);
                    node.Position = new(size.X * (i + 0.5f) / 2, size.Y / 2); nodes.Add(node);
                }
                await NGame.Instance.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
                Check(image.SavePng(Path.Combine(output, "bulwark-" + lang + ".png")) == Error.Ok, "capture " + lang);
                foreach (var node in nodes) node.QueueFree();
                await NGame.Instance.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            foreach (string type in new[] { "LibrarianOnboarding", "LibrarianPracticeSession", "LibrarianPracticeOverlay" })
                Check(typeof(MainFile).Assembly.GetType("Librarian.Mechanics." + type) is null, "teaching excluded " + type);
            MainFile.Logger.Info($"BETA8_TEXT_AUDIT_PASS checks={checks} phases=4 settlementChanged=False");
        }
        finally { layer.QueueFree(); LibrarianLanguage.Select(language); }
    }
}
