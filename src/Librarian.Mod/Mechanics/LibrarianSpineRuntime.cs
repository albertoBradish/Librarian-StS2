using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace Librarian.Mechanics;

/// <summary>Opt-in ownership registry. A foreign animator is never intercepted.</summary>
internal static class LibrarianSpineRuntime
{
    private sealed class Registration(Node sprite, Creature creature, LibrarianSpinePoseDriver driver, LibrarianSpineNativeBinding binding)
    {
        internal readonly Node Sprite = sprite;
        internal readonly Creature Creature = creature;
        internal readonly LibrarianSpinePoseDriver Driver = driver;
        internal readonly LibrarianSpineNativeBinding Binding = binding;
        internal CreatureAnimator? Animator;
        internal bool Counted;
    }
    private sealed class Faulted;
    private static readonly ConditionalWeakTable<CreatureAnimator, Registration> Animators = new();
    private static readonly ConditionalWeakTable<CreatureAnimator, Faulted> FaultedAnimators = new();
    private static readonly ConditionalWeakTable<Creature, Registration> Creatures = new();
    private static readonly ConditionalWeakTable<Node, Registration> Sprites = new();
    private static LibrarianSpineBank? _bank;
    private static Func<LibrarianSpineSettings>? _settings;
    private static Action<Exception>? _logFault;
    private static Action<LibrarianSpineObservation>? _observe; // Null by default; private probe opts in.
    private static long _nativeOccurrence;
    private static int _registered;
    internal static event Action<LibrarianSpineVisualCue>? VisualCue;
    internal static int RegisteredDriverCount => Volatile.Read(ref _registered);

    internal static void Configure(LibrarianSpineBank bank, Func<LibrarianSpineSettings> settings,
        Action<Exception>? logFault = null, Action<LibrarianSpineObservation>? observation = null)
    {
        if (RegisteredDriverCount != 0) throw new InvalidOperationException("Configure the fixed bank before creating any drivers.");
        _bank = bank; _settings = settings; _logFault = logFault; _observe = observation;
    }

    // Called only by LibrarianCharacter.SetupCustomCreatureAnimator, after real Spine readiness.
    internal static CreatureAnimator CreateCombatAnimator(MegaSprite sprite)
    {
        LibrarianSpineCardExecution.AssertDispatchPatchVerified();
        var node = sprite.BoundObject as Node ?? throw new InvalidDataException("Owned combat Spine is not a Node.");
        var creatureNode = Ancestor<NCreature>(node) ?? throw new InvalidDataException("Owned Spine has no actual NCreature owner.");
        var creature = creatureNode.Entity;
        // The inert initial state avoids native initial idle random time/speed and has no queued next state.
        var animator = new CreatureAnimator(new AnimState("idle_still", false), sprite);
        foreach (var (trigger, clip) in new (string, string)[] {
            ("Idle", "idle_loop"), ("Relaxed", "relaxed_loop"), ("Attack", "attack"), ("Cast", "cast"),
            ("PowerUp", "power"), ("Hit", "hurt"), ("Dead", "die"), ("Revive", "revive") })
            animator.AddAnyState(trigger, new AnimState(clip, false));
        var registration = Create(node, creature, () => new LibrarianSpineContext(LibrarianSpineRoom.Combat, -1,
            creature.IsDead, IsLowHealth(creature)));
        registration.Animator = animator;
        Animators.Add(animator, registration);
        return animator;
    }

    // Root calls after native room initialization, retaining the direct first-child SpineSprite shape.
    internal static void AttachRoom(Node spineSprite, Player player, LibrarianSpineRoom room, int actIndex)
    {
        if (room == LibrarianSpineRoom.Combat) throw new ArgumentException("Use the native combat animator factory.");
        if (Sprites.TryGetValue(spineSprite, out _)) return;
        Create(spineSprite, player.Creature, () => new LibrarianSpineContext(room, actIndex, player.Creature.IsDead, IsLowHealth(player.Creature)));
    }

