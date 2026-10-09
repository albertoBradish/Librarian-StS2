using Godot;
using Librarian.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.TestSupport;

namespace Librarian.Mechanics;

/// <summary>Cosmetic-only native FMOD references. No banks, loops, timers, or gameplay RNG.</summary>
internal static class LibrarianOrbAudio
{
    internal enum Cue { Fire, Tide, Growth, Activate, Strengthen, Lock, Unlock, Extinguish }
    internal sealed record Sound(string Path, float Volume);
    internal static readonly IReadOnlyDictionary<Cue, Sound> Sounds = new Dictionary<Cue, Sound>
    {
        [Cue.Fire] = new("event:/sfx/characters/attack_fire", 0.55f),
        [Cue.Tide] = new("event:/sfx/characters/defect/defect_frost_channel", 0.35f),
        [Cue.Growth] = new("event:/sfx/enemy/enemy_attacks/scroll_of_biting/scroll_of_biting_buff", 0.40f),
        [Cue.Activate] = new("event:/sfx/ui/enchant_shimmer", 0.30f),
        [Cue.Strengthen] = new("event:/sfx/buff", 0.22f),
        [Cue.Lock] = new("event:/sfx/debuff", 0.30f),
        [Cue.Unlock] = new("event:/sfx/ui/gain_energy", 0.30f),
        [Cue.Extinguish] = new("event:/sfx/ui/cards/card_movement_B_power", 0.25f)
    };
    internal static bool IsSettlement(Cue cue) => cue is Cue.Fire or Cue.Tide or Cue.Growth;

    // One gate for the local mix, rather than one per player: copied multiplayer effects cannot stack identical sounds.
    internal sealed class Gate
    {
        private readonly Dictionary<Cue, long> _last = new();
        private readonly Queue<long> _recent = new();
        private long _lastState = long.MinValue / 2;
        private long _lastSettlement = long.MinValue / 2;
        internal bool Accept(Cue cue, long now)
        {
            while (_recent.TryPeek(out long prior) && now - prior >= 1000) _recent.Dequeue();
            bool settle = IsSettlement(cue);
            if (_last.TryGetValue(cue, out long same) && now - same < (settle ? 220 : 350)) return false;
            if (!settle && (now - _lastState < 180 || now - _lastSettlement < 400)) return false;
            if (_recent.Count >= 6) return false;
            _last[cue] = now; _recent.Enqueue(now);
            if (settle) _lastSettlement = now; else _lastState = now;
            return true;
        }
    }
    private static Gate _gate = new();
    internal static long Submitted { get; private set; }
    internal static long Throttled { get; private set; }
    internal static bool Available => !TestMode.IsOn && !NonInteractiveMode.IsActive &&
        NAudioManager.Instance is not null && CombatManager.Instance.IsInProgress && !CombatManager.Instance.IsOverOrEnding;

    internal static Cue? SelectOperation(OrbOperationResult operation)
    {
        if (operation.Status != OrbOperationStatus.Applied) return null;
        var events = operation.Events;
        // Gaining an orb emits Gained, Strengthened and Activated. Only its most meaningful state is sounded.
        if (events.Any(e => e.Kind == OrbEventKind.Locked)) return Cue.Lock;
        if (events.Any(e => e.Kind == OrbEventKind.Unlocked)) return Cue.Unlock;
        if (events.Any(e => e.Kind == OrbEventKind.Extinguished)) return Cue.Extinguish;
        if (events.Any(e => e.Kind == OrbEventKind.Activated)) return Cue.Activate;
        if (events.Any(e => (e.Kind is OrbEventKind.Gained or OrbEventKind.Strengthened) && e.ActualAmount > 0)) return Cue.Strengthen;
        return null; // Loss, zeroing, permutation and every display refresh stay silent.
    }
    internal static void OnOperation(LibrarianSession session, OrbOperationResult operation)
    {
        if (session.Player.Creature.IsDead || session.Player.Creature.CombatState is null) return;
        if (SelectOperation(operation) is { } cue) TryCue(cue);
    }
    internal static void OnSettlement(LibrarianSession session, SettlementRequest request)
    {
        if (request.EffectAmount() <= 0 || session.Player.Creature.IsDead || session.Player.Creature.CombatState is null) return;
        TryCue(request.Orb switch { OrbKind.Fire => Cue.Fire, OrbKind.Tide => Cue.Tide, OrbKind.Growth => Cue.Growth, _ => throw new ArgumentOutOfRangeException() });
    }
    internal static bool TryCue(Cue cue)
    {
        try
        {
            if (!Available || !Sounds.TryGetValue(cue, out var sound) || !LibrarianNativeAudioQuery.EventExists(sound.Path)) return false;
            if (!_gate.Accept(cue, (long)Time.GetTicksMsec())) { Throttled++; return false; }
            return TryPlayValidatedEvent(sound.Path, sound.Volume);
        }
        catch { return false; }
    }
    internal static bool TryPlayValidatedEvent(string path, float volume)
    {
        // Native SfxCmd -> NAudioManager -> AudioManagerProxy. User Master/SFX bus levels remain authoritative.
        try
        {
            if (!LibrarianPreferences050.Current.OrbSounds || LibrarianPreferences050.Current.SoundVolume == 0) return false;
            if (!Available || string.IsNullOrWhiteSpace(path) || !path.StartsWith("event:/sfx/", StringComparison.Ordinal) || !LibrarianNativeAudioQuery.EventExists(path)) return false;
            SfxCmd.Play(path, Math.Clamp(volume, 0f, 1f) * LibrarianPreferences050.Current.SoundVolume / 100f); Submitted++; return true;
        }
        catch { return false; } // Query and playback failures must never interrupt card resolution.
    }
    private static long _lastPreview = long.MinValue / 2;
    internal static bool TryPreview(Cue cue)
    {
        try
        {
            // Explicit settings action can play outside combat; the normal combat gate stays unchanged.
            if (TestMode.IsOn || NonInteractiveMode.IsActive || NAudioManager.Instance is null ||
                !LibrarianPreferences050.Current.OrbSounds || LibrarianPreferences050.Current.SoundVolume == 0 ||
                !Sounds.TryGetValue(cue, out var sound) || !LibrarianNativeAudioQuery.EventExists(sound.Path)) return false;
            long now = (long)Time.GetTicksMsec();
            if (now - _lastPreview < 350) return false;
            _lastPreview = now;
            SfxCmd.Play(sound.Path, sound.Volume * LibrarianPreferences050.Current.SoundVolume / 100f);
            Submitted++;
            return true;
        }
        catch { return false; }
    }
    internal static void ResetAuditCounters() { _gate = new Gate(); Submitted = 0; Throttled = 0; }
}
