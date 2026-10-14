using System.Runtime.CompilerServices;

namespace Librarian.Mechanics;

internal interface ILibrarianSpineEntry : IDisposable
{
    int Track { get; }
    string Name { get; }
    float Duration { get; }
    float TrackTime { get; set; }
    float MixTime { get; }
    float MixDuration { get; set; }
    float TimeScale { get; set; }
    int TagBits { get; set; }
    bool IsComplete { get; }
    void DisableMixingCallbacks();
}
internal interface ILibrarianSpineAttachment : IDisposable
{
    string SlotName { get; }
    void Restore();
}
internal interface ILibrarianSpineBinding : IDisposable
{
    ILibrarianSpineEntry SetAnimation(string name, bool loop, int track);
    int? CurrentTag(int track);
    IReadOnlyList<ILibrarianSpineAttachment> Capture(IReadOnlyList<string> slots);
    void ApplyZero(Action afterApply);
    void DisconnectListeners();
    void ClearTrackIfOwned(int track, int tagBits);
}
internal enum LibrarianSpinePose { RoomIdle, Card, Hurt, Dying, Dead, Reviving }
internal enum LibrarianSpineCueStage { Prepare, Release, Completed, Cancelled }
internal sealed record LibrarianSpineMotionCue(object Token, LibrarianSpineCueStage Stage, string Semantic,
    long Generation, int TagBits, long OccurrenceId, LibrarianSpineSettings Settings);
internal sealed record LibrarianSpineObservation(string Kind, long Generation, int TagBits, string Animation,
    long OccurrenceId, string Detail);
internal sealed record LibrarianSpineDiagnostics(string BodyClip, string ExpressionClip, long Generation,
    int BodyTagBits, int ExpressionTagBits, IReadOnlyList<string> HeldSlots, LibrarianSpinePose Pose,
    bool DeadLatch, bool IsDisposed, long Accepted, long Suppressed, long Deduped, long Completed, long Stale);

/// <summary>One presentation state machine per real SpineSprite. No gameplay calls, waits, RNG or sounds.</summary>
internal sealed class LibrarianSpinePoseDriver : IDisposable
{
    private sealed class Seen { internal bool Handled; }
    private sealed record Pending(long Generation, int TagBits, string Name, int Track)
    {
        internal bool Started;
    }
    private sealed record Hold(ILibrarianSpineAttachment Attachment, float? FirstKey);
    private readonly ILibrarianSpineBinding _binding;
    private readonly LibrarianSpineBank _bank;
    private readonly Func<LibrarianSpineSettings> _readSettings;
    private readonly Func<LibrarianSpineContext> _readContext;
    private readonly Action<LibrarianSpineObservation>? _observe;
    private readonly Action<LibrarianSpineMotionCue>? _visualCue;
    private readonly ConditionalWeakTable<object, Seen> _occurrences = new();
    private readonly List<Hold> _holds = [];
    private readonly Queue<(long Generation, int Tag, string Event)> _events = new();
    private readonly HashSet<(string Name, int Time)> _consumedEvents = [];
    private ILibrarianSpineEntry? _body;
    private ILibrarianSpineEntry? _expressionEntry;
    private Pending? _pending;
    private LibrarianSpineClip? _clip;
    private LibrarianSpineSettings _settings;
    private string _semantic = "idle_still";
    private string _expression = "expression_dot";
    private int _bodyTag;
    private int _expressionTag;
    private long _generation;
    private long _occurrenceId;
    private long? _completeGeneration;
    private long _completionConsumed = -1;
    private long _accepted, _suppressed, _deduped, _completed, _stale;
    private bool _muteCosmeticEvents;
    private bool _disposed;
    private bool _deadLatch;
    private bool _foreignEntry;
    private float _lastTrackTime;
    private bool _insideMutation;
    private bool _hasVisibleBody;
    private object? _cardToken;
    private bool _preparePending;
    private bool _releaseSent;
    private bool _returnPending;
    private float _expressionUntil = -1;
    private bool _resultConfirmed;
    private bool _expressionReduced;
    private string? _pendingExpression;
    private bool _restoreExpressionPending;
    private bool _drawPending;
    private bool _victorious;
    internal LibrarianSpinePose Pose { get; private set; } = LibrarianSpinePose.RoomIdle;
    internal bool IsDisposed => _disposed;
    internal bool DeadLatch => _deadLatch;
    internal long Generation => _generation;
    internal int BodyTag => _bodyTag;
    internal string BodySemantic => _semantic;
    internal bool HasPendingVisual(object token) => !_disposed && ReferenceEquals(_cardToken, token);
    internal object? CurrentCardToken => _cardToken;
    internal void CardDrawn()
    {
        if (_disposed || _deadLatch || _readContext().Room != LibrarianSpineRoom.Combat) return;
        // One real batch gets one flip. Draws during cast_draw are already shown by its page hinge.
        if (_semantic is "cast_draw" or "book_flip") return;
        var settings = _readSettings();
        if (settings.ReducedMotion || settings.Speed == LibrarianSpineSpeed.Instant) return;
        _drawPending = true;
    }
    internal void CombatVictory()
    {
        if (_disposed || _deadLatch || _readContext().Room != LibrarianSpineRoom.Combat) return;
        _victorious = true;
        _resultConfirmed = false;
        _drawPending = false;
        _pendingExpression = "expression_heart";
        _expressionUntil = -1;
    }
    internal void ConfirmVisualResult(object token)
    {
        if (HasPendingVisual(token)) _resultConfirmed = true;
    }
    internal void MerchantPurchaseConfirmed(long occurrenceId)
    {
        if (_disposed) return;
        BeforeUpdate();
        var context = _readContext();
        if (context.Room != LibrarianSpineRoom.Merchant || context.IsDead || _deadLatch) return;
        if (!_settings.ReducedMotion && _settings.Speed != LibrarianSpineSpeed.Instant)
        {
            Pose = LibrarianSpinePose.Card;
            _accepted++;
            StartBody("cast_draw", occurrenceId);
        }
        // The reduced preference suppresses the gesture; a brief eye glyph does not move the body.
        SetExpression("expression_check");
        _expressionUntil = (_body?.TrackTime ?? 0) + .32f;
        Emit("merchant_purchase_confirmed", "actual successful native purchase/removal");
    }
    internal LibrarianSpineDiagnostics Diagnostics() => new(_clip?.Name ?? "",
        _expressionEntry?.Name ?? "", _generation, _bodyTag, _expressionTag,
        _holds.Select(h => h.Attachment.SlotName).ToArray(), Pose, _deadLatch, _disposed,
        _accepted, _suppressed, _deduped, _completed, _stale);

