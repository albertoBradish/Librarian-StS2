using System.IO;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace Librarian.Mechanics;

/// <summary>Same-process native presentation fixture; does not certify network synchronization.</summary>
internal static class DevelopmentRevision040RemoteVisualAudit
{
    private static void Require(bool pass, string message)
    {
        if (!pass) throw new InvalidOperationException("040 remote visual: " + message);
        MainFile.Logger.Info("VISUAL_040_REMOTE_CHECK_PASS " + message);
    }
    private static async Task Wait(double seconds = 0.45)
        => await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private static IEnumerable<T> Descendants<T>(Node root) where T : Node
    {
        foreach (var child in root.GetChildren())
        {
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static async Task Capture(string name)
    {
        await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
        var output = System.Environment.GetEnvironmentVariable("LIBRARIAN_VISUAL_OUTPUT")
            ?? @"D:\Slay The Spire_Mod Dev\.research\revision-v040-screenshots";
        Directory.CreateDirectory(output);
        Require(image.SavePng(Path.Combine(output, name + ".png")) == Error.Ok, "capture " + name);
    }
    internal static async Task Run(Player player)
    {
        if (!DevelopmentVisualAudit.Enabled) return;
        Require(OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase), "isolated profile");
        Require(DisplayServer.GetName() != "headless", "native rendering");
        var room = NCombatRoom.Instance!;
        var combat = (CombatState)player.Creature.CombatState!;
        var ally = Player.CreateForNewRun<LibrarianCharacter>(UnlockState.all, 940003);
        ally.RunState = player.RunState;
        ally.ResetCombatState();
        var window = NGame.Instance!.GetWindow();
        var originalSize = window.Size;
        NMultiplayerPlayerState? hud = null;
        try
        {
            combat.AddPlayer(ally);
            if (room.GetCreatureNode(ally.Creature) is null) room.AddCreature(ally.Creature);
            var actor = room.GetCreatureNode(player.Creature)!;
            var remote = room.GetCreatureNode(ally.Creature)!;
            remote.Position = actor.Position + new Vector2(430, -160);
            Require(!LocalContext.IsMe(ally), "fixture is non-local player");
            var session = LibrarianRuntime.Get(ally);
            session.Orbs.Gain(OrbKind.Tide, 23);
            session.Orbs.Lock(OrbKind.Growth);
            session.Waves.Add(17);
            LibrarianOrbPanel.Refresh(LibrarianRuntime.Get(player));
            var display = LibrarianOrbPanel.GetDisplay(session)!;
            // Real party-HUD scene invokes its normal NHealthBar.SetCreature path.
            hud = NMultiplayerPlayerState.Create(ally);
            NGame.Instance.AddChild(hud);
            hud.Position = new Vector2(25, 120);
            await Wait(1);
            foreach (var resolution in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                window.Size = resolution;
                await Wait();
                var expected = Math.Max(remote.Hitbox.Size.X / 2 + 28, remote.Hitbox.Size.Y * 0.30f + 48) * 0.75f;
                Require(Math.Abs(display.FormationRadius - expected) < 0.01f, "remote radius is native 75 percent " + resolution);
                var tide = display.GetNode<Control>(OrbKind.Tide.ToString());
                var growth = display.GetNode<Control>(OrbKind.Growth.ToString());
                var tideLabels = tide.GetChildren().OfType<Label>().ToArray();
                var growthLabels = growth.GetChildren().OfType<Label>().ToArray();
                Require(tideLabels.Length == 2 && growthLabels.Length == 2, "value and lock labels located");
                Require(!tideLabels[0].Visible && !growthLabels[1].Visible && tide.Modulate.A < 0.6f, "remote values hidden and dim at rest");
                Require(growth.GetChildren().OfType<TextureRect>().Last().Visible, "remote lock remains recognizable");
                float expectedScale = session.Orbs.Snapshot()[OrbKind.Tide].IsForeground
                    ? LibrarianOrbDisplay.ForegroundScale * 0.75f : LibrarianOrbDisplay.BackgroundScale * 0.75f;
                Require(Math.Abs(tide.Scale.X - expectedScale) < 0.01f, "remote sprite scale is 75 percent");
                var battlefieldBars = Descendants<NHealthBar>(remote).ToArray();
                Require(battlefieldBars.Length > 0 && battlefieldBars.Any(b => b.GetNodeOrNull<LibrarianWaveBar>("LibrarianWaves")?.IsVisibleInTree() == true), "battlefield Wave band visible for positive Waves");
                var hudBars = Descendants<NHealthBar>(hud).ToArray();
                Require(hudBars.Length > 0 && hudBars.All(b => b.GetNodeOrNull("LibrarianWaves") is null), "native party HUD has no duplicate Wave band");
                Require(Descendants<LibrarianWaterShield>(display).Any(s => s.IsVisibleInTree()), "positive Waves retain water shield");
                await Capture($"040-remote-idle-{resolution.X}x{resolution.Y}");
                tide.GrabFocus();
                await Wait(0.1);
                Require(tide.HasFocus() && tideLabels[0].Visible && tide.Modulate.A > 0.99f, "controller focus reveals remote value");
                growth.GrabFocus();
                await Wait(0.1);
                Require(!tideLabels[0].Visible && growthLabels[1].Visible, "focus moves details to locked remote orb");
                await Capture($"040-remote-focused-{resolution.X}x{resolution.Y}");
                growth.ReleaseFocus();
                await Wait(0.1);
                Require(!growthLabels[1].Visible, "focus exit hides lock number");
                tide.EmitSignal(Control.SignalName.MouseEntered);
                await Wait(0.1);
                Require(tideLabels[0].Visible, "mouse-enter signal reveals value");
                tide.EmitSignal(Control.SignalName.MouseExited);
                await Wait(0.1);
                Require(!tideLabels[0].Visible, "mouse-exit signal hides value");
            }
            Require(session.Waves.Amount == 17 && session.Orbs.Value(OrbKind.Tide) == 23, "presentation preserves Wave and orb values");
            MainFile.Logger.Info("VISUAL_040_REMOTE_AUDIT_PASS resolutions=2 sameProcess=True liveMulticlient=False");
        }
        finally
        {
            if (hud is not null && GodotObject.IsInstanceValid(hud)) { hud.GetParent()?.RemoveChild(hud); hud.QueueFree(); }
            window.Size = originalSize;
            combat.RemoveCreature(ally.Creature);
        }
    }
}
