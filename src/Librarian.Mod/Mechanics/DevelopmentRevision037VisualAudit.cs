using System.IO;
using Godot;
using BaseLib.Utils;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Powers;
using Librarian.LibrarianCode.Relics;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace Librarian.Mechanics;

internal static class DevelopmentRevision037VisualAudit
{
    private static string Output => System.Environment.GetEnvironmentVariable("LIBRARIAN_VISUAL_OUTPUT")
        ?? @"D:\Slay The Spire_Mod Dev\.research\revision-v037-screenshots";
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("037 visual audit: " + message);
        MainFile.Logger.Info("VISUAL_037_CHECK_PASS " + message);
    }
    private static async Task Wait(double seconds = 0.15)
        => await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private static async Task Capture(string name)
    {
        await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
        Require(image != null && !image.IsEmpty(), "viewport " + name);
        var path = Path.Combine(Output, name + ".png");
        Require(image!.SavePng(path) == Error.Ok, "screenshot " + name);
        MainFile.Logger.Info($"VISUAL_037_SCREENSHOT path={path} size={image.GetWidth()}x{image.GetHeight()}");
    }
    private static void TextureCheck(Texture2D texture, string name, int maximum)
    {
        Require(texture != null && texture.ResourcePath.StartsWith("res://Librarian/"), "own texture " + name);
        Require(texture!.GetWidth() > 0 && texture.GetHeight() > 0 && texture.GetWidth() <= maximum && texture.GetHeight() <= maximum,
            $"bounded texture {name} {texture.GetWidth()}x{texture.GetHeight()}");
        using var pixels = texture.GetImage();
        Require(pixels != null && !pixels.IsEmpty(), "texture pixels " + name);
        if (pixels!.IsCompressed()) Require(pixels.Decompress() == Error.Ok, "decompress " + name);
        bool transparent = false, opaque = false;
        for (int y = 0; y < pixels.GetHeight(); y++)
            for (int x = 0; x < pixels.GetWidth(); x++)
            {
                float alpha = pixels.GetPixel(x, y).A;
                transparent |= alpha == 0;
                opaque |= alpha > 0.9;
            }
        Require(transparent && opaque, "real transparent exterior and opaque subject " + name);
    }
    private static void AddTile(Control root, Texture2D texture, string title, Vector2 position, Vector2 size)
    {
        var image = new TextureRect { Position = position, Size = new Vector2(size.X, size.Y - 32),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Texture = texture, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(image);
        var label = new Label { Position = position + new Vector2(0, size.Y - 31), Size = new Vector2(size.X, 30),
            Text = title, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", 16);
        root.AddChild(label);
    }

    internal static async Task Run(Player player)
    {
        if (!DevelopmentVisualAudit.Enabled) return;
        Require(OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase), "isolated profile");
        Require(DisplayServer.GetName() != "headless", "rendering enabled");
        Directory.CreateDirectory(Output);
        var game = NGame.Instance!;
        var creature = NCombatRoom.Instance!.GetCreatureNode(player.Creature)!;
        Require(creature != null && creature.Visuals.Name == "LibrarianCombat", "mounted custom native combat visuals");
        Require(!creature!.HasSpineAnimation, "custom scene has no Ironclad skeleton");
        var animation = creature.Visuals.GetNode<AnimationPlayer>("Motion/AnimationPlayer");
        Require(CustomAnimation.HasCustomAnimation(creature), "BaseLib discovers nested AnimationPlayer from real NCreature");
        TextureCheck(creature.Visuals.GetNode<Sprite2D>("Visuals/Body").Texture, "combat body", 768);
        var originalDeathTask = creature.DeathAnimationTask;
        try
        {
            foreach (string trigger in new[] { "Attack", "Cast", "Hit" })
            {
                creature.SetAnimationTrigger(trigger);
                await Wait(0.06);
                Require(animation.CurrentAnimation == trigger, "native trigger reaches " + trigger);
                await Wait(0.65);
                Require(animation.CurrentAnimation == "Idle", trigger + " returns to Idle");
            }
            float reportedDeathDuration = creature.StartDeathAnim(shouldRemove: false);
            Require(Math.Abs(reportedDeathDuration - 1.2f) < 0.01f, "native StartDeathAnim reports custom 1.2-second duration");
            Task? nativeDeathTask = creature.DeathAnimationTask;
            Require(nativeDeathTask != null, "native death task created");
            Task completed = await Task.WhenAny(nativeDeathTask!, Wait(4.0));
            Require(completed == nativeDeathTask, "native AnimDie task completes within four seconds");
            await nativeDeathTask!;
            await Wait(1.4);
            Require(creature.Visuals.GetNode<Node2D>("Visuals").Modulate.A < 0.05f, "native Dead fades fully");
            Require(animation.CurrentAnimation != "Idle", "Dead holds instead of returning to Idle");
            await Capture("037-native-dead-hold");
            creature.StartReviveAnim();
            await Wait(0.9);
            Require(animation.CurrentAnimation == "Idle" && creature.Visuals.GetNode<Node2D>("Visuals").Modulate.A > 0.99f,
                "native Revive restores visible Idle");
        }
        finally
        {
            // The fixture never changes entity HP/death state, so restore its temporary UI state too.
            creature.StartReviveAnim();
            creature.DeathAnimationTask = originalDeathTask;
            creature.SetAnimationTrigger("Idle");
        }

        var relics = player.Character.RelicPool.AllRelics.Where(r => r is not TatteredSpellScroll)
            .Append(ModelDb.Relic<RestoredSpellScroll>()).DistinctBy(r => r.Id).ToArray();
        var potions = player.Character.PotionPool.AllPotions.ToArray();
        var powers = ModelDb.AllPowers.OfType<LibrarianPower>().ToArray();
        Require(relics.Length == 8 && potions.Length == 3 && powers.Length == 18, "art catalog 8 relics 3 potions 18 powers");
        foreach (var relic in relics) { TextureCheck(relic.Icon, relic.Id.ToString(), 100); TextureCheck(relic.IconOutline, relic.Id + " outline", 100); TextureCheck(relic.BigIcon, relic.Id + " large", 2048); }
        foreach (var potion in potions) { TextureCheck(potion.Image, potion.Id.ToString(), 256); TextureCheck(potion.Outline!, potion.Id + " outline", 256); }
        foreach (var power in powers) { TextureCheck(power.Icon, power.Id.ToString(), 100); TextureCheck(power.BigIcon, power.Id + " large", 2048); }
        var hands = new[] { player.Character.ArmPointingTexture, player.Character.ArmRockTexture, player.Character.ArmPaperTexture, player.Character.ArmScissorsTexture };
        foreach (var hand in hands) { TextureCheck(hand, hand.ResourcePath, 1200); Require(hand.GetWidth() == 422 && hand.GetHeight() == 1200, "native hand footprint"); }

        var layer = new CanvasLayer { Layer = 100, Name = "Revision037ArtFixture" };
        game.AddChild(layer);
        var cardRoot = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        var catalog = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        var identities = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        layer.AddChild(cardRoot); layer.AddChild(catalog); layer.AddChild(identities);
        var window = game.GetWindow();
        var originalSize = window.Size;
        var cards = new List<NCard>();
        var rest = NRestSiteCharacter.Create(player, 0);
        var shop = PreloadManager.Cache.GetScene(player.Character.MerchantAnimPath).Instantiate<NMerchantCharacter>();
        identities.AddChild(rest); identities.AddChild(shop);
        Require(rest.Name == "LibrarianRest" && rest.Hitbox != null, "native rest conversion mounted with hitbox");
        Require(shop.Name == "LibrarianShop", "native merchant conversion mounted");
        var shopMotion = LibrarianMerchantFactoryCompatibility.FindMotion(shop);
        Require(shopMotion != null && shopMotion.IsNodeReady(), "native merchant nested motion component ready");
        TextureCheck(shopMotion!.GetParent().GetNode<Sprite2D>(shopMotion.VisualPath + "/Body").Texture, "merchant body", 768);
        shop.PlayAnimation("relaxed_loop", true);
        Require(CustomAnimation.HasCustomAnimation(shop), "native merchant custom animation route");
        string shopAnimation = shopMotion!.GetNode<AnimationPlayer>("AnimationPlayer").CurrentAnimation;
        Require(shopAnimation == "idle" || shopAnimation == "Idle" || shopAnimation == "relaxed_loop",
            "native merchant relaxed route reaches AnimationPlayer");
        var originalShopName = shop.Name;
        try
        {
            shop.Name = "LibrarianShop2";
            shop.PlayAnimation("relaxed_loop", true);
            Require(shopMotion.GetNode<AnimationPlayer>("AnimationPlayer").CurrentAnimation == "Idle",
                "renamed duplicate Librarian merchant still routes native animation");
        }
        finally { shop.Name = originalShopName; }
        try
        {
            foreach (CardModel canonical in new CardModel[] { ModelDb.Card<MultiplayerPlaceholderA>(), ModelDb.Card<MultiplayerPlaceholderB>(),
                ModelDb.Card<AncientSpark>(), ModelDb.Card<CirculationNotes>(), ModelDb.Card<SharedShelter>() })
            {
                var card = PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>();
                cardRoot.AddChild(card);
                card.Model = player.Creature.CombatState!.CreateCard(canonical, player);
                card.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
                Require(card.Model.PortraitPath.StartsWith("res://Librarian/") && !card.Model.PortraitPath.EndsWith("/card.png"), "new card art " + canonical.Id);
                cards.Add(card);
            }
            foreach (var resolution in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                window.Size = resolution; await Wait(0.5);
                var view = game.GetViewport().GetVisibleRect().Size;
                string suffix = $"{resolution.X}x{resolution.Y}";
                cardRoot.Visible = true; catalog.Visible = false; identities.Visible = false;
                for (int i = 0; i < cards.Count; i++)
                {
                    cards[i].Position = new Vector2(view.X * (0.38f + i * 0.12f), view.Y * 0.32f);
                    cards[i].Scale = Vector2.One * (view.X / 2600f);
                }
                await Wait(); await Capture("037-combat-five-cards-energy-" + suffix);
                cardRoot.Visible = false; catalog.Visible = true;
                foreach (var child in catalog.GetChildren()) { catalog.RemoveChild(child); child.QueueFree(); }
                catalog.AddChild(new ColorRect { Color = new Color("25303c"), Size = view, MouseFilter = Control.MouseFilterEnum.Ignore });
                var samples = relics.Select(r => (r.Icon, r.Title.GetFormattedText()))
                    .Concat(potions.Select(p => (p.Image, p.Title.GetFormattedText())))
                    .Concat(powers.Select(p => (p.Icon, p.Title.GetFormattedText()))).ToArray();
                var cell = new Vector2(view.X / 8f, view.Y / 4f);
                for (int i = 0; i < samples.Length; i++)
                    AddTile(catalog, samples[i].Item1, samples[i].Item2, new Vector2(i % 8 * cell.X, i / 8 * cell.Y), cell);
                await Wait(); await Capture("037-eight-relics-three-potions-eighteen-powers-" + suffix);
                catalog.Visible = false; identities.Visible = true;
                foreach (var child in identities.GetChildren().Where(n => n != rest && n != shop).ToArray()) { identities.RemoveChild(child); child.QueueFree(); }
                var backdrop = new ColorRect { Color = new Color("35424f"), Size = view, MouseFilter = Control.MouseFilterEnum.Ignore };
                identities.AddChild(backdrop); identities.MoveChild(backdrop, 0);
                rest.Position = new Vector2(view.X * 0.15f, view.Y * 0.62f);
                shop.Position = new Vector2(view.X * 0.36f, view.Y * 0.62f);
                for (int i = 0; i < hands.Length; i++)
                    AddTile(identities, hands[i], new[] { "Point", "Rock", "Paper", "Scissors" }[i],
                        new Vector2(view.X * (0.49f + i * 0.125f), view.Y * 0.1f), new Vector2(view.X * 0.12f, view.Y * 0.8f));
                await Wait(); await Capture("037-native-rest-shop-and-four-hands-" + suffix);
            }
        }
        finally
        {
            layer.QueueFree(); window.Size = originalSize; await Wait();
        }
        MainFile.Logger.Info("REVISION_037_VISUAL_AUDIT_PASS nativeAnimation=true restShopConversion=true cards=5 relics=8 potions=3 powers=18 hands=4 resolutions=2");
    }
}
