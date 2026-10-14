using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.addons.mega_text;

namespace Librarian.Mechanics;

/// <summary>Explicit, isolated native rendering check of the user-adopted r5 portraits; inactive in normal play.</summary>
internal static class DevelopmentCardArtR5Audit
{
    internal static async Task Run()
    {
        string output = System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT")
            ?? throw new InvalidOperationException("Absolute art audit output required");
        string manifest = System.Environment.GetEnvironmentVariable("LIBRARIAN_CARD_ART_MANIFEST")
            ?? throw new InvalidOperationException("Exact card art integration manifest required");
        if (!Path.IsPathFullyQualified(output) || !Path.IsPathFullyQualified(manifest)
            || !OS.GetUserDataDir().Contains("card-art-r5-integration-20261009", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Card art audit requires its isolated profile and absolute paths");
        Directory.CreateDirectory(output);
        using var source = JsonDocument.Parse(File.ReadAllText(manifest));
        var entries = source.RootElement.GetProperty("cards").EnumerateArray().Select(x => new
        {
            Id = x.GetProperty("card_id").GetString()!,
            Art = x.GetProperty("portrait_slug").GetString()!,
            Name = x.GetProperty("name").GetString()!
        }).ToArray();
        int checks = 0, handViews = 0, inspectViews = 0, screenshots = 0;
        var records = new List<object>();
        var game = NGame.Instance ?? throw new InvalidOperationException("Native game required");
        var originalSize = game.GetWindow().Size;
        string originalLanguage = LibrarianLanguage.Selected;
        var inspect = game.GetInspectCardScreen();
        void Check(bool value, string label)
        {
            if (!value) throw new InvalidOperationException("CardArtR5: " + label);
            checks++;
        }
        async Task Wait(double seconds = .2)
            => await game.ToSignal(game.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        async Task Ready(Player player)
        {
            for (int i = 0; !CombatManager.Instance.IsInProgress || player.PlayerCombatState?.Phase != PlayerTurnPhase.Play; i++)
            {
                if (i > 1800) throw new TimeoutException("Art fixture combat play phase");
                await game.ToSignal(game.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
        static object Rect(Rect2 r) => new { x = r.Position.X, y = r.Position.Y, width = r.Size.X, height = r.Size.Y };
        async Task Shot(string name, string? card = null, Rect2? portraitRect = null)
        {
            await game.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = game.GetViewport().GetTexture().GetImage();
            Check(image is not null && !image.IsEmpty() && image.SavePng(Path.Combine(output, name + ".png")) == Error.Ok, "viewport screenshot " + name);
            records.Add(new { screenshot = name + ".png", card, portrait_destination = portraitRect is { } r ? Rect(r) : null,
                width = image!.GetWidth(), height = image.GetHeight(), window_width = game.GetWindow().Size.X,
                window_height = game.GetWindow().Size.Y, viewport = Rect(game.GetViewport().GetVisibleRect()) });
            screenshots++;
        }
        string DumpTexture(Texture2D texture, string name)
        {
            using var image = texture.GetImage();
            Check(image is not null && !image.IsEmpty(), "native texture pixels " + name);
            if (image!.IsCompressed()) Check(image.Decompress() == Error.Ok, "native texture decode " + name);
            image.Convert(Image.Format.Rgba8);
            string path = Path.Combine(output, "decoded", name + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Check(image.SavePng(path) == Error.Ok, "save decoded owned portrait " + name);
            return Convert.ToHexString(SHA256.HashData(image.GetData())).ToLowerInvariant();
        }
        Rect2 NativeCard(NCard node, CardModel card, string art, string label, bool complete)
        {
            string path = "res://Librarian/images/card_portraits/" + art + ".png";
            var portrait = node.GetNode<TextureRect>("%Portrait");
            Check(node.IsVisibleInTree() && node.Model?.Id == card.Id, "visible native model " + label);
            Check(portrait.Visible && !node.GetNode<TextureRect>("%AncientPortrait").Visible, "standard frame route " + label);
            Check(card.PortraitPath == path && portrait.Texture?.ResourcePath == path, "native portrait path " + label);
            Check(portrait.Texture!.GetSize().IsEqualApprox(new Vector2(250, 190)), "native small dimensions " + label);
            Check(portrait.Texture.GetRid() == card.Portrait.GetRid(), "native uses CardModel portrait " + label);
            Check(Math.Abs(portrait.Size.X / portrait.Size.Y - 250f / 190f) < .03f, "portrait destination aspect " + label);
            string text = node.GetNode<MegaRichTextLabel>("%DescriptionLabel").Text;
            Check(!text.Contains('{') && !text.Contains('}'), "formatted native description " + label);
            if (complete)
            {
                var view = game.GetViewport().GetVisibleRect();
                foreach (Control part in new Control[] { portrait, node.GetNode<MegaLabel>("%TitleLabel"), node.GetNode<MegaRichTextLabel>("%DescriptionLabel") })
                {
                    var r = part.GetGlobalRect();
                    Check(r.Size.X > 0 && r.Size.Y > 0 && r.Position.X >= view.Position.X - 3 && r.Position.Y >= view.Position.Y - 3
                        && r.End.X <= view.End.X + 3 && r.End.Y <= view.End.Y + 3, "complete inspected " + part.Name + " " + label);
                }
            }
            records.Add(new { view = label, card = card.Id.Entry, language = LibrarianLanguage.Selected, upgraded = card.IsUpgraded,
                portrait_path = path, portrait_destination = Rect(portrait.GetGlobalRect()), description = text,
                native_inspection_uses_small_portrait = complete });
            return portrait.GetGlobalRect();
        }
        try
        {
            Check(DisplayServer.GetName() != "headless" && game.MainMenu!.IsVisibleInTree(), "rendered native menu");
            Check(LibrarianUpdateNotice051.CurrentVersion == "1.2.0-beta13", "exact loaded mod version");
            Check(entries.Length == 89 && entries.Select(x => x.Id).Distinct().Count() == 89, "89 unique targets");
            LibrarianLanguage.Select("zhs");
            await Shot("menu-before-run");
            var pool = ModelDb.CardPool<LibrarianCardPool>().AllCards.ToDictionary(c => c.Id.Entry);
            Check(pool.Count == 91 && entries.All(e => pool.ContainsKey(e.Id)), "all targets in native active pool");
            foreach (var e in entries)
            {
                string small = "res://Librarian/images/card_portraits/" + e.Art + ".png";
                string big = "res://Librarian/images/card_portraits/big/" + e.Art + ".png";
                Check(pool[e.Id].PortraitPath == small && ResourceLoader.Exists(small) && ResourceLoader.Exists(big), "ID small/big route " + e.Id);
                var st = ResourceLoader.Load<Texture2D>(small);var bt = ResourceLoader.Load<Texture2D>(big);
                Check(st is not null && st.GetSize().IsEqualApprox(new Vector2(250, 190)), "small loaded dimensions " + e.Id);
                Check(bt is not null && bt.GetSize().IsEqualApprox(new Vector2(1000, 760)), "big loaded dimensions " + e.Id);
                records.Add(new { card = e.Id, art = e.Art, small_path = small, big_path = big,
                    small_rgba_sha256 = DumpTexture(st!, "small/" + e.Art), big_rgba_sha256 = DumpTexture(bt!, "big/" + e.Art) });
            }
            MainFile.Logger.Info("CARD_ART_R5_RESOURCES_PASS cards=89 resources=178");
            LibrarianUnlocks040.ApplyChoice(SaveManager.Instance.Progress, true);
            var run = await game.StartNewSingleplayerRun(ModelDb.Character<LibrarianCharacter>(), true, ActModel.GetDefaultList(), [], "ART_R5", GameMode.Standard);
            var player = run.Players.Single();
            await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<TunnelerWeak>().ToMutable());
            await Ready(player);await Wait(.4);
            await CardPileCmd.Add(player.PlayerCombatState!.Hand.Cards.ToArray(), PileType.Discard, skipVisuals: false);
            foreach (var resolution in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                DisplayServer.WindowSetSize(resolution);await Wait(.5);
                foreach (var batch in entries.Chunk(8))
                {
                    var cards = new List<(CardModel Card, string Art)>();
                    try
                    {
                        foreach (var e in batch)
                        {
                            var card = player.Creature.CombatState!.CreateCard(pool[e.Id], player);cards.Add((card, e.Art));
                            await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, player);
                        }
                        await Wait(.35);
                        foreach (var (card, art) in cards)
                        {
                            var holder = NCombatRoom.Instance!.Ui.Hand.GetCardHolder(card);
                            Check(holder is NHandCardHolder && ReferenceEquals(holder.CardModel, card), "real hand holder " + card.Id);
                            holder!.CardNode.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
                            NativeCard(holder.CardNode, card, art, "hand " + card.Id.Entry + " " + resolution.X, false);handViews++;
                        }
                        await Shot("hand-" + resolution.X + "-" + batch[0].Art);
                        foreach (var (card, art) in cards)
                        {
                            try
                            {
                                inspect.Open([card], 0);await Wait(.3);
                                Check(inspect.IsVisibleInTree(), "native inspect screen " + card.Id);
                                var node = inspect.GetNode<NCard>("Card");
                                var r = NativeCard(node, card, art, "inspect " + card.Id.Entry + " " + resolution.X, true);
                                await Shot("inspect-" + resolution.X + "-" + art, card.Id.Entry, r);inspectViews++;
                            }
                            finally { inspect.Close();await Wait(.1); }
                        }
                        MainFile.Logger.Info("CARD_ART_R5_BATCH_PASS resolution=" + resolution.X + " last=" + batch[^1].Id);
                    }
                    finally
                    {
                        // Use the native discard route so holder movement and pooling complete normally.
                        // Removing generated cards directly starts detached exhaust VFX that can outlive room teardown.
                        await CardPileCmd.Add(cards.Select(x => x.Card).Where(c => c.Pile?.Type == PileType.Hand).ToArray(),
                            PileType.Discard, skipVisuals: false);
                        await Wait(.6);
                    }
                }
            }
            Check(handViews == 178 && inspectViews == 178, "all 89 portraits in hand and inspect at two resolutions");
            foreach (var card in PileType.Deck.GetPile(player).Cards.ToArray()) await CardPileCmd.RemoveFromDeck(card, false);
            foreach (var e in entries) await CardPileCmd.Add(player.RunState.CreateCard(pool[e.Id], player), PileType.Deck);
            await Wait();await SaveManager.Instance.SaveRun(null);
            var saved = SaveManager.Instance.LoadRunSave();Check(saved.Success && saved.SaveData is not null, "real isolated save read");
            var restored = RunState.FromSerializable(saved.SaveData!);
            await game.ReturnToMainMenu();await RunManager.Instance.SetUpSavedSingleplayer(restored, saved.SaveData!);
            await game.LoadRun(restored, saved.SaveData!.PreFinishedRoom);await game.Transition.FadeIn();
            player = restored.Players.Single();
            Check(RunManager.Instance.IsInProgress && PileType.Deck.GetPile(player).Cards.Count == 89, "native reload 89-card deck");
            Check(PileType.Deck.GetPile(player).Cards.Select(c => c.Id.Entry).Order().SequenceEqual(entries.Select(e => e.Id).Order()), "reloaded IDs unchanged");
            await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<TunnelerWeak>().ToMutable());await Ready(player);
            await Wait(.4);await Shot("combat-after-reload");
            MainFile.Logger.Info("SAVE_RELOAD_AUDIT_PASS revision=120-card-art-r5 cards=89 continuation=True");
            File.WriteAllText(Path.Combine(output, "card-art-r5-checks.json"), JsonSerializer.Serialize(new { checks, cards = 89,
                resources = 178, hand_views = handViews, inspect_views = inspectViews, screenshots, languages = new[] { "zhs" },
                requested_resolutions = new[] { "1280x720", "1920x1080" }, records, real_save_reload = true,
                only_portrait_change = true, live_multiclient = false }, new JsonSerializerOptions { WriteIndented = true }));
            MainFile.Logger.Info($"CARD_ART_R5_AUDIT_PASS cards=89 resources=178 handViews={handViews} inspectViews={inspectViews} screenshots={screenshots} checks={checks} menu=True newRun=True combat=True save=True reload=True continuation=True");
        }
        finally
        {
            inspect.Close();LibrarianLanguage.Select(originalLanguage);DisplayServer.WindowSetSize(originalSize);await Wait();
        }
        game.Quit();
    }
}