    internal LibrarianSpinePoseDriver(ILibrarianSpineBinding binding, LibrarianSpineBank bank,
        Func<LibrarianSpineSettings> settings, Func<LibrarianSpineContext> context,
        Action<LibrarianSpineObservation>? observe = null, Action<LibrarianSpineMotionCue>? visualCue = null)
    {
        _binding = binding;
        _bank = bank;
        _readSettings = settings;
        _readContext = context;
        _observe = observe;
        _visualCue = visualCue;
        _settings = settings();
    }

    internal void Initialize()
    {
        if (_readContext().IsDead)
        {
            _deadLatch = true;
            Pose = LibrarianSpinePose.Dead;
            StartBody("die", 0, terminal: true, muteEvents: true);
            SetExpression("expression_off");
        }
        else
        {
            ReturnToCurrentRoom();
            SetRestingExpression();
        }
    }

    // A real CardPlay object is the token. Replays and repeated class names are different objects.
    // Returning true also means an intentional priority/settings suppression was consumed.
    internal bool ConsumeCard(object cardPlayToken, long occurrenceId, string semantic)
    {
        if (_disposed) return false;
        BeforeUpdate();
        var seen = _occurrences.GetValue(cardPlayToken, _ => new Seen());
        if (seen.Handled) { _deduped++; Emit("card_duplicate", "same CardPlay"); return true; }
        seen.Handled = true;
        if (!LibrarianSpineBank.IsCardGesture(semantic)) throw new ArgumentException("Unverified card motion route.", nameof(semantic));
        if (_deadLatch || Pose is LibrarianSpinePose.Hurt or LibrarianSpinePose.Reviving
            || _settings.ReducedMotion || _settings.Speed == LibrarianSpineSpeed.Instant || _readContext().IsDead)
        {
            _suppressed++;
            Emit("card_suppressed", "consumed occurrence " + occurrenceId);
            return true;
        }
        Pose = LibrarianSpinePose.Card;
        _accepted++;
        StartBody(semantic, occurrenceId);
        SetRestingExpression();
        _cardToken = cardPlayToken;
        _preparePending = true;
        _releaseSent = false;
        return true;
    }

