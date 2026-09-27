using BaseLib.Utils;
using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using static Librarian.Mechanics.LibrarianOrbAudio;

namespace Librarian.Mechanics;

/// <summary>Requires a real isolated combat with FMOD loaded. Does not claim human listening acceptance.</summary>
internal static class DevelopmentRevision0310AudioAudit
{
    private static void Require(bool pass, string message)
    {
        if (!pass) throw new InvalidOperationException("0310 audio: " + message);
        MainFile.Logger.Info("AUDIO_0310_CHECK_PASS " + message);
    }
    private static async Task Pause()
        => await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(0.60), SceneTreeTimer.SignalName.Timeout);

    internal static async Task Run(Player player, Func<Task> freshFight)
    {
        Require(OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase), "isolated profile");
        await freshFight(); await Pause();
        Require(Available, "real native FMOD command path available in combat");
        foreach (var (cue, sound) in Sounds)
        {
            Require(FmodAudio.EventExists(sound.Path), "loaded bank contains " + sound.Path);
            MainFile.Logger.Info($"AUDIO_0310_EVENT cue={cue} path={sound.Path} relativeVolume={sound.Volume} source=nativeBank");
        }
        Require(FmodAudio.BusExists("bus:/master") && FmodAudio.BusExists("bus:/master/sfx"), "native Master and SFX buses exist");
        float masterBefore = FmodAudio.GetBusVolume("bus:/master"), sfxBefore = FmodAudio.GetBusVolume("bus:/master/sfx");
        var gate = new Gate();
        Require(gate.Accept(Cue.Fire, 1000) && gate.Accept(Cue.Tide, 1000) && gate.Accept(Cue.Growth, 1000), "three distinct natural settlements are audible");
        Require(Enumerable.Range(0, 100).All(i => !gate.Accept(Cue.Fire, 1001 + i)), "100 repeated same-element requests suppressed");
        Require(!gate.Accept(Cue.Strengthen, 1399), "settlement-derived strengthening suppressed for 400 ms");
        Require(gate.Accept(Cue.Activate, 1400) && gate.Accept(Cue.Lock, 1600) && gate.Accept(Cue.Unlock, 1800), "state cues resume at bounded intervals");
        Require(!gate.Accept(Cue.Fire, 1900), "six-per-second overall ceiling");
        Require(gate.Accept(Cue.Fire, 2000), "burst capacity recovers without a timer");
        var stateGate = new Gate();
        Require(stateGate.Accept(Cue.Activate, 0) && !stateGate.Accept(Cue.Lock, 179) && stateGate.Accept(Cue.Lock, 180), "state-group 180 ms gate");
        Require(!stateGate.Accept(Cue.Activate, 349) && stateGate.Accept(Cue.Activate, 360), "same state cue minimum 350 ms");
        ResetAuditCounters();
        Require(!TryPlayValidatedEvent("event:/sfx/librarian/nonexistent-audit-event", 1), "unknown event silently skipped before native command");
        Require(!TryPlayValidatedEvent("event:/music/act1", 1) && !TryPlayValidatedEvent("", 1), "non-SFX and empty paths rejected");
        Require(Submitted == 0, "invalid requests submitted no sounds");
        foreach (var cue in Sounds.Keys)
        {
            await Pause(); Require(TryCue(cue), "submitted native one-shot " + cue);
            Require(!TryCue(cue), "immediate live duplicate suppressed " + cue);
        }
        Require(Submitted == Sounds.Count && Throttled == Sounds.Count, "live submission and suppression counts");
        Require(FmodAudio.GetBusVolume("bus:/master") == masterBefore && FmodAudio.GetBusVolume("bus:/master/sfx") == sfxBefore,
            $"playback preserves user bus levels master={masterBefore} sfx={sfxBefore}");
        var session = LibrarianRuntime.Get(player);
        var context = new ThrowingPlayerChoiceContext();
        await Pause(); ResetAuditCounters();
        await LibrarianRuntime.Dispatch(session, context, session.Orbs.Extinguish(OrbKind.Fire, OrbScope.All));
        await Pause(); ResetAuditCounters();
        var gain = session.Orbs.Gain(OrbKind.Fire, 3, new("audio-audit"));
        Require(gain.Events.Count > 1 && SelectOperation(gain) == Cue.Activate, "compound gain coalesces to activation cue");
        await LibrarianRuntime.Dispatch(session, context, gain);
        Require(Submitted == 1, "actual Dispatch hook submits one sound for compound gain");
        await Pause(); ResetAuditCounters();
        var before = session.Orbs.Snapshot();
        OnSettlement(session, new(session.Orbs.OwnerId, session.Orbs.OwnerTurn, OrbKind.Fire, 0, SettlementReason.Immediate, "audio-zero", 0, 1));
        Require(Submitted == 0, "zero settlement remains silent");
        Require(before.Orbs.SequenceEqual(session.Orbs.Snapshot().Orbs), "audio request does not mutate orb values or state");
        await LibrarianRuntime.Settle(session, context, new(session.Orbs.OwnerId, session.Orbs.OwnerTurn, OrbKind.Fire, 2, SettlementReason.Immediate, "audio-audit", 0, 1));
        Require(Submitted >= 1, "actual Settle hook submits fire audio");
        ResetAuditCounters();
        await freshFight();
        MainFile.Logger.Info("REVISION_0310_AUDIO_AUDIT_PASS nativeEvents=8 invalidSafe=true throttling=true busesUnchanged=true hooks=Dispatch,Settle listeningAcceptance=notPerformed");
    }
}
