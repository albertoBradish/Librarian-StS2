using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.Stateful;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.addons.mega_text;

namespace Librarian.Mechanics;

/// <summary>Opt-in native Transcribe keyword tooltip checks, without changing its copy rules.</summary>
internal static class DevelopmentTranscribeHover120Beta3Audit
{
    internal static async Task Run(Player player)
    {
        string output = System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT")
            ?? throw new InvalidOperationException("LIBRARIAN_AUDIT_OUTPUT is required");
        if (!Path.IsPathFullyQualified(output)) throw new InvalidOperationException("Absolute Transcribe audit output required");
        Directory.CreateDirectory(output);
        int checks = 0, screenshots = 0;
        var context = new ThrowingPlayerChoiceContext();
        var records = new List<object>();
        string originalLanguage = LibrarianLanguage.Selected;
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("Transcribe120Beta3: " + label);
            checks++; MainFile.Logger.Info("TRANSCRIBE120_BETA3_CHECK_PASS " + label);
        }
        void Tips(CardModel card, string label)
        {
            var tips = card.HoverTips.ToArray();
            Check(IHoverTip.RemoveDupes(tips).SequenceEqual(tips), "native dedupe " + label);
            foreach (var keyword in new[] { CardKeyword.Ethereal, CardKeyword.Exhaust })
            {
                var expected = HoverTipFactory.FromKeyword(keyword);
                Check(tips.Count(t => t.Id == expected.Id) == 1, "exactly one native " + keyword + " " + label);
                Check(tips.Single(t => t.Id == expected.Id).Equals(expected), "original native keyword tip " + keyword + " " + label);
            }
            Check(tips.OfType<HoverTip>().All(t => !t.Description.Contains('{') && !t.Description.Contains('}')), "formatted tips " + label);
            records.Add(new { label, id = card.Id.Entry, upgraded = card.IsUpgraded,
                keywords = card.Keywords.Select(k => k.ToString()).ToArray(),
                description = card.GetDescriptionForPile(PileType.None),
                tips = tips.OfType<HoverTip>().Select(t => new { t.Id, t.Title, t.Description }).ToArray() });
        }
        void Body(Transcribe card, string label)
        {
            Check(card.EnergyCost.GetWithModifiers(CostModifiers.None) == 1 && card.Rarity == CardRarity.Rare, "cost and rarity unchanged " + label);
            Check(card.Keywords.Contains(CardKeyword.Exhaust) == !card.IsUpgraded && !card.Keywords.Contains(CardKeyword.Ethereal), "body keywords unchanged " + label);
            string description = Regex.Replace(card.GetDescriptionForPile(PileType.None), @"\[[^\]]*\]", "");
            if (LibrarianLanguage.Selected == "zhs")
                Check(description.Contains("将2张复制品分别加入手牌和抽牌堆底部。"), "user card sentence unchanged " + label);
            Tips(card, label);
        }
        try
        {
            foreach (string language in new[] { "zhs", "eng" })
            {
                LibrarianLanguage.Select(language);
                foreach (bool upgraded in new[] { false, true })
                {
                    string label = language + " " + upgraded;
                    var libraryCard = (Transcribe)ModelDb.Card<Transcribe>().ToMutable(); libraryCard.Owner = player;
                    if (upgraded) { libraryCard.UpgradeInternal(); libraryCard.FinalizeUpgradeInternal(); }
                    Check(libraryCard.CombatState is null, "non-combat preview " + label);
                    Body(libraryCard, "preview " + label);

                    var combat = player.Creature.CombatState!;
                    var source = combat.CreateCard<LibrarianDefend>(player);
                    await CardCmd.AutoPlay(context, source, player.Creature, skipCardPileVisuals: true);
                    var card = combat.CreateCard<Transcribe>(player);
                    if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
                    await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, player);
                    Check(card.CombatState == combat && card.Pile?.Type == PileType.Hand, "actual hand " + label);
                    Body(card, "hand " + label);
                    screenshots += await Capture(card, language, output, Check);

                    var handBefore = player.PlayerCombatState!.Hand.Cards.ToHashSet();
                    var drawBefore = player.PlayerCombatState.DrawPile.Cards.ToHashSet();
                    await CardCmd.AutoPlay(context, card, player.Creature, skipCardPileVisuals: true);
                    var handCopy = player.PlayerCombatState.Hand.Cards.Single(c => c.Id == source.Id && !handBefore.Contains(c));
                    var bottomCopy = player.PlayerCombatState.DrawPile.Cards.Single(c => c.Id == source.Id && !drawBefore.Contains(c));
                    Check(!ReferenceEquals(handCopy, bottomCopy) && ReferenceEquals(player.PlayerCombatState.DrawPile.Cards.Last(), bottomCopy), "two distinct copies and draw bottom unchanged " + label);
                    foreach (var copy in new[] { handCopy, bottomCopy })
                    {
                        Check(copy.Keywords.Contains(CardKeyword.Ethereal) && copy.Keywords.Contains(CardKeyword.Exhaust), "actual copy keywords unchanged " + label + " " + copy.Pile!.Type);
                        Tips(copy, "copy " + label + " " + copy.Pile!.Type);
                    }
                    Check(card.Pile?.Type == (upgraded ? PileType.Discard : PileType.Exhaust), "body destination unchanged " + label);
                }
            }
            File.WriteAllText(Path.Combine(output, "transcribe-hover-checks.json"), JsonSerializer.Serialize(new { checks, screenshots, records }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { LibrarianLanguage.Select(originalLanguage); }
        MainFile.Logger.Info($"TRANSCRIBE120_BETA3_AUDIT_PASS checks={checks} screenshots={screenshots} languages=2 baseAndUpgrade=True nativeCopies=True liveMulticlient=False");
    }

    private static async Task<int> Capture(Transcribe card, string language, string output, Action<bool, string> check)
    {
        check(DisplayServer.GetName() != "headless", "native tooltip renderer");
        var originalSize = NGame.Instance!.GetWindow().Size;
        var layer = new CanvasLayer { Layer = 120 }; NGame.Instance.AddChild(layer);
        var panel = new Control(); layer.AddChild(panel);
        var background = new ColorRect { Color = new Color("20272e") }; panel.AddChild(background);
        var node = PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>();
        panel.AddChild(node); node.Model = card;
        int screenshots = 0;
        async Task Wait() => await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(.25), SceneTreeTimer.SignalName.Timeout);
        try
        {
            foreach (var resolution in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                DisplayServer.WindowSetSize(resolution); await Wait();
                var size = NGame.Instance.GetViewport().GetVisibleRect().Size; background.Size = size;
                node.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
                node.Position = new(size.X * .25f, size.Y * .5f);
                node.Scale = Vector2.One * Math.Min(size.X / 1280, size.Y / 750);
                var owner = new Control { Position = new(size.X * .45f, size.Y * .25f), Size = new(20, 20) }; panel.AddChild(owner);
                var set = NHoverTipSet.CreateAndShow(owner, card.HoverTips, HoverTipAlignment.Right);
                check(set is not null, "native hover set " + language + " " + card.IsUpgraded);
                set!.GetParent().RemoveChild(set); panel.AddChild(set);
                try
                {
                    await Wait();
                    var rendered = set.GetNode<Control>("textHoverTipContainer").GetChildren().OfType<Control>().ToArray();
                    check(rendered.Length == 2, "exactly two rendered keyword panels " + language + " " + card.IsUpgraded);
                    foreach (var keyword in new[] { CardKeyword.Ethereal, CardKeyword.Exhaust })
                    {
                        string description = ((HoverTip)HoverTipFactory.FromKeyword(keyword)).Description;
                        check(rendered.Count(t => t.GetNode<MegaRichTextLabel>("%Description").Text == description) == 1,
                            "exactly one rendered native " + keyword + " " + language + " " + card.IsUpgraded);
                    }
                    await NGame.Instance.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
                    check(image.SavePng(Path.Combine(output, $"transcribe-hover-{language}-{resolution.X}-{card.IsUpgraded}.png")) == Error.Ok, "native hover screenshot");
                    screenshots++;
                }
                finally { NHoverTipSet.Remove(owner); owner.QueueFree(); }
                await Wait();
            }
        }
        finally { layer.QueueFree(); DisplayServer.WindowSetSize(originalSize); await Wait(); }
        return screenshots;
    }
}