    internal bool NativeTrigger(string trigger, bool mergeWithHandledCard, long nativeOccurrenceId)
    {
        if (_disposed) return false;
        BeforeUpdate();
        switch (trigger)
        {
            case "Dead":
                if (_deadLatch && Pose is LibrarianSpinePose.Dying or LibrarianSpinePose.Dead) { _deduped++; Emit("dead_duplicate", "latched"); return true; }
                _accepted++;
                _deadLatch = true;
                _drawPending = false;
                _victorious = false;
                Pose = LibrarianSpinePose.Dying;
                StartBody("die", nativeOccurrenceId);
                SetExpression("expression_off");
                return true;
            case "Revive":
                if (!_deadLatch || Pose == LibrarianSpinePose.Reviving) { _suppressed++; Emit("revive_ignored", "requires actual latched death"); return true; }
                _accepted++;
                Pose = LibrarianSpinePose.Reviving;
                // Start from the currently visible pose; never jump to the terminal first.
                StartBody("revive", nativeOccurrenceId);
                SetExpression("expression_dot");
                return true;
            case "Hit":
                if (_deadLatch || Pose == LibrarianSpinePose.Reviving || _readContext().IsDead) { _suppressed++; return true; }
                _accepted++;
                Pose = LibrarianSpinePose.Hurt;
                StartBody("hurt", nativeOccurrenceId);
                SetExpression("expression_surprise");
                _expressionUntil = .48f;
                return true;
            case "Attack": case "Cast": case "PowerUp":
                if (mergeWithHandledCard) { _deduped++; Emit("native_card_merged", "exact handled execution"); return true; }
                if (_deadLatch || Pose is LibrarianSpinePose.Hurt or LibrarianSpinePose.Reviving || _readContext().IsDead) { _suppressed++; return true; }
                if (_settings.ReducedMotion || _settings.Speed == LibrarianSpineSpeed.Instant) { _suppressed++; return true; }
                _accepted++;
                Pose = LibrarianSpinePose.Card;
                StartBody(trigger == "Attack" ? "attack" : trigger == "PowerUp" ? "power" : "cast", nativeOccurrenceId);
                SetRestingExpression();
                return true;
            case "Idle": case "Relaxed":
                if (_deadLatch || Pose != LibrarianSpinePose.RoomIdle) return true;
                ReturnToCurrentRoom();
                return true;
            default: return false;
        }
    }

    internal void BeforeUpdate()
    {
        if (_disposed) return;
        if (_returnPending)
        {
            _returnPending = false;
            FinishBody();
        }
        var next = _readSettings();
        if (_foreignEntry)
        {
            _foreignEntry = false;
            // The only expected foreign room start is the native startup's normal loop.
            // Keep semantic/phase/death latch; no new card occurrence or release is created.
            StartBody(_semantic, _occurrenceId, phase: _lastTrackTime, muteEvents: true);
            Emit("foreign_start_reconciled", "owned semantic restored");
        }
        if (next != _settings)
        {
            float phase = _body?.TrackTime ?? _lastTrackTime;
            float oldDuration = _clip?.Duration ?? 0;
            bool changedReduced = next.ReducedMotion != _settings.ReducedMotion;
            _settings = next;
            if (Pose == LibrarianSpinePose.Card && (next.ReducedMotion || next.Speed == LibrarianSpineSpeed.Instant))
                ReturnToCurrentRoom();
            else
            {
                var target = _bank.Resolve(_semantic, next.ReducedMotion);
                float mapped = _clip?.Loop == true && oldDuration > 0 && target.Duration > 0
                    ? (phase % oldDuration) / oldDuration * target.Duration
                    : Math.Min(phase, target.Duration);
                StartBody(_semantic, _occurrenceId, phase: mapped, muteEvents: true, terminal: Pose == LibrarianSpinePose.Dead);
            }
            // Expression changes prefix only. No new inferred emotion is introduced.
            if (changedReduced) SetExpression(_expression);
            Emit("settings_preserved", "semantic/phase/latch/expression; no release replay");
        }
        if (!_deadLatch && Pose == LibrarianSpinePose.RoomIdle)
        {
            string semantic = LibrarianSpineBank.ReturnSemantic(_readContext(), _settings);
            if (_semantic != semantic) ReturnToCurrentRoom();
            if (_expressionUntil < 0) SetRestingExpression();
            if (_drawPending)
            {
                _drawPending = false;
                if (!_settings.ReducedMotion && _settings.Speed != LibrarianSpineSpeed.Instant && !_victorious)
                {
                    Pose = LibrarianSpinePose.Card;
                    StartBody("book_flip", 0);
                    Emit("actual_draw_page_turn", "coalesced real draws; no card execution or release");
                }
            }
        }
        if (_pendingExpression is { } expression)
        {
            _pendingExpression = null;
            SetExpression(expression);
        }
        else if (_restoreExpressionPending)
        {
            _restoreExpressionPending = false;
            SetRestingExpression();
        }
    }

