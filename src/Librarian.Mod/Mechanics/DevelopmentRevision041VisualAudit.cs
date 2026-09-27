using System.IO;
using System;
using Godot;
using Librarian.LibrarianCode;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Nodes.Cards;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.OrbBasics;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Cards.OrbAdvancedBasics;
using Librarian.LibrarianCode.Cards.Stateful;

namespace Librarian.Mechanics;

/// <summary>
/// V0.4.1 visual checks for the native combat creature and the layered character
/// selection scene. Invoke only from the non-headless isolated visual harness.
/// </summary>
internal static class DevelopmentRevision041VisualAudit
{
    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("041 visual audit: " + description);
        MainFile.Logger.Info("VISUAL_041_CHECK_PASS " + description);
    }

    private static async Task Wait(double seconds = 0.16)
        => await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private static string Output()
    {
        string output = System.Environment.GetEnvironmentVariable("LIBRARIAN_VISUAL_OUTPUT")
            ?? @"D:\Slay The Spire_Mod Dev\.research\revision-v0.4.1-visual";
        string full = Path.GetFullPath(output);
        Require(Path.IsPathFullyQualified(output) &&
            (full.StartsWith(@"D:\Slay The Spire_Mod Dev\.research\", StringComparison.OrdinalIgnoreCase) ||
             full.StartsWith(@"D:\Slay The Spire_Mod Dev\outputs\", StringComparison.OrdinalIgnoreCase)),
            "visual output stays in the workspace research/output area");
        Directory.CreateDirectory(full);
        return full;
    }

    private static async Task Capture(string name)
    {
        await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image image = NGame.Instance.GetViewport().GetTexture().GetImage();
        Require(image.SavePng(Path.Combine(Output(), name + ".png")) == Error.Ok, "capture " + name);
    }

    internal static async Task Run(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (!DevelopmentVisualAudit.Enabled) return;
        Require(OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase),
            "isolated profile");
        Require(DisplayServer.GetName() != "headless", "native renderer");
        await Wait(3.0); // Let the native battle-start banner and darkening clear.
        var creature = NCombatRoom.Instance!.GetCreatureNode(player.Creature)!;
        var motion = creature.Visuals.GetNode<LibrarianCharacterMotion>("Motion");
        var visual = motion.GetParent().GetNode<Node2D>(motion.VisualPath);
        Require(motion.HasSeparateHands, "two independent combat hands");
        var right = visual.GetNode<Sprite2D>("RightHand");
        var left = visual.GetNode<Sprite2D>("LeftHand");
        Require(right.Position.X < 0 && left.Position.X > 0, "right hand screen-left and left hand screen-right");
        Require(right.Texture is not null && left.Texture is not null &&
            right.Texture.ResourcePath.EndsWith("/hand_left.png") && left.Texture.ResourcePath.EndsWith("/hand_right.png"),
            "painted hand textures retain corrected anatomy mapping");
        Require(right.GetNodeOrNull<LibrarianHandGlow>("SpellGlow") is not null &&
            left.GetNodeOrNull<LibrarianHandGlow>("SpellGlow") is not null, "both hands have independent glow nodes");
        var rightRest = right.Position;
        var leftRest = left.Position;
        foreach (CardType type in new[] { CardType.Skill, CardType.Attack, CardType.Power })
        {
            LibrarianSpellVisuals.CardPlayed(player, type);
            await Wait();
            if (type == CardType.Skill)
            {
                Require(right.Position.DistanceTo(rightRest) > 5 && left.Position.DistanceTo(leftRest) < 0.5f,
                    "Skill animates screen-left hand only");
            }
            else if (type == CardType.Attack)
            {
                Require(left.Position.DistanceTo(leftRest) > 5 && right.Position.DistanceTo(rightRest) < 0.5f,
                    "Attack animates screen-right hand only");
            }
            else
            {
                Require(right.Position.DistanceTo(rightRest) > 5 && left.Position.DistanceTo(leftRest) > 5,
                    "Power animates both hands");
            }
            await Capture("041-hand-" + type);
            await Wait(0.55);
            Require(right.Position.DistanceTo(rightRest) < 0.5f && left.Position.DistanceTo(leftRest) < 0.5f,
                "hands return after " + type);
        }
        motion.PlayCardGesture(CardType.Skill);
        await Wait(0.20);
        var interrupted = right.Position;
        motion.PlayCardGesture(CardType.Attack);
        Require(right.Position == interrupted, "rapid next card does not snap the prior hand to rest");
        await Wait(0.65);
        Require(right.Position.DistanceTo(rightRest) < 0.5f && left.Position.DistanceTo(leftRest) < 0.5f,
            "both interrupted hands settle back to rest");
        await CaptureCards(player);
        await CapturePowerIcons();
    }

    private static async Task CaptureCards(Player player)
    {
        var layer = new CanvasLayer { Layer = 120 };
        NGame.Instance!.AddChild(layer);
        var view = NGame.Instance.GetViewport().GetVisibleRect().Size;
        var panel = new Control();
        layer.AddChild(panel);
        panel.AddChild(new ColorRect { Color = new Color("20272e"), Size = view });
        var models = new CardModel[] { ModelDb.Card<ReadBackward>(), ModelDb.Card<FuelTheFire>(),
            ModelDb.Card<Rekindle>(), ModelDb.Card<SeaBurial>(), ModelDb.Card<Calibrate>(), ModelDb.Card<EmberReckoning>(),
            ModelDb.Card<ArchiveBulwark>() };
        try
        {
            for (int page = 0; page < 2; page++)
            {
                var cards = new List<NCard>();
                var samples = models.Skip(page * 4).Take(4).ToArray();
                for (int i = 0; i < samples.Length; i++)
                {
                    var card = PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>();
                    panel.AddChild(card);
                    card.Model = player.Creature.CombatState!.CreateCard(samples[i], player);
                    card.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
                    float scale = Math.Min(view.X / (samples.Length * 340f), view.Y / 530f);
                    card.Scale = Vector2.One * scale;
                    card.Position = new Vector2(view.X * (i + 0.5f) / samples.Length, view.Y * 0.5f);
                    cards.Add(card);
                }
                await Wait(0.20);
                await Capture("041-card-text-and-art-page" + page);
                foreach (var card in cards)
                {
                    card.Model.UpgradeInternal();
                    card.Model.FinalizeUpgradeInternal();
                    card.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
                }
                await Wait(0.10);
                await Capture("041-card-text-and-art-upgraded-page" + page);
                foreach (var card in cards) { panel.RemoveChild(card); card.QueueFree(); }
            }
        }
        finally { layer.QueueFree(); await Wait(0.05); }
    }

    private static async Task CapturePowerIcons()
    {
        string[] names = ["ancient_catalog", "blazing_chapter", "crowd_kindling", "ember_bookmark", "eternal_grimoire",
            "fuel_the_fire", "lifeline", "overfishing", "overlimit_form", "power", "ridge_ward", "ring_curriculum",
            "sedimentation", "selective_overfishing", "shifting_pages", "spacetime_twist", "thorn_burst",
            "tidal_block_status", "tidal_mark", "unretreating_tide", "water_spirit"];
        var layer = new CanvasLayer { Layer = 120 };
        NGame.Instance!.AddChild(layer);
        var view = NGame.Instance.GetViewport().GetVisibleRect().Size;
        var panel = new Control();
        layer.AddChild(panel);
        panel.AddChild(new ColorRect { Color = new Color("20272e"), Size = view });
        try
        {
            var cell = new Vector2(view.X / 7f, view.Y / 3f);
            for (int i = 0; i < names.Length; i++)
            {
                var origin = new Vector2(i % 7 * cell.X, i / 7 * cell.Y);
                panel.AddChild(new TextureRect { Texture = GD.Load<Texture2D>($"res://Librarian/images/powers/v0.4.1/{names[i]}.png"),
                    Position = origin + new Vector2(16, 28),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Size = new Vector2(64, 64) });
                panel.AddChild(new TextureRect { Texture = GD.Load<Texture2D>($"res://Librarian/images/powers/v0.4.1/big/{names[i]}.png"),
                    Position = origin + new Vector2(88, 15),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Size = new Vector2(120, 120) });
                var label = new Label { Text = names[i], Position = origin + new Vector2(12, 152) };
                label.AddThemeFontSizeOverride("font_size", 17);
                panel.AddChild(label);
            }
            await Wait(0.15);
            await Capture("041-status-icons-native-import");
        }
        finally { layer.QueueFree(); await Wait(0.05); }
    }

    internal static Task RunSelection(NCharacterSelectScreen screen)
        => DevelopmentSelection050Audit.Run(screen);

    private static IEnumerable<Node> Descendants(Node node)
    {
        yield return node;
        foreach (Node child in node.GetChildren())
            foreach (Node nested in Descendants(child))
                yield return nested;
    }
}
