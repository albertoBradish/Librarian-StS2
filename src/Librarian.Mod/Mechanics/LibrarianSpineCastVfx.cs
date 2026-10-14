using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace Librarian.Mechanics;

internal sealed record LibrarianSpineVfxObservation(string Stage, long Generation, long OccurrenceId,
    LibrarianSpineSocket Socket, Vector2 GlobalPosition, ulong SocketFrame, ulong Frame);

/// <summary>Decorative cues receive real entry identity and post-world positions; results stay native.</summary>
internal static class LibrarianSpineCastVfx
{
    private sealed class CastState
    {
        internal bool ResultObserved;
        internal bool Released;
        internal readonly List<LibrarianSpineSpellPulse> Preparation = [];
    }
    private static readonly ConditionalWeakTable<CardPlay, CastState> States = new();
    private static bool _subscribed;
    internal static Action<LibrarianSpineVfxObservation>? Observer;

    internal static void EnsureSubscribed()
    {
        if (_subscribed) return;
        LibrarianSpineRuntime.VisualCue += OnCue;
        _subscribed = true;
    }

    internal static bool OwnsCast(CardPlay play) => LibrarianSpineRuntime.Find(LibrarianGameApi.PlayingPlayer(play).Creature) is not null;

    internal static void ActualResult(CardModel? source) => LibrarianOrbVfx.Safely(() =>
    {
        if (source is null) return;
        LibrarianSpineRuntime.NotifyVisualResult(source);
        if (LibrarianSpineRuntime.TryGetVisualPlay(source, out var play) && play is not null)
            States.GetValue(play, _ => new CastState()).ResultObserved = true;
    });

    private static void OnCue(LibrarianSpineVisualCue cue) => LibrarianOrbVfx.Safely(() =>
    {
        if (cue.Stage is LibrarianSpineCueStage.Completed or LibrarianSpineCueStage.Cancelled)
        {
            if (States.TryGetValue(cue.Play, out var previous)) StopPreparation(previous);
            States.Remove(cue.Play);
            return;
        }
        var owner = cue.Creature.Player ?? cue.Play.Card.Owner;
        if (!LibrarianCardVfx050.Available(owner)
            || cue.Settings.ReducedMotion || cue.Settings.Speed == LibrarianSpineSpeed.Instant
            || !LibrarianCardVfx050.TryProfile(cue.Play.Card, out var profile) || cue.Socket is not { } socket) return;
        var state = States.GetValue(cue.Play, _ => new CastState());
        bool major = cue.Profile.Semantic == "power";
        bool fast = cue.Settings.Speed == LibrarianSpineSpeed.Fast;
        if (cue.Stage == LibrarianSpineCueStage.Prepare)
        {
            if (state.Preparation.Count != 0 || state.Released || state.ResultObserved) return;
            var effect = Spawn(cue.Creature, owner, profile, LibrarianSpineSpellStage.Prepare,
                socket.GlobalPosition, major ? 34 : 24, fast ? .19f : .34f, major, cue.Profile.Socket,
                cue.Generation, cue.TagBits, follow: true, socket.Frame);
            if (effect is not null) state.Preparation.Add(effect);
        }
        else if (cue.Stage == LibrarianSpineCueStage.Release && !state.Released)
        {
            state.Released = true;
            StopPreparation(state);
            // A result already on screen must not be followed by a projectile pretending to hit later.
            // Retain only a local fading glyph as residual energy in that case.
            var stage = state.ResultObserved ? LibrarianSpineSpellStage.Status : LibrarianSpineSpellStage.Release;
            var effect = Spawn(cue.Creature, owner, profile, stage, socket.GlobalPosition,
                state.ResultObserved ? (major ? 48 : 27) : (major ? 70 : 51),
                fast ? .27f : (major ? .65f : .46f), major, cue.Profile.Socket,
                cue.Generation, cue.TagBits, follow: false, socket.Frame);
            if (effect is not null && !major && !state.ResultObserved)
                Orient(effect, socket.GlobalDirection);
            if (major && !state.ResultObserved)
                foreach (var hand in new[] { LibrarianSpineSocket.ScreenLeft, LibrarianSpineSocket.ScreenRight })
                    if (LibrarianSpineRuntime.TryGetSocket(cue.Creature, hand, out var sample))
                        Spawn(cue.Creature, owner, profile, LibrarianSpineSpellStage.Status,
                            sample.GlobalPosition, 24, fast ? .23f : .4f, false, hand,
                            cue.Generation, cue.TagBits, follow: false, sample.Frame);
        }
        Observer?.Invoke(new(cue.Stage.ToString(),cue.Generation,cue.OccurrenceId,cue.Profile.Socket,
            socket.GlobalPosition,socket.Frame,Engine.GetProcessFrames()));
    });