    // Synchronous Started may precede set_animation's return. Only a strict pending transition may tag it.
    internal void NativeStarted(ILibrarianSpineEntry entry)
    {
        if (_disposed) return;
        if (_pending is { Started: false } pending && pending.Generation == _generation && entry.Track == pending.Track && entry.Name == pending.Name)
        {
            entry.TagBits = pending.TagBits;
            pending.Started = true;
            Emit("entry_started_tagged", "pending owned track " + pending.Track);
            return;
        }
        if (entry.Track != 0) return;
        if (entry.TagBits == _bodyTag && _bodyTag != 0) return;
        _foreignEntry = true;
        CancelVisual();
        _completeGeneration = null;
        _events.Clear();
        ClearHolds();
        _body?.Dispose();
        _body = null;
        Emit("foreign_entry_started", entry.Name);
    }

    internal void NativeCompleted(ILibrarianSpineEntry entry)
    {
        if (entry.Track != 0 || _clip?.Loop == true) return; // Static eye/loop completes never return the body.
        if (MatchesCurrent(entry))
        {
            if (_completionConsumed == _generation || _completeGeneration == _generation) { _deduped++; return; }
            _completeGeneration = _generation;
        }
        else { _stale++; Emit("stale_complete_ignored", "entry tag does not own current generation"); }
    }
    internal void NativeInterrupted(ILibrarianSpineEntry entry)
    {
        if (entry.Track != 0) return;
        // Native set_animation has already changed get_current while broadcasting interruption.
        // A matching owned tag may save its final visible phase; it cannot complete/return/revive anything.
        if (!_disposed && entry.TagBits == _bodyTag && !_insideMutation)
        {
            _lastTrackTime = entry.TrackTime;
            CancelVisual();
            _completeGeneration = null;
            _events.Clear();
            Emit("current_entry_interrupted", "external native replacement");
        }
        else { _stale++; Emit("stale_interrupt_ignored", "entry tag does not own current generation"); }
    }
    internal void NativeEvent(ILibrarianSpineEntry entry, string eventName, float eventTime)
    {
        if (entry.Track != 0) return;
        if (!MatchesCurrent(entry) || _muteCosmeticEvents) { _stale++; Emit("stale_event_ignored", eventName); return; }
        if (!_consumedEvents.Add((eventName, BitConverter.SingleToInt32Bits(eventTime)))) { _deduped++; return; }
        _events.Enqueue((_generation, _bodyTag, eventName));
    }

    internal void AfterApply()
    {
        if (_disposed || _insideMutation || _body is null) return;
        if (_clip?.Loop == true && _holds.Count == 0 && _events.Count == 0 && _completeGeneration is null) return;
        if (_binding.CurrentTag(0) != _bodyTag) return;
        _lastTrackTime = _body.TrackTime;
        EnforceHolds();
    }

