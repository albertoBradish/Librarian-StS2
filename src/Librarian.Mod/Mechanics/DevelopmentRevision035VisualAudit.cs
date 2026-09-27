using System.IO;
using Godot;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Assets;

namespace Librarian.Mechanics;

/// <summary>Opt-in native rendering checks, called only inside the disposable combat fixture.</summary>
internal static class DevelopmentRevision035VisualAudit
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("035 visual audit: " + message);
        MainFile.Logger.Info("VISUAL_035_CHECK_PASS " + message);
    }

    private static IEnumerable<Node> Descendants(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static async Task Wait(double seconds = 0.2)
        => await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private static async Task Capture(string directory, string filename)
    {
        await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
        Require(image != null && !image.IsEmpty(), "rendered viewport " + filename);
        var path = Path.Combine(directory, filename + ".png");
        Require(image!.SavePng(path) == Error.Ok, "saved " + filename);
        MainFile.Logger.Info($"VISUAL_035_SCREENSHOT path={path} dimensions={image.GetWidth()}x{image.GetHeight()}");
    }

    internal static async Task Run(Player player)
    {
        if (!DevelopmentVisualAudit.Enabled) return;
        Require(OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase), "isolated runtime profile");
        Require(DisplayServer.GetName() != "headless", "real rendering enabled");
        string? destination = System.Environment.GetEnvironmentVariable("LIBRARIAN_VISUAL_OUTPUT");
        Require(!string.IsNullOrWhiteSpace(destination) && Path.IsPathFullyQualified(destination), "absolute output supplied");
        string directory = Path.GetFullPath(destination!);
        const string workspace = @"D:\Slay The Spire_Mod Dev\";
        Require(directory.StartsWith(workspace + @".research\", StringComparison.OrdinalIgnoreCase)
            || directory.StartsWith(workspace + @"outputs\", StringComparison.OrdinalIgnoreCase), "workspace screenshot destination");
        Directory.CreateDirectory(directory);
        Require(player.Character is LibrarianCharacter, "Librarian player");
        Require(player.Character.MapDrawingColor.IsEqualApprox(new Color("EBA466")), "map drawing #EBA466");
        Require(player.Character.RelicPool.LabOutlineColor.IsEqualApprox(new Color("EBA466")), "relic outline #EBA466");
        Require(player.Character.PotionPool.LabOutlineColor.IsEqualApprox(new Color("EBA466")), "potion outline #EBA466");
        Require(player.Character.CardPool.FrameMaterial is ShaderMaterial material
            && material.Shader.ResourcePath.EndsWith("v0.3.5/card_frame_orange.gdshader"), "native frame uses dedicated orange shader");

        var counter = Descendants(NGame.Instance!).OfType<NEnergyCounter>()
            .Single(n => n.IsVisibleInTree());
        await Wait(0.5); // First combat after menu/resolution changes needs native layout to settle.
        MainFile.Logger.Info($"VISUAL_035_COUNTER_LAYOUT size={counter.Size} scale={counter.Scale}");
        var triangle = counter.GetNode<TextureRect>("Layers/Triangle");
        Require(counter.Name == "LibrarianEnergyCounter", "mounted counter is converted custom scene");
        Require(triangle.Texture.ResourcePath == LibrarianVisualTheme.BigEnergyIconPath, "mounted counter uses triangle texture");
        Require(counter.GetNode<Control>("%RotationLayers").GetChildCount() == 0, "inverted triangle does not rotate");
        Require(counter.Size.IsEqualApprox(new Vector2(128, 128)), "counter retains native 128-pixel footprint");
        Require(triangle.Texture.GetWidth() == 128 && triangle.Texture.GetHeight() == 128, "energy SVG imported 128x128");
        using (var pixels = triangle.Texture.GetImage())
            Require(pixels.GetPixel(0, 0).A == 0, "energy icon corner actually transparent");
        var textTexture = GD.Load<Texture2D>(LibrarianVisualTheme.TextEnergyIconPath);
        Require(textTexture.GetWidth() == 32 && textTexture.GetHeight() == 32, "inline energy SVG imported 32x32");

        var pool = player.Character.CardPool.AllCards;
        var specimens = new (CardModel Model, bool Upgraded)[]
        {
            (ModelDb.Card<Spark>(), false), (ModelDb.Card<Spark>(), true),
            (ModelDb.Card<AncientSpark>(), false), (ModelDb.Card<AncientSpark>(), true),
            (pool.First(c => c.EnergyCost.CostsX), false),
            (pool.First(c => c.Rarity == CardRarity.Uncommon), false),
            (pool.First(c => c.Rarity == CardRarity.Rare), false)
        };
        var layer = new CanvasLayer { Name = "Revision035VisualSamples", Layer = 100 };
        NGame.Instance!.AddChild(layer);
        var cards = new List<NCard>();
        int originalEnergy = player.PlayerCombatState!.Energy;
        var window = NGame.Instance.GetWindow();
        var originalSize = window.Size;
        try
        {
            foreach (var (canonical, upgraded) in specimens)
            {
                var model = player.Creature.CombatState!.CreateCard(canonical, player);
                if (upgraded) { model.UpgradeInternal(); model.FinalizeUpgradeInternal(); }
                // Instantiate directly so removing the fixture cannot affect the shared card node pool.
                var card = PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>();
                layer.AddChild(card);
                card.Model = model;
                card.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
                cards.Add(card);
                Require(card.GetNode<TextureRect>("%EnergyIcon").Texture.ResourcePath == LibrarianVisualTheme.BigEnergyIconPath,
                    "native card triangle " + model.Id + " upgraded=" + upgraded);
                Require(card.GetNode<TextureRect>("%EnergyIcon").Size.IsEqualApprox(new Vector2(64, 64)),
                    "card icon retains 64-pixel slot " + model.Id);
                MainFile.Logger.Info($"VISUAL_035_CARD id={model.Id} upgraded={upgraded} cost={card.GetNode<Label>("%EnergyLabel").Text}");
            }
            foreach (var resolution in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                window.Size = resolution;
                await Wait(0.5);
                var view = NGame.Instance.GetViewport().GetVisibleRect().Size;
                for (int i = 0; i < cards.Count; i++)
                {
                    cards[i].Position = new Vector2(view.X * ((i % 4 + 0.5f) / 4f), view.Y * (i < 4 ? 0.28f : 0.65f));
                    cards[i].Scale = Vector2.One * Math.Min(0.70f, view.X / 2400f);
                }
                await PlayerCmd.SetEnergy(3, player);
                await Wait();
                Require(counter.GetNode<Label>("Label").Text.StartsWith("3/"), "live positive energy label");
                await Capture(directory, $"035-energy-cards-positive-{resolution.X}x{resolution.Y}");
                await PlayerCmd.SetEnergy(0, player);
                await Wait();
                Require(counter.GetNode<Label>("Label").Text.StartsWith("0/"), "live depleted energy label");
                Require(triangle.Material != null, "native depleted-energy material applied");
                await Capture(directory, $"035-energy-cards-depleted-{resolution.X}x{resolution.Y}");
            }
        }
        finally
        {
            layer.QueueFree();
            window.Size = originalSize;
            await PlayerCmd.SetEnergy(originalEnergy, player);
            await Wait();
        }
        MainFile.Logger.Info("REVISION_035_VISUAL_AUDIT_PASS energyCounter=true cards=7 resolutions=2 energyStates=2");
    }
}
