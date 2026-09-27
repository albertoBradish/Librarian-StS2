using System.IO;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.Mechanics;

/// <summary>Native command/notification checks on a disposable non-local actor. Not a network test.</summary>
internal static class DevelopmentRevision040CombatVisualAudit
{
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("040 combat visual: " + message);
        MainFile.Logger.Info("VISUAL_040_COMBAT_CHECK_PASS " + message);
    }
    private static async Task Wait(double seconds)
        => await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
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
        Require(DisplayServer.GetName() != "headless" && player.Creature.IsAlive, "rendering with living local player");
        var combat = (CombatState)player.Creature.CombatState!;
        var room = NCombatRoom.Instance!;
        var fixture = Player.CreateForNewRun<LibrarianCharacter>(UnlockState.all, 940004);
        fixture.RunState = player.RunState;
        fixture.ResetCombatState();
        // Native damage/heal writes room-history statistics for every participating player.
        // This actor was added after room creation, so add its matching disposable row too.
        var history = fixture.RunState.CurrentMapPointHistoryEntry;
        Require(history is not null, "current room history exists");
        Require(history!.PlayerStats.All(p => p.PlayerId != fixture.NetId), "fixture history ID unused");
        var fixtureStats = new PlayerMapPointHistoryEntry { PlayerId = fixture.NetId };
        int shieldHits = 0;
        void Observe(Node node)
        {
            if (node is LibrarianSpellEffect { Effect: LibrarianSpellEffect.Kind.ShieldHit }) shieldHits++;
        }
        room.CombatVfxContainer.ChildEnteredTree += Observe;
        try
        {
            history.PlayerStats.Add(fixtureStats);
            combat.AddPlayer(fixture);
            if (room.GetCreatureNode(fixture.Creature) is null) room.AddCreature(fixture.Creature);
            var actor = room.GetCreatureNode(fixture.Creature)!;
            actor.Position = room.GetCreatureNode(player.Creature)!.Position + new Vector2(420, -100);
            Require(!LocalContext.IsMe(fixture), "disposable non-local actor");
            await Wait(0.4);
            async Task Hit(string label, int block, int damage, ValueProp props, bool expectFlash)
            {
                await Wait(0.2); // Outside the production 120ms flash throttle.
                fixture.Creature.LoseBlockInternal(fixture.Creature.Block);
                if (block > 0) await CreatureCmd.GainBlock(fixture.Creature, block, ValueProp.Unpowered, null);
                int prior = shieldHits;
                var results = (await CreatureCmd.Damage(new ThrowingPlayerChoiceContext(), fixture.Creature,
                    damage, props, player.Creature)).ToArray();
                Require(shieldHits - prior == (expectFlash ? 1 : 0), label + " actual hook shield count");
                if (expectFlash) Require(results.Any(r => r.BlockedDamage > 0 && r.UnblockedDamage == 0 && r.WasFullyBlocked), label + " actual fully blocked result");
            }
            await Hit("perfect block", 10, 5, ValueProp.Move | ValueProp.Unpowered, true);
            await Hit("partial block", 2, 5, ValueProp.Move | ValueProp.Unpowered, false);
            await Hit("zero damage", 10, 0, ValueProp.Move | ValueProp.Unpowered, false);
            await Hit("unblockable", 10, 2, ValueProp.Move | ValueProp.Unpowered | ValueProp.Unblockable, false);
            await Hit("non-attack damage", 10, 2, ValueProp.Unpowered, false);
            var visual = actor.Visuals.GetNode<Node2D>("Visuals");
            await CreatureCmd.Kill(fixture.Creature, force: true);
            await Wait(1.5);
            Require(fixture.Creature.IsDead && player.Creature.IsAlive, "native kill kills only fixture");
            Require(GodotObject.IsInstanceValid(actor) && actor.IsInsideTree() && visual.IsVisibleInTree(), "dead actor remains mounted and visible");
            Require(visual.Modulate.A > 0.99f && visual.Modulate.R < 0.5f && visual.Rotation < -1, "actual dead corpse holds opaque fallen pose");
            await Capture("040-actual-dead-player");
            await CreatureCmd.Heal(fixture.Creature, 20);
            await Wait(1.1);
            Require(fixture.Creature.IsAlive && visual.Modulate == Colors.White, "native heal revives actor and restores color");
            Require(Math.Abs(visual.Rotation) < 0.05f && visual.Position.Length() < 6, "actual revive restores rest pose");
            await Capture("040-actual-revived-player");
            MainFile.Logger.Info("VISUAL_040_COMBAT_AUDIT_PASS damageCases=5 nativeDeathRevive=True liveMulticlient=False");
        }
        finally
        {
            room.CombatVfxContainer.ChildEnteredTree -= Observe;
            try { combat.RemoveCreature(fixture.Creature); }
            finally { history.PlayerStats.Remove(fixtureStats); }
        }
    }
}