    // Called by the native world_transforms_changed signal after the socket cache is refreshed.
    // Never apply a new body here: changing its local pose after world transforms would render stale geometry.
    internal void AfterWorldTransforms()
    {
        if (_disposed || _insideMutation || _body is null || _binding.CurrentTag(0) != _bodyTag) return;
        _lastTrackTime = _body.TrackTime;
        if (_preparePending)
        {
            _preparePending = false;
            Cue(LibrarianSpineCueStage.Prepare);
        }
        while (_events.TryDequeue(out var e))
            if (e.Generation == _generation && e.Tag == _bodyTag && !_muteCosmeticEvents)
            {
                Emit("cosmetic_event", e.Event);
                if (e.Event == (_semantic == "attack" ? "attack_release" : "cast_release")
                    && !_releaseSent && _cardToken is not null)
                {
                    _releaseSent = true;
                    Cue(LibrarianSpineCueStage.Release);
                    if (_semantic == "attack" && !_victorious) { _pendingExpression = "expression_narrowed"; _expressionUntil = _body.TrackTime + .20f; }
                }
            }
        // The fixed native host can omit animation_event broadcasts while this entry visibly
        // advances. Its pinned authored marker is also a valid cosmetic clock. Read real entry
        // time after world transforms, with the same generation and single-release latch.
        if (!_releaseSent && !_muteCosmeticEvents && _cardToken is not null
            && _clip?.ReleaseTime is { } authoredRelease && _body.TrackTime >= authoredRelease)
        {
            _releaseSent = true;
            Cue(LibrarianSpineCueStage.Release);
            Emit("phase_release", "current entry crossed pinned authored marker " + authoredRelease);
            if (_semantic == "attack" && !_victorious) { _pendingExpression = "expression_narrowed"; _expressionUntil = _body.TrackTime + .20f; }
        }
        if (_resultConfirmed && _releaseSent && _semantic == "power")
        {
            _resultConfirmed = false;
            _pendingExpression = "expression_star";
            _expressionUntil = _body.TrackTime + .22f;
        }
        if (_semantic == "hurt" && _expression == "expression_surprise" && _body.TrackTime >= .14f && _body.TrackTime < .48f)
            _pendingExpression = "expression_tear";
        if (_expressionUntil >= 0 && _body.TrackTime >= _expressionUntil)
        {
            _expressionUntil = -1;
            _pendingExpression = null;
            _restoreExpressionPending = true;
        }
        if (_clip?.Loop != false || _completeGeneration != _generation || !_body.IsComplete) return;
        _completeGeneration = null;
        _completionConsumed = _generation;
        _completed++;
        Cue(LibrarianSpineCueStage.Completed);
        _cardToken = null;
        _returnPending = true;
    }

    private void FinishBody()
    {
        if (Pose == LibrarianSpinePose.RoomIdle) return; // idle_still retains its pose; no zero-duration restart loop.
        if (Pose == LibrarianSpinePose.Dying)
        {
            Pose = LibrarianSpinePose.Dead;
            Emit("death_terminal_held", "opaque authored final pose");
            return;
        }
        if (Pose == LibrarianSpinePose.Dead) return;
        if (Pose == LibrarianSpinePose.Reviving)
        {
            if (_readContext().IsDead)
            {
                // Native death can preempt a rebuild; this never changes gameplay HP.
                Pose = LibrarianSpinePose.Dying;
                StartBody("die", 0);
                SetExpression("expression_off");
                return;
            }
            _deadLatch = false;
        }
        ReturnToCurrentRoom();
    }

    private bool MatchesCurrent(ILibrarianSpineEntry entry) => !_disposed && entry.Track == 0 && _bodyTag != 0
        && entry.TagBits == _bodyTag && _binding.CurrentTag(0) == _bodyTag;

    private void ReturnToCurrentRoom()
    {
        if (_deadLatch) return;
        Pose = LibrarianSpinePose.RoomIdle;
        _settings = _readSettings();
        StartBody(LibrarianSpineBank.ReturnSemantic(_readContext(), _settings), 0);
        SetRestingExpression();
    }

    private void StartBody(string semantic, long occurrenceId, float phase = 0, bool terminal = false, bool muteEvents = false)
    {
        if (_disposed) return;
        CancelVisual();
        _returnPending = false;
        _expressionUntil = -1;
        _pendingExpression = null;
        _restoreExpressionPending = false;
        var clip = _bank.Resolve(semantic, _settings.ReducedMotion);
        if (clip.Track != 0) throw new InvalidDataException("Body clip is not on track zero.");
        var captured = _hasVisibleBody ? _binding.Capture(_bank.Slots) : [];
        ClearHolds();
        foreach (var attachment in captured)
            _holds.Add(new Hold(attachment, clip.FirstAttachmentKeys[attachment.SlotName]));
        _generation++;
        _occurrenceId = occurrenceId;
        _bodyTag = LibrarianSpineEntryTags.Next();
        _completeGeneration = null;
        _events.Clear();
        _consumedEvents.Clear();
        _muteCosmeticEvents = muteEvents;
        string? fromClip = _clip?.Name;
        _semantic = semantic;
        _clip = clip;
        var old = _body;
        _body = null;
        _pending = new Pending(_generation, _bodyTag, clip.Name, 0);
        _insideMutation = true;
        try
        {
            var entry = _binding.SetAnimation(clip.Name, clip.Loop, 0);
            _body = entry;
            _hasVisibleBody = true;
            if (!_pending.Started || entry.TagBits != _bodyTag)
                throw new InvalidOperationException("Synchronous Started failed to tag the exact owned entry.");
            float actualDuration = entry.Duration;
            if (!float.IsFinite(actualDuration) || Math.Abs(actualDuration - clip.Duration) > .0001f)
                throw new InvalidDataException("Runtime/receipt animation duration differs: " + clip.Name);
            entry.MixDuration = terminal ? 0 : _bank.MixDuration(fromClip, clip, _settings.Speed);
            entry.DisableMixingCallbacks();
            entry.TimeScale = _settings.Speed == LibrarianSpineSpeed.Fast ? 2 : 1;
            entry.TrackTime = terminal ? clip.Duration : Math.Clamp(phase, 0, clip.Loop ? float.MaxValue : clip.Duration);
            _lastTrackTime = entry.TrackTime;
            _binding.ApplyZero(EnforceHolds);
        }
        finally
        {
            _pending = null;
            _insideMutation = false;
            old?.Dispose();
        }
        Emit("body_replaced", "from actual visible attachments");
    }