    private static Registration Create(Node sprite, Creature creature, Func<LibrarianSpineContext> context)
    {
        LibrarianSpineCardExecution.AssertDispatchPatchVerified();
        var bank = _bank ?? throw new InvalidOperationException("The exact exported Spine bank receipt was not configured.");
        var settings = _settings ?? throw new InvalidOperationException("Existing presentation settings were not bound.");
        Registration? registration = null;
        LibrarianSpineNativeBinding? binding = null;
        try
        {
            binding = new LibrarianSpineNativeBinding(sprite, bank, error =>
            {
                if (registration?.Animator is { } animator) FaultedAnimators.GetValue(animator, _ => new Faulted());
                ReportFault(error);
            }, () => { if (registration is not null) Release(registration); });
            var driver = new LibrarianSpinePoseDriver(binding, bank, settings, context, _observe,
                cue => { if (registration is not null) DispatchVisualCue(registration, cue); });
            registration = new Registration(sprite, creature, driver, binding);
            if (Sprites.TryGetValue(sprite, out _)) throw new InvalidOperationException("Two drivers attempted to own one SpineSprite.");
            Sprites.Add(sprite, registration);
            Creatures.Remove(creature); Creatures.Add(creature, registration);
            Interlocked.Increment(ref _registered); registration.Counted = true;
            binding.Connect(driver);
            driver.Initialize();
            return registration;
        }
        catch
        {
            if (registration is not null) registration.Driver.Dispose(); else binding?.Dispose();
            throw;
        }
    }
    private static void Release(Registration registration)
    {
        if (registration.Animator is { } animator) Animators.Remove(animator);
        if (Creatures.TryGetValue(registration.Creature, out var current) && ReferenceEquals(current, registration))
            Creatures.Remove(registration.Creature);
        Sprites.Remove(registration.Sprite);
        if (registration.Counted) { registration.Counted = false; Interlocked.Decrement(ref _registered); }
    }

    internal static bool TryHandleCard(CardPlay play, Player playingPlayer)
    {
        if (!Creatures.TryGetValue(playingPlayer.Creature, out var registration) || registration.Driver.IsDisposed) return false;
        var cell = LibrarianSpineCardExecution.Get(play);
        var profile = LibrarianSpineMotionProfiles.Resolve(play.Card);
        if (profile is null) return true;
        try
        {
            bool consumed = registration.Driver.ConsumeCard(play, cell.Id, profile.Semantic);
            if (consumed) cell.Handled(playingPlayer.Creature);
            return consumed;
        }
        catch (Exception error)
        {
            if (registration.Animator is { } animator) FaultedAnimators.GetValue(animator, _ => new Faulted());
            ReportFault(error);
            try { registration.Driver.Dispose(); } catch (Exception cleanupError) { ReportFault(cleanupError); }
            return true; // A presentation failure must not start a second legacy driver or alter native card execution.
        }
    }

    internal static bool TryGet(CreatureAnimator animator, out LibrarianSpinePoseDriver? driver)
    {
        driver = Animators.TryGetValue(animator, out var registration) ? registration.Driver : null;
        return driver is not null && !driver.IsDisposed;
    }
    internal static LibrarianSpinePoseDriver? Find(Node node)
    {
        if (Sprites.TryGetValue(node, out var registration) && !registration.Driver.IsDisposed) return registration.Driver;
        foreach (Node child in node.GetChildren()) if (Find(child) is { } found) return found;
        return null;
    }
    internal static LibrarianSpinePoseDriver? Find(Creature creature) => Creatures.TryGetValue(creature, out var r) && !r.Driver.IsDisposed ? r.Driver : null;

