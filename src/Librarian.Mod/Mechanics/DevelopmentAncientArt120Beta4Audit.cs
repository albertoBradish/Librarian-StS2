using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.PowerCards;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.addons.mega_text;

namespace Librarian.Mechanics;

/// <summary>Opt-in checks of the two portrait replacements in real hand holders and native card inspection.</summary>
internal static class DevelopmentAncientArt120Beta4Audit
{
    internal static async Task Run(Player player)
    {
        string output = System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT")
            ?? throw new InvalidOperationException("LIBRARIAN_AUDIT_OUTPUT is required");
        if (!Path.IsPathFullyQualified(output)) throw new InvalidOperationException("Absolute ancient art audit output required");
        Directory.CreateDirectory(output);
        int checks = 0, screenshots = 0, handScreenshots = 0, inspectScreenshots = 0;
        var records = new List<object>();
        var game = NGame.Instance ?? throw new InvalidOperationException("Native NGame required");
        var originalSize = game.GetWindow().Size;
        string originalLanguage = LibrarianLanguage.Selected;
        var inspect = game.GetInspectCardScreen();
        var specimens = new (CardModel Model, string Art)[]
        {
            (ModelDb.Card<RidgeWard>(), "ridge_ward"),
            (ModelDb.Card<AncientSpark>(), "ancient_spark")
        };
        void Check(bool value, string label)
        {
            if (!value) throw new InvalidOperationException("AncientArt120Beta4: " + label);
            checks++; MainFile.Logger.Info("ANCIENT_ART120_BETA4_CHECK_PASS " + label);
        }
        async Task Wait(double seconds = .4)
            => await game.ToSignal(game.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        static object Size(Vector2 value) => new { x = value.X, y = value.Y };
        static object Rect(Rect2 value) => new { x = value.Position.X, y = value.Position.Y, width = value.Size.X, height = value.Size.Y };
        static string Plain(string value) => Regex.Replace(value, @"\[[^\]]*\]", "").Trim();
        static string Pixels(Texture2D texture)
        {
            using var image = texture.GetImage();
            if (image is null || image.IsEmpty()) throw new InvalidOperationException("Native portrait has no pixels");
            if (image.IsCompressed() && image.Decompress() != Error.Ok)
                throw new InvalidOperationException("Native portrait decompression failed");
            if (image.IsCompressed()) throw new InvalidOperationException("Native portrait remains compressed");
            image.Convert(Image.Format.Rgba8);
            return Convert.ToHexString(SHA256.HashData(image.GetData())).ToLowerInvariant();
        }
        void NativeCard(NCard node, CardModel card, string art, string label, bool completeCard)
        {
            string path = "res://Librarian/images/card_portraits/" + art + ".png";
            Check(node.IsNodeReady() && node.IsVisibleInTree(), "visible native NCard " + label);
            Check(node.Model?.Id == card.Id && node.Model.IsUpgraded == card.IsUpgraded, "native model stage " + label);
            Check(card.Rarity == CardRarity.Ancient && node.Model!.Rarity == CardRarity.Ancient, "Ancient frame routing " + label);
            var portrait = node.GetNode<TextureRect>("%AncientPortrait");
            var texture = portrait.Texture;
            Check(portrait.Visible && !node.GetNode<TextureRect>("%Portrait").Visible
                && !node.GetNode<TextureRect>("%Frame").Visible
                && node.GetNode<TextureRect>("%AncientBorder").Visible, "native Ancient layers " + label);
            Check(card.PortraitPath == path && card.Portrait.ResourcePath == path
                && texture is not null && texture.ResourcePath == path, "original ID portrait path " + label);
            Check(texture!.GetSize().IsEqualApprox(new Vector2(303, 426)), "native small portrait 303x426 " + label);
            Check(texture.GetRid() == card.Portrait.GetRid(), "native renderer uses CardModel.Portrait " + label);
            Check(Math.Abs(portrait.Size.X / portrait.Size.Y - 303f / 426f) < .02f,
                "Ancient destination retains portrait aspect " + label);
            string displayed = node.GetNode<MegaRichTextLabel>("%DescriptionLabel").Text;
            Check(!displayed.Contains('{') && !displayed.Contains('}'), "formatted native description " + label);
            Check(Plain(displayed) == Plain(card.GetDescriptionForPile(PileType.Hand)), "native card text unchanged " + label);
            if (completeCard)
            {
                var view = game.GetViewport().GetVisibleRect();
                foreach (Control part in new Control[] { portrait, node.GetNode<TextureRect>("%AncientBorder"),
                    node.GetNode<MegaLabel>("%TitleLabel"), node.GetNode<MegaRichTextLabel>("%DescriptionLabel") })
                {
                    var rect = part.GetGlobalRect();
                    Check(rect.Size.X > 0 && rect.Size.Y > 0 && rect.Position.X >= view.Position.X - 3
                        && rect.Position.Y >= view.Position.Y - 3 && rect.End.X <= view.End.X + 3
                        && rect.End.Y <= view.End.Y + 3, "complete native inspected " + part.Name + " " + label);
                }
            }
            records.Add(new { view = label, card = card.Id.Entry, language = LibrarianLanguage.Selected,
                upgraded = card.IsUpgraded, portrait_path = path, native_texture_size = Size(texture.GetSize()),
                native_rgba_sha256 = Pixels(texture), portrait_destination = Rect(portrait.GetGlobalRect()),
                title = node.GetNode<MegaLabel>("%TitleLabel").Text, description = displayed,
                native_inspect_uses_small_portrait = completeCard });
        }
        async Task Shot(string name)
        {
            await game.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = game.GetViewport().GetTexture().GetImage();
            Check(image is not null && !image.IsEmpty(), "native viewport pixels " + name);
            Check(image!.SavePng(Path.Combine(output, name + ".png")) == Error.Ok, "native screenshot " + name);
            records.Add(new { screenshot = name + ".png", pixel_width = image.GetWidth(), pixel_height = image.GetHeight(),
                requested_window_size = Size(game.GetWindow().Size), viewport = Rect(game.GetViewport().GetVisibleRect()) });
            screenshots++;
        }
        try
        {
            Check(DisplayServer.GetName() != "headless", "native rendering display");
            Check(NCombatRoom.Instance is not null && player.PlayerCombatState is not null, "actual combat and player hand");
            for (int frame = 0; player.PlayerCombatState!.Phase != PlayerTurnPhase.Play; frame++)
            {
                if (frame > 1200) throw new TimeoutException("Ancient art fixture player phase");
                await game.ToSignal(game.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            // Keep the disposable fixture's hand readable without modifying its deck or playing cards.
            await CardPileCmd.Add(player.PlayerCombatState.Hand.Cards.ToArray(), PileType.Discard, skipVisuals: true);
            foreach (var (model, art) in specimens)
            {
                string small = "res://Librarian/images/card_portraits/" + art + ".png";
                string big = "res://Librarian/images/card_portraits/big/" + art + ".png";
                Check(ResourceLoader.Exists(small) && ResourceLoader.Exists(big), "both original image paths " + model.Id.Entry);
                var smallTexture = ResourceLoader.Load<Texture2D>(small);
                var bigTexture = ResourceLoader.Load<Texture2D>(big);
                Check(smallTexture is not null && smallTexture.GetSize().IsEqualApprox(new Vector2(303, 426)), "small native resource 303x426 " + model.Id.Entry);
                Check(bigTexture is not null && bigTexture.GetSize().IsEqualApprox(new Vector2(606, 852)), "big native resource 606x852 " + model.Id.Entry);
                records.Add(new { card = model.Id.Entry, small_path = small, big_path = big,
                    small_rgba_sha256 = Pixels(smallTexture!), big_rgba_sha256 = Pixels(bigTexture!),
                    small_size = Size(smallTexture!.GetSize()), big_size = Size(bigTexture!.GetSize()),
                    big_resource_checked_separately = true, native_inspect_uses_small_portrait = true });
            }
            foreach (var resolution in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                DisplayServer.WindowSetSize(resolution); await Wait();
                foreach (string language in new[] { "zhs", "eng" })
                {
                    LibrarianLanguage.Select(language); await Wait();
                    foreach (bool upgraded in new[] { false, true })
                    {
                        var cards = new List<(CardModel Card, string Art)>();
                        try
                        {
                            foreach (var (model, art) in specimens)
                            {
                                var card = player.Creature.CombatState!.CreateCard(model, player);
                                if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
                                cards.Add((card, art));
                                await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, player);
                            }
                            await Wait();
                            foreach (var (card, art) in cards)
                            {
                                string label = "hand " + card.Id.Entry + " " + language + " " + resolution.X + " " + upgraded;
                                Check(card.Pile?.Type == PileType.Hand && card.Owner == player, "real hand pile owner " + label);
                                var holder = NCombatRoom.Instance!.Ui.Hand.GetCardHolder(card);
                                Check(holder is NHandCardHolder && ReferenceEquals(holder.CardModel, card), "real native hand holder " + label);
                                var node = NCard.FindOnTable(card, PileType.Hand);
                                Check(node is not null && ReferenceEquals(node, holder!.CardNode) && ReferenceEquals(node.Model, card), "actual hand node identity " + label);
                                node!.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
                                NativeCard(node, card, art, label, completeCard: false);
                            }
                            if (!upgraded)
                            {
                                await Wait(); await Shot($"ancient-hand-{language}-{resolution.X}"); handScreenshots++;
                            }
                            foreach (var (card, art) in cards)
                            {
                                try
                                {
                                    inspect.Open([card], 0, viewAllUpgraded: upgraded); await Wait(.6);
                                    Check(inspect.IsVisibleInTree(), "real native inspection screen " + art + " " + language);
                                    var node = inspect.GetNode<NCard>("Card");
                                    Check(!ReferenceEquals(node.Model, card), "native inspection clones source model " + art);
                                    NativeCard(node, card, art, "inspect " + card.Id.Entry + " " + language + " " + resolution.X + " " + upgraded, completeCard: true);
                                    await Shot($"ancient-inspect-{art}-{language}-{resolution.X}-{upgraded}"); inspectScreenshots++;
                                }
                                finally { inspect.Close(); await Wait(); }
                            }
                        }
                        finally
                        {
                            foreach (var (card, _) in cards)
                                if (card.Pile?.IsCombatPile == true) await CardPileCmd.RemoveFromCombat(card, skipVisuals: true);
                            await Wait();
                        }
                    }
                }
            }
            Check(handScreenshots == 4 && inspectScreenshots == 16 && screenshots == 20, "full bilingual dual-resolution native capture coverage");
            File.WriteAllText(Path.Combine(output, "ancient-art-checks.json"), JsonSerializer.Serialize(new { checks, screenshots,
                hand_screenshots = handScreenshots, inspect_screenshots = inspectScreenshots, cards = 2, languages = 2,
                resolutions = 2, base_and_upgrade = true, records, live_multiclient = false,
                user_visual_acceptance = "not asserted by automated checks" }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            inspect.Close(); LibrarianLanguage.Select(originalLanguage);
            DisplayServer.WindowSetSize(originalSize); await Wait();
        }
        MainFile.Logger.Info($"ANCIENT_ART120_BETA4_AUDIT_PASS checks={checks} screenshots={screenshots} handScreenshots={handScreenshots} inspectScreenshots={inspectScreenshots} cards=2 languages=2 resolutions=2 baseAndUpgrade=True nativeHand=True nativeInspect=True bigResourcesLoaded=True nativeInspectUsesSmallPortrait=True liveMulticlient=False");
    }
}
