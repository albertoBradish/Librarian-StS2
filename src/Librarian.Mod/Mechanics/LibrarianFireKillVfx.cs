using System.Reflection;
using Godot;
using HarmonyLib;
using Librarian.LibrarianCode;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;

namespace Librarian.Mechanics;

/// <summary>Observes only the direct HP result and confirmed death within one fire settlement.</summary>
internal static class LibrarianFireKillVfx
{
    private static readonly AsyncLocal<Observation?> Current = new();

    internal static IDisposable Observe(LibrarianSession session, Creature target, int value)
    {
        var observation = new Observation(session.Player, target, value, Current.Value);
        Current.Value = observation;
        return observation;
    }

    private sealed class Observation(Player owner, Creature target, int value, Observation? previous) : IDisposable
    {
        private bool _sawDamage;
        private bool _lethal;
        private bool _played;
        internal void Damage(DamageResult result)
        {
            if (_sawDamage || !ReferenceEquals(result.Receiver, target)) return;
            // Later damage from hooks cannot turn a nonlethal fireball into a kill.
            _sawDamage = true;
            _lethal = result.WasTargetKilled && result.UnblockedDamage > 0;
        }
        internal void Died(Creature creature)
        {
            if (!_lethal || _played || !ReferenceEquals(creature, target)) return;
            _played = true;
            Play(owner, creature, value);
        }
        public void Dispose() => Current.Value = previous;
    }

    [HarmonyPatch]
    private static class ObserveHpResult
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Creature), "LoseHpInternal");
        private static void Postfix(DamageResult __result) => Current.Value?.Damage(__result);
    }

    [HarmonyPatch]
    private static class ObserveConfirmedDeath
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Creature), "InvokeDiedEvent");
        // The original death command has approved death, but still owns the creature node.
        private static void Prefix(Creature __instance)
            => LibrarianOrbVfx.Safely(() => Current.Value?.Died(__instance));
    }

    private static void Play(Player owner, Creature target, int value)
    {
        var prefs = LibrarianPreferences050.Current;
        // The final enemy's HP is already zero. Combat's ending predicate may be
        // true here even though the native death command still owns its node.
        if (!prefs.OrbEffects || MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn || DisplayServer.GetName() == "headless"
            || SaveManager.Instance.PrefsSave.FastMode == FastModeType.Instant
            || (!prefs.TeammateEffects && !MegaCrit.Sts2.Core.Context.LocalContext.IsMe(owner))
            || LibrarianCardVfx050.Active >= prefs.EffectLimit) return;
        var room = NCombatRoom.Instance;
        var creatureNode = room?.GetCreatureNode(target);
        var hitbox = creatureNode?.Hitbox;
        if (room is null || !GodotObject.IsInstanceValid(room) || !room.IsInsideTree()
            || hitbox is null || !GodotObject.IsInstanceValid(hitbox)) return;
        var floor = creatureNode!.GetBottomOfHitbox();
        var center = hitbox.GetGlobalTransform() * (hitbox.Size / 2);
        float inverseScale = 1 / Math.Max(.01f, room.CombatVfxContainer.GetGlobalTransform().Scale.Abs().X);
        if (prefs.ReducedMotion || !prefs.Particles)
        {
            var compact = new LibrarianFireballVfx { EffectOwner = owner, SettlementValue = value, FinishImpactAfterCombat = true };
            room.CombatVfxContainer.AddChild(compact);
            compact.GlobalPosition = center;
            compact.Scale = Vector2.One * inverseScale;
            compact.CaptureDestination(center);
            compact.Impact();
            compact.Name = "LibrarianFireKillCompact";
            return;
        }
        // Runtime reference only: the original scene, shaders and textures stay in the game.
        // Use Cinder's native 0.75 scale. The kill flare is separate from projectile size.
        var burst = NFireBurstVfx.Create(floor, .75f);
        if (burst is null) return;
        var lifetime = new LibrarianFireKillBurst { EffectOwner = owner };
        lifetime.Scale = burst.Scale * inverseScale;
        // Do not enter the native controller: its Cmd.Wait can collapse to zero
        // after the last kill. Transfer its existing scene components unchanged,
        // then restart the original emitters under our bounded presentation clock.
        foreach (Node child in burst.GetChildren())
        {
            burst.RemoveChild(child);
            lifetime.AddChild(child);
        }
        burst.Free();
        room.CombatVfxContainer.AddChild(lifetime);
        lifetime.GlobalPosition = floor;
        MainFile.Logger.Info($"FIRE_ORB_KILL_VFX target={target.Monster?.GetType().Name} value={value}");
    }
}

/// <summary>Counts one native burst against the shared budget; owns its bounded cleanup.</summary>
public partial class LibrarianFireKillBurst : Node2D
{
    internal Player? EffectOwner { get; init; }
    private double _age;
    public override void _EnterTree() => LibrarianCardVfx050.Enter();
    public override void _ExitTree() => LibrarianCardVfx050.Exit();
    public override void _Ready() => RestartParticles(this);
    private static void RestartParticles(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is GpuParticles2D particles) particles.Restart();
            RestartParticles(child);
        }
    }
    public override void _Process(double delta)
    {
        _age += delta;
        var prefs = LibrarianPreferences050.Current;
        // Keep the final enemy's death burst visible through the transition to rewards.
        if (_age >= 2.1 || !prefs.OrbEffects || !prefs.Particles || prefs.ReducedMotion
            || SaveManager.Instance.PrefsSave.FastMode == FastModeType.Instant
            || (!prefs.TeammateEffects && EffectOwner is not null && !MegaCrit.Sts2.Core.Context.LocalContext.IsMe(EffectOwner)))
        {
            QueueFree();
            return;
        }
        Modulate = new Color(1, 1, 1, prefs.EffectOpacity / 100f);
    }
}