    internal static bool TryGetSocket(Creature creature, LibrarianSpineSocket socket, out LibrarianSpineSocketSample sample)
    {
        sample = default;
        return Creatures.TryGetValue(creature, out var registration) && !registration.Driver.IsDisposed
            && registration.Binding.TryGetSocket(socket, out sample);
    }
    internal static bool HasPendingVisual(CardPlay play) => Find(play.Card.Owner.Creature)?.HasPendingVisual(play) == true;
    internal static bool TryGetVisualPlay(CardModel source, out CardPlay? play)
    {
        play = null;
        if (Find(source.Owner.Creature)?.CurrentCardToken is not CardPlay current
            || !ReferenceEquals(current.Card, source)
            || !LibrarianSpineCardExecution.TryHandled(source.Owner.Creature, out long occurrence)
            || LibrarianSpineCardExecution.Get(current).Id != occurrence) return false;
        play = current;
        return true;
    }
    // Call only from a real result hook. An attempted/no-op Power CardPlay alone is not an established ability.
    internal static void NotifyVisualResult(CardModel source)
    {
        if (TryGetVisualPlay(source, out var play)) Find(source.Owner.Creature)?.ConfirmVisualResult(play!);
    }
    private static void DispatchVisualCue(Registration registration, LibrarianSpineMotionCue cue)
    {
        if (cue.Token is not CardPlay play || LibrarianSpineMotionProfiles.Resolve(play.Card) is not { } profile) return;
        LibrarianSpineSocketSample? socket = registration.Binding.TryGetSocket(profile.Socket, out var sample) ? sample : null;
        var visual = new LibrarianSpineVisualCue(registration.Creature, play, profile, cue.Stage,
            cue.Generation, cue.TagBits, cue.OccurrenceId, cue.Settings, socket);
        if (VisualCue is not { } listeners) return;
        // A single decorative listener failure must not suppress another listener or touch card execution.
        foreach (Action<LibrarianSpineVisualCue> listener in listeners.GetInvocationList())
            try { listener(visual); } catch (Exception error) { ReportFault(error); }
    }

    private static T? Ancestor<T>(Node node) where T : Node
    {
        for (Node? current = node; current is not null; current = current.GetParent()) if (current is T typed) return typed;
        return null;
    }
    private static bool IsLowHealth(Creature creature)
    {
        // Both fixed APIs expose these integer properties; use the same positive-HP threshold.
        return creature.CurrentHp > 0 && creature.MaxHp > 0 && (long)creature.CurrentHp * 4 <= creature.MaxHp;
    }
    private static void ReportFault(Exception error)
    {
        try { _logFault?.Invoke(error); } catch { } // A logging failure cannot change a native card/command Task.
    }
    private static readonly System.Reflection.FieldInfo CombatAnimatorField =
        AccessTools.Field(typeof(NCreature), "_spineAnimator")
        ?? throw new InvalidDataException("The fixed native creature animator field is unavailable.");
    private static bool HandleOwnedTrigger(Registration registration, string trigger)
    {
        bool merge = LibrarianSpineCardExecution.TryHandled(registration.Creature, out long occurrence);
        try
        {
            bool handled = registration.Driver.NativeTrigger(trigger, merge,
                merge ? occurrence : Interlocked.Decrement(ref _nativeOccurrence));
            return !handled;
        }
        catch (Exception error)
        {
            if (registration.Animator is { } animator) FaultedAnimators.GetValue(animator, _ => new Faulted());
            ReportFault(error);
            try { registration.Driver.Dispose(); } catch (Exception cleanupError) { ReportFault(cleanupError); }
            return false;
        }
    }
    // The native caller can already contain an inlined CreatureAnimator.SetTrigger when RitsuLib
    // patches it first. Consume our exact registered creature at the public native entry as well.
    [HarmonyPatch(typeof(NCreature), nameof(NCreature.SetAnimationTrigger))]
    private static class OwnedCreatureNativeTriggers
    {
        [HarmonyPriority(801)]
        private static bool Prefix(NCreature __instance, string trigger)
        {
            if (CombatAnimatorField.GetValue(__instance) is not CreatureAnimator animator) return true;
            if (FaultedAnimators.TryGetValue(animator, out _)) return false;
            if (!Animators.TryGetValue(animator, out var registration)
                || !ReferenceEquals(registration.Creature, __instance.Entity)
                || !ReferenceEquals(Ancestor<NCreature>(registration.Sprite), __instance)) return true;
            return HandleOwnedTrigger(registration, trigger);
        }
    }
    [HarmonyPatch(typeof(CreatureAnimator), nameof(CreatureAnimator.SetTrigger))]
    private static class OwnedNativeTriggers
    {
        private static bool Prefix(CreatureAnimator __instance, string trigger)
        {
            if (FaultedAnimators.TryGetValue(__instance, out _)) return false;
            if (!Animators.TryGetValue(__instance, out var registration)) return true;
            return HandleOwnedTrigger(registration, trigger);
        }
    }
}