    internal static bool TryResult(Creature target, Player? owner, SpellProfile050 profile, string stage,
        Vector2 position, float radius, out bool emitted)
    {
        emitted = false;
        if (owner is null || LibrarianSpineRuntime.Find(owner.Creature) is null
            || stage is not ("impact" or "block" or "status")) return false;
        var kind = stage == "impact" ? LibrarianSpineSpellStage.Impact
            : stage == "block" ? LibrarianSpineSpellStage.Block : LibrarianSpineSpellStage.Status;
        bool fast = LibrarianSpineIntegration.ReadSettings().Speed == LibrarianSpineSpeed.Fast;
        emitted = Spawn(target, owner, profile, kind, position, Math.Clamp(radius * .8f, 25, 65),
            fast ? .25f : .43f, false, LibrarianSpineSocket.Book, 0, 0, false, 0) is not null;
        return true; // Capacity/disabled presentation must not fall through into a duplicate old effect.
    }

    private static LibrarianSpineSpellPulse? Spawn(Creature actor, Player owner, SpellProfile050 profile,
        LibrarianSpineSpellStage stage, Vector2 globalPosition, float radius, float duration, bool major,
        LibrarianSpineSocket socket, long generation, int tag, bool follow, ulong socketFrame)
    {
        var room = NCombatRoom.Instance;
        if (room is null || !room.IsInsideTree() || !LibrarianCardVfx050.Available(owner)) return null;
        if (LibrarianCardVfx050.Active >= LibrarianPreferences050.Current.EffectLimit)
        { LibrarianCardVfx050.RecordDropped(); return null; }
        var layer = room.CombatVfxContainer;
        // Radius is authored in combat design units; keep viewport scaling exactly once.
        float actorScale = room.GetCreatureNode(actor)?.Hitbox?.GetGlobalTransform().Scale.Abs().X ?? 1;
        float layerScale = Math.Max(.01f, layer.GetGlobalTransform().Scale.Abs().X);
        var effect = new LibrarianSpineSpellPulse
        {
            Name = "LibrarianSpineSpellPulse", Profile = profile, Stage = stage, EffectOwner = owner,
            FollowCreature = actor, Socket = socket, Generation = generation, EntryTag = tag,
            FollowSocket = follow, Radius = radius, Duration = duration, Major = major,
            Scale = Vector2.One * actorScale / layerScale
        };
        layer.AddChild(effect);
        effect.GlobalPosition = globalPosition;
        effect.ObserveOrigin(globalPosition, socketFrame);
        return effect;
    }
    private static void Orient(Node2D effect, Vector2 globalDirection)
    {
        if (effect.GetParent() is not CanvasItem parent || globalDirection.LengthSquared() < .001f) return;
        var inverse = parent.GetGlobalTransform().AffineInverse();
        Vector2 local = inverse * (effect.GlobalPosition + globalDirection) - inverse * effect.GlobalPosition;
        effect.Rotation = local.Angle();
    }
    private static void StopPreparation(CastState state)
    {
        foreach (var effect in state.Preparation)
            if (GodotObject.IsInstanceValid(effect) && !effect.IsQueuedForDeletion()) effect.FadeOut();
        state.Preparation.Clear();
    }
}
