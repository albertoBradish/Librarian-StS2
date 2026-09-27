using System.IO;
using Godot;
using Librarian.LibrarianCode;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.RestSite;

namespace Librarian.Mechanics;

internal static class DevelopmentRevision040VisualAudit
{
    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("040 visual audit: " + description);
        MainFile.Logger.Info("VISUAL_040_CHECK_PASS " + description);
    }
    private static async Task Wait(double seconds)
        => await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private static async Task Capture(string name)
    {
        await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
        string output = System.Environment.GetEnvironmentVariable("LIBRARIAN_VISUAL_OUTPUT")
            ?? @"D:\Slay The Spire_Mod Dev\.research\revision-v040-screenshots";
        Directory.CreateDirectory(output);
        Require(image.SavePng(Path.Combine(output, name + ".png")) == Error.Ok, "capture " + name);
    }
    internal static async Task Run(Player player)
    {
        if (!DevelopmentVisualAudit.Enabled) return;
        Require(OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase), "isolated profile");
        var creature = NCombatRoom.Instance!.GetCreatureNode(player.Creature)!;
        var motion = creature.Visuals.GetNode<LibrarianCharacterMotion>("Motion");
        var animation = motion.GetNode<AnimationPlayer>("AnimationPlayer");
        var visual = creature.Visuals.GetNode<Node2D>("Visuals");
        Require(motion.HasSeparateHands, "body and two independent hand textures mounted");
        Require(creature.Visuals.GetNodeOrNull<LibrarianGroundShadow>("LibrarianGroundShadow") is not null, "ground anchored shadow");
        var right = visual.GetNode<Sprite2D>("RightHand");
        var left = visual.GetNode<Sprite2D>("LeftHand");
        Require(right.Position.X < 0 && left.Position.X > 0, "anatomical right is viewer-left");
        Require(right.Texture.ResourcePath.EndsWith("/hand_left.png") && left.Texture.ResourcePath.EndsWith("/hand_right.png"), "corrected painted hand anatomy mapping");
        Require(!right.FlipH && !left.FlipH, "hand anatomy is not mirrored back");
        var rightRest = right.Position;
        var leftRest = left.Position;
        foreach (var type in new[] { CardType.Attack, CardType.Skill, CardType.Power })
        {
            LibrarianSpellVisuals.CardPlayed(player, type);
            await Wait(0.16);
            Require(type == CardType.Skill ? left.Position.DistanceTo(leftRest) > 5 : right.Position.DistanceTo(rightRest) > 5,
                "independent hand movement " + type);
            if (type == CardType.Attack) Require(left.Position.DistanceTo(leftRest) < 0.5f, "attack leaves left hand resting");
            if (type == CardType.Skill) Require(right.Position.DistanceTo(rightRest) < 0.5f, "skill leaves right hand resting");
            if (type == CardType.Power) Require(left.Position.DistanceTo(leftRest) > 5, "power moves both hands");
            await Capture("040-hand-" + type);
            await Wait(0.55);
            Require(right.Position.DistanceTo(rightRest) < 0.5f && left.Position.DistanceTo(leftRest) < 0.5f, "hands return " + type);
        }
        var oldDeathTask = creature.DeathAnimationTask;
        try
        {
            creature.StartDeathAnim(shouldRemove: false);
            await Wait(1.5);
            Require(visual.Modulate.A > 0.99f && visual.Modulate.R < 0.5f, "death keeps opaque dim corpse");
            Require(visual.Rotation < -1, "fallen body reaches ground pose");
            Require(animation.CurrentAnimation != "Idle", "death holds");
            await Capture("040-native-corpse");
            creature.StartReviveAnim();
            await Wait(0.95);
            Require(animation.CurrentAnimation == "Idle" && visual.Modulate == Colors.White, "revive restores visible idle");
            Require(Math.Abs(visual.Rotation) < 0.05f && visual.Position.Length() < 6, "revive restores rest transform");
            await Capture("040-native-revive");
        }
        finally
        {
            creature.StartReviveAnim();
            creature.DeathAnimationTask = oldDeathTask;
        }
        await CompareRest(player);
        MainFile.Logger.Info("VISUAL_040_AUDIT_PASS");
    }

    private static async Task CompareRest(Player player)
    {
        var game = NGame.Instance!;
        var layer = new CanvasLayer { Name = "Revision040RestComparison", Layer = 100 };
        game.AddChild(layer);
        var background = new ColorRect { Color = new Color("243038"), MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(background);
        var librarian = NRestSiteCharacter.Create(player, 0);
        // A native visual reference only: same Player provides act/animation context,
        // with no new character, run, progress or save created by this comparison.
        var native = GD.Load<PackedScene>("res://scenes/rest_site/characters/defect_rest_site.tscn").Instantiate<NRestSiteCharacter>();
        typeof(NRestSiteCharacter).GetProperty(nameof(NRestSiteCharacter.Player))!.SetValue(native, player);
        layer.AddChild(librarian);
        layer.AddChild(native);
        var heading = new Label { Text = "Librarian / native Defect — rest scene scale", MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(heading);
        var originalSize = game.GetWindow().Size;
        try
        {
            await Wait(0.1); // Motion attaches its ground shadow after the parent finishes mounting.
            var body = librarian.GetNode<Sprite2D>("ControlRoot/Visuals/Body");
            float displayedHeight = body.Texture.GetHeight() * body.Scale.Y;
            Require(displayedHeight >= 440 && displayedHeight <= 520, "rest body native-scale candidate " + displayedHeight);
            Require(librarian.GetNodeOrNull<LibrarianGroundShadow>("ControlRoot/LibrarianGroundShadow") is not null, "rest ground shadow");
            foreach (var resolution in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                game.GetWindow().Size = resolution;
                await Wait(0.35);
                var viewport = game.GetViewport().GetVisibleRect().Size;
                background.Size = viewport;
                heading.Position = new Vector2(40, 30);
                librarian.Position = new Vector2(viewport.X * 0.28f, viewport.Y * 0.87f);
                native.Position = new Vector2(viewport.X * 0.70f, viewport.Y * 0.87f);
                // Ground on the authored hitbox bottom, not the Spine root origin.
                native.Position -= new Vector2(0, native.Hitbox.GetGlobalRect().End.Y - viewport.Y * 0.87f);
                await Wait(0.3);
                await Capture($"040-rest-native-comparison-{resolution.X}x{resolution.Y}");
            }
        }
        finally
        {
            game.GetWindow().Size = originalSize;
            layer.QueueFree();
        }
    }
}