    private void SetExpression(string semantic)
    {
        if (_expressionEntry is not null && _expression == semantic && _expressionReduced == _settings.ReducedMotion) return;
        var clip = _bank.Resolve(semantic, _settings.ReducedMotion);
        if (clip.Track != 1) throw new InvalidDataException("Expression clip is not on track one.");
        _expression = semantic;
        int tag = LibrarianSpineEntryTags.Next();
        var old = _expressionEntry;
        _pending = new Pending(_generation, tag, clip.Name, 1);
        try
        {
            var entry = _binding.SetAnimation(clip.Name, clip.Loop, 1);
            _expressionEntry = entry;
            if (!_pending.Started || entry.TagBits != tag) throw new InvalidOperationException("Expression entry identity was not registered.");
            _expressionTag = tag;
            _expressionReduced = _settings.ReducedMotion;
            entry.MixDuration = 0;
            entry.DisableMixingCallbacks();
            entry.TimeScale = 1;
            _binding.ApplyZero(EnforceHolds);
        }
        finally { _pending = null; old?.Dispose(); }
    }

    private void EnforceHolds()
    {
        if (_body is null || _binding.CurrentTag(0) != _bodyTag) return;
        for (int i = _holds.Count - 1; i >= 0; i--)
        {
            var hold = _holds[i];
            // Artist keys use entry time; absent keys use real native mix time, unaffected by per-entry Fast scale.
            bool keep = hold.FirstKey is { } first ? _body.TrackTime < first : _body.MixTime < _body.MixDuration;
            if (keep) hold.Attachment.Restore();
            else { hold.Attachment.Dispose(); _holds.RemoveAt(i); }
        }
        if (_holds.Count > 0 && _observe is not null) Emit("attachment_hold_after_apply", _holds.Count + " nullable pointers restored");
    }

    private void ClearHolds()
    {
        foreach (var hold in _holds) hold.Attachment.Dispose();
        _holds.Clear();
    }
    private void SetRestingExpression()
    {
        var context = _readContext();
        string semantic = _deadLatch ? "expression_off"
            : context.Room == LibrarianSpineRoom.Rest ? "expression_friendly_arc"
            : _victorious ? "expression_heart" : context.LowHealth ? "expression_uneasy_arc"
            : !_settings.ReducedMotion && _settings.Speed != LibrarianSpineSpeed.Instant
                && Pose == LibrarianSpinePose.RoomIdle && _lastTrackTime % 11.8f >= 11.62f
                    ? "expression_friendly_arc" : "expression_dot";
        SetExpression(semantic);
    }
    private void Cue(LibrarianSpineCueStage stage)
    {
        if (_cardToken is { } token)
            _visualCue?.Invoke(new(token, stage, _semantic, _generation, _bodyTag, _occurrenceId, _settings));
    }
    private void CancelVisual()
    {
        Cue(LibrarianSpineCueStage.Cancelled);
        _cardToken = null;
        _preparePending = false;
        _releaseSent = false;
        _resultConfirmed = false;
    }
    private void Emit(string kind, string detail) => _observe?.Invoke(new(kind, _generation, _bodyTag, _clip?.Name ?? "",
        _occurrenceId, detail));

    public void Dispose()
    {
        if (_disposed) return;
        CancelVisual();
        _generation++; // Invalidate before detaching, clearing tracks or releasing wrappers.
        _disposed = true;
        _binding.DisconnectListeners();
        _pending = null;
        _completeGeneration = null;
        _events.Clear();
        ClearHolds();
        _binding.ClearTrackIfOwned(0, _bodyTag);
        _binding.ClearTrackIfOwned(1, _expressionTag);
        _body?.Dispose(); _body = null;
        _expressionEntry?.Dispose(); _expressionEntry = null;
        _binding.Dispose();
    }
}
