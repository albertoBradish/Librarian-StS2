namespace Librarian.Core;

/// <summary>
/// One player's combat-only state. Mutations are serialized by the adapter on the game action queue.
/// No engine damage modifiers, disk saves, static player state, or background worker are owned here.
/// </summary>
public sealed class OrbCombatState
{
    private static readonly OrbKind[] Order = [OrbKind.Fire, OrbKind.Tide, OrbKind.Growth];
    private readonly List<OrbKind> _positions = [OrbKind.Fire, OrbKind.Tide, OrbKind.Growth];
    private readonly int[] _lockedTurns = new int[3]; // -1 is permanent; positive includes this turn.
    private readonly int[] _values = new int[3];
    private readonly int[] _highWater = new int[3];
    public int HighestValueThisCombat(OrbKind kind) { Validate(kind); return _highWater[(int)kind]; }
    private readonly bool[] _active = new bool[3];
    private readonly HashSet<string> _firstThisTurn = new(StringComparer.Ordinal);
    private readonly HashSet<OrbKind> _suppressed = [];
    private readonly HashSet<OrbKind> _lockedKindsThisCombat = [];
    private readonly List<ExtraTask> _extraTasks = [];
    private long _eventSequence;
    private long _resolvedTurn = -1;
    private bool _resolving;
    private bool _snapshotFrozen;
    private bool _faulted;
    private readonly HashSet<OrbKind> _preserved = [];
    public bool SwitchLocked { get; set; }

    public OrbCombatState(string ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        OwnerId = ownerId;
    }

    public string OwnerId { get; }
    public long OwnerTurn { get; private set; }
    public OrbKind Foreground => _positions[0];
    public int SettlementsThisTurn { get; private set; }
    public int SettlementsThisCombat { get; private set; }
    public IReadOnlyList<OrbKind> Positions => _positions.AsReadOnly();
    public long ZeroCount { get; private set; }
    public long LossCount { get; private set; }
    public long SwitchesThisTurn { get; private set; }
    public bool TideGainBlocked { get; private set; }
    public bool IsFaulted => _faulted;
    public TideBlockLedger BlockLedger { get; } = new();
    /// <summary>Orb kinds successfully locked at least once in this combat.</summary>
    public IReadOnlySet<OrbKind> LockedKindsThisCombat => _lockedKindsThisCombat;
    public int LockedKindsThisCombatCount => _lockedKindsThisCombat.Count;
    public bool WasEverLocked(OrbKind kind) { Validate(kind); return _lockedKindsThisCombat.Contains(kind); }

    public OrbCombatSnapshot Snapshot() => new(OwnerId, OwnerTurn, Foreground,
        Array.AsReadOnly(_positions.Select(k => new OrbView(k, _values[(int)k], _active[(int)k], k == Foreground, _lockedTurns[(int)k])).ToArray()),
        ZeroCount, LossCount, SwitchesThisTurn, TideGainBlocked);

    public int Value(OrbKind kind) { Validate(kind); return _values[(int)kind]; }
    public bool IsActivated(OrbKind kind) { Validate(kind); return _active[(int)kind]; }
    public bool IsLocked(OrbKind kind) { Validate(kind); return _lockedTurns[(int)kind] != 0; }
    public int LockedTurns(OrbKind kind) { Validate(kind); return _lockedTurns[(int)kind]; }

    public OrbOperationResult Lock(OrbKind kind, int? turns = null, OrbOrigin? origin = null)
    {
        EnsureHealthy(); Validate(kind);
        if (turns is <= 0) throw new ArgumentOutOfRangeException(nameof(turns));
        int before = _lockedTurns[(int)kind];
        int after = before == OrbView.PermanentLock || turns is null ? OrbView.PermanentLock : checked(before + turns.Value);
        _lockedTurns[(int)kind] = after;
        _lockedKindsThisCombat.Add(kind);
        origin = Normalize(origin, "lock");
        var events = new List<OrbEvent> { Event(OrbEventKind.Locked, kind, Value(kind), Value(kind), 0, origin) };
        // The lock is already visible when extinguish listeners run, so they cannot reactivate it.
        events.AddRange(Extinguish(kind, OrbScope.All, origin).Events);
        return Result(0, events);
    }

    public OrbOperationResult Unlock(OrbKind kind, OrbOrigin? origin = null)
    {
        EnsureHealthy(); Validate(kind);
        if (!IsLocked(kind)) return OrbOperationResult.Empty(OrbOperationStatus.Applied);
        _lockedTurns[(int)kind] = 0;
        return Result(0, [Event(OrbEventKind.Unlocked, kind, Value(kind), Value(kind), 0, Normalize(origin, "unlock"))]);
    }

    public OrbOperationResult SwapPositions(OrbKind first, OrbKind second, OrbOrigin? origin = null)
    {
        EnsureHealthy(); Validate(first); Validate(second);
        if (SwitchLocked && (first == Foreground || second == Foreground))
            return OrbOperationResult.Empty(OrbOperationStatus.Blocked);
        if (first == second) return OrbOperationResult.Empty(OrbOperationStatus.Applied);
        var before = Foreground;
        int a = _positions.IndexOf(first), b = _positions.IndexOf(second);
        (_positions[a], _positions[b]) = (_positions[b], _positions[a]);
        origin = Normalize(origin, "swap");
        var events = new List<OrbEvent>();
        RecordPositionChange(first, before, origin, events);
        return Result(0, events);
    }

    /// <summary>Ties follow the ring after the source, without consuming combat RNG.</summary>
    public OrbKind? LowestOther(OrbKind source)
    {
        Validate(source);
        var candidates = _positions.Where(k => k != source).ToArray();
        int min = candidates.Min(Value);
        return ResolveTie(candidates.Where(k => Value(k) == min).ToArray(), source);
    }

    public OrbKind? HighestOther(OrbKind source)
    {
        Validate(source);
        var candidates = _positions.Where(k => k != source).ToArray();
        int max = candidates.Max(Value);
        return ResolveTie(candidates.Where(k => Value(k) == max).ToArray(), source);
    }

    public long BeginOwnerTurn()
    {
        EnsureHealthy();
        if (_resolving) throw new InvalidOperationException("Cannot begin a turn during end-turn resolution.");
        OwnerTurn = checked(OwnerTurn + 1);
        TideGainBlocked = false;
        SwitchesThisTurn = 0;
        SettlementsThisTurn = 0;
        foreach (var kind in Order) if (_lockedTurns[(int)kind] > 0) _lockedTurns[(int)kind]--;
        _suppressed.Clear();
        _preserved.Clear();
        _firstThisTurn.Clear();
        return OwnerTurn;
    }

    public void PreserveEndTurnActivation(OrbKind kind) { EnsureHealthy(); Validate(kind); _preserved.Add(kind); }
    public void BlockTideGainForThisTurn() { EnsureHealthy(); TideGainBlocked = true; }
    public void SuppressEndTurnSettlement(OrbKind kind) { EnsureHealthy(); Validate(kind); _suppressed.Add(kind); }
    public bool TryMarkFirstThisTurn(string key)
    {
        EnsureHealthy();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _firstThisTurn.Add(key);
    }

    public IReadOnlyList<OrbKind> Select(OrbScope scope, ActivationFilter activation = ActivationFilter.Any)
    {
        Validate(scope); Validate(activation);
        return Array.AsReadOnly(_positions.Where(k => InScope(k, scope) && MatchesActivation(k, activation)).ToArray());
    }

    /// <summary>Read-only candidates for UI. Never consumes randomness.</summary>
    public IReadOnlyList<OrbKind> GetCandidates(OrbSelector selector)
    {
        ValidateSelector(selector);
        var candidates = Select(selector.Scope, selector.Activation);
        if (selector.Selection == OrbSelection.Named)
            return candidates.Contains(selector.Kind!.Value) ? Array.AsReadOnly(new[] { selector.Kind.Value }) : Array.Empty<OrbKind>();
        if (candidates.Count == 0 || selector.Selection == OrbSelection.Random) return candidates;
        var value = selector.Selection == OrbSelection.Lowest ? candidates.Min(Value) : candidates.Max(Value);
        return Array.AsReadOnly(candidates.Where(k => Value(k) == value).ToArray());
    }

    public OrbKind? ResolveSelector(OrbSelector selector, IOrbRandom? random = null, OrbKind? source = null)
    {
        var candidates = GetCandidates(selector);
        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];
        if (selector.Selection != OrbSelection.Random) return ResolveTie(candidates, source ?? Foreground);
        ArgumentNullException.ThrowIfNull(random);
        var index = random.NextInt(candidates.Count);
        if ((uint)index >= (uint)candidates.Count) throw new InvalidOperationException("Combat RNG returned an out-of-range index.");
        return candidates[index];
    }

    private OrbKind ResolveTie(IReadOnlyList<OrbKind> candidates, OrbKind source)
    {
        int start = _positions.IndexOf(source);
        for (int step = 1; step <= 3; step++)
        {
            var kind = _positions[(start + step) % 3];
            if (candidates.Contains(kind)) return kind;
        }
        throw new InvalidOperationException("Empty tie candidates.");
    }

    /// <summary>Gain uniquely crosses background scope. Zero gain still switches and activates; blocked Tide gain does nothing.</summary>
    public OrbOperationResult Gain(OrbKind kind, int amount, OrbOrigin? origin = null)
    {
        EnsureHealthy(); Validate(kind); Nonnegative(amount);
        if (kind == OrbKind.Tide && TideGainBlocked) return OrbOperationResult.Empty(OrbOperationStatus.Blocked);
        int before = Value(kind), after = checked(before + amount);
        origin = Normalize(origin, "gain");
        var events = new List<OrbEvent>();
        SwitchTo(kind, origin, events);
        _values[(int)kind] = after;
        events.Add(Event(OrbEventKind.Gained, kind, before, after, amount, origin));
        ActivateInternal(kind, origin, events);
        if (!IsLocked(kind)) events.Add(Event(OrbEventKind.Imbued, kind, after, after, 0, origin));
        return Result(amount, events);
    }

    public OrbOperationResult Strengthen(OrbKind kind, int amount, OrbScope scope = OrbScope.Foreground, OrbOrigin? origin = null)
    {
        EnsureHealthy(); Validate(kind); Validate(scope); Nonnegative(amount);
        if (!InScope(kind, scope)) return OrbOperationResult.Empty(OrbOperationStatus.OutOfScope);
        int before = Value(kind), after = checked(before + amount);
        _values[(int)kind] = after;
        return amount == 0 ? OrbOperationResult.Empty(OrbOperationStatus.Applied) :
            Result(amount, [Event(OrbEventKind.Strengthened, kind, before, after, amount, Normalize(origin, "strengthen"))]);
    }

    public OrbOperationResult Activate(OrbKind kind, OrbScope scope = OrbScope.Foreground, OrbOrigin? origin = null)
    {
        EnsureHealthy(); Validate(kind); Validate(scope);
        if (IsLocked(kind)) return OrbOperationResult.Empty(OrbOperationStatus.Blocked);
        if (!InScope(kind, scope)) return OrbOperationResult.Empty(OrbOperationStatus.OutOfScope);
        origin = Normalize(origin, "activate");
        var events = new List<OrbEvent>();
        SwitchTo(kind, origin, events);
        ActivateInternal(kind, origin, events);
        events.Add(Event(OrbEventKind.Channelled, kind, Value(kind), Value(kind), 0, origin));
        events.Add(Event(OrbEventKind.Imbued, kind, Value(kind), Value(kind), 0, origin));
        return Result(0, events);
    }

    public OrbOperationResult ActivateWithoutSwitch(OrbKind kind, OrbScope scope = OrbScope.All, OrbOrigin? origin = null)
    {
        EnsureHealthy(); Validate(kind); Validate(scope);
        if (IsLocked(kind)) return OrbOperationResult.Empty(OrbOperationStatus.Blocked);
        if (!InScope(kind, scope)) return OrbOperationResult.Empty(OrbOperationStatus.OutOfScope);
        var events = new List<OrbEvent>();
        origin = Normalize(origin, "activate-without-switch");
        ActivateInternal(kind, origin, events);
        events.Add(Event(OrbEventKind.Channelled, kind, Value(kind), Value(kind), 0, origin));
        events.Add(Event(OrbEventKind.Imbued, kind, Value(kind), Value(kind), 0, origin));
        return Result(0, events);
    }

    public OrbOperationResult Lose(OrbKind kind, int requested, OrbScope scope = OrbScope.Foreground, OrbOrigin? origin = null)
    {
        EnsureHealthy(); Validate(kind); Validate(scope); Nonnegative(requested);
        if (!InScope(kind, scope)) return OrbOperationResult.Empty(OrbOperationStatus.OutOfScope);
        int before = Value(kind), actual = Math.Min(before, requested), after = before - actual;
        if (actual == 0) return OrbOperationResult.Empty(OrbOperationStatus.Applied);
        origin = Normalize(origin, "loss");
        _values[(int)kind] = after;
        LossCount++;
        var events = new List<OrbEvent> { Event(OrbEventKind.Lost, kind, before, after, actual, origin) };
        if (after == 0)
        {
            ZeroCount++;
            events.Add(Event(OrbEventKind.Zeroed, kind, before, after, actual, origin));
        }
        return Result(actual, events);
    }

    public OrbOperationResult LoseAll(OrbKind kind, OrbScope scope = OrbScope.Foreground, OrbOrigin? origin = null) => Lose(kind, Value(kind), scope, origin);
    public OrbOperationResult LoseHalf(OrbKind kind, OrbScope scope = OrbScope.Foreground, OrbOrigin? origin = null) => Lose(kind, Value(kind) / 2, scope, origin);

    public OrbOperationResult Extinguish(OrbKind kind, OrbScope scope = OrbScope.Foreground, OrbOrigin? origin = null)
    {
        EnsureHealthy(); Validate(kind); Validate(scope);
        if (!InScope(kind, scope)) return OrbOperationResult.Empty(OrbOperationStatus.OutOfScope);
        if (!_active[(int)kind]) return OrbOperationResult.Empty(OrbOperationStatus.Applied);
        _active[(int)kind] = false;
        return Result(0, [Event(OrbEventKind.Extinguished, kind, Value(kind), Value(kind), 0, Normalize(origin, "extinguish"))]);
    }

    /// <summary>Permutes existing values only. Position/activation and all loss/zero counters stay unchanged.</summary>
    public OrbOperationResult PermuteValues(OrbKind fireSource, OrbKind tideSource, OrbKind growthSource, OrbOrigin? origin = null)
    {
        EnsureHealthy(); Validate(fireSource); Validate(tideSource); Validate(growthSource);
        OrbKind[] sources = [fireSource, tideSource, growthSource];
        if (sources.Distinct().Count() != 3) throw new ArgumentException("Each source orb must occur exactly once.");
        int[] before = (int[])_values.Clone();
        origin = Normalize(origin, "permutation");
        var events = new List<OrbEvent>();
        foreach (var kind in Order) _values[(int)kind] = before[(int)sources[(int)kind]];
        foreach (var kind in Order)
            if (before[(int)kind] != Value(kind)) events.Add(Event(OrbEventKind.ValuesPermuted, kind, before[(int)kind], Value(kind), 0, origin));
        return Result(0, events);
    }

    /// <summary>Counts are captured now; target resolves once after delayed actions at end turn. Late registrations run next turn.</summary>
    public void QueueExtraSettlement(OrbSelector selector, int count, string source, bool loseAllAfter = false)
    {
        EnsureHealthy(); ValidateSelector(selector); Nonnegative(count);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (count == 0 && !loseAllAfter) return;
        long due = _snapshotFrozen || _resolvedTurn == OwnerTurn ? checked(OwnerTurn + 1) : OwnerTurn;
        _extraTasks.Add(new(selector, count, source, loseAllAfter, due));
    }

    /// <summary>A copied settlement uses an external value and does not mutate a local orb.</summary>
    public void RecordSharedSettlement()
    {
        EnsureHealthy();
        SettlementsThisTurn = checked(SettlementsThisTurn + 1);
        SettlementsThisCombat = checked(SettlementsThisCombat + 1);
    }

    public async ValueTask<int> SettleImmediatelyAsync(OrbSelector selector, int count,
        Func<SettlementRequest, ValueTask> settleAsync, string source, IOrbRandom? random = null, Func<bool>? canSettle = null)
    {
        EnsureHealthy(); ValidateSelector(selector); Nonnegative(count); ArgumentNullException.ThrowIfNull(settleAsync);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (count == 0) return 0;
        var target = ResolveSelector(selector, random);
        if (target is null) return 0;
        int executed = 0;
        for (int i = 0; i < count; i++)
        {
            if (IsLocked(target.Value) || canSettle?.Invoke() == false) break;
            await settleAsync(new(OwnerId, OwnerTurn, target.Value, Value(target.Value), SettlementReason.Immediate, source, i + 1, count));
            SettlementsThisTurn = checked(SettlementsThisTurn + 1);
            SettlementsThisCombat = checked(SettlementsThisCombat + 1);
            executed++;
        }
        return executed;
    }

    /// <summary>
    /// Tide expiry → delayed actions → ring-ordered natural settlements with immediate extinguish callbacks → extras.
    /// A callback failure poisons this instance to prevent replaying partially executed external effects.
    /// </summary>
    public async ValueTask<EndTurnResult> ResolveEndTurnAsync(EndTurnRules rules,
        Func<SettlementRequest, ValueTask> settleAsync,
        Func<OrbOperationResult, ValueTask>? afterOperationAsync = null,
        Func<BlockLedgerChange, ValueTask>? expireBlockAsync = null,
        Func<ValueTask>? delayedAsync = null, IOrbRandom? random = null, Func<bool>? canSettle = null)
    {
        EnsureHealthy(); ArgumentNullException.ThrowIfNull(rules); Validate(rules.NaturalScope);
        ArgumentNullException.ThrowIfNull(settleAsync);
        if (_resolving) throw new InvalidOperationException("End turn is already resolving.");
        if (_resolvedTurn == OwnerTurn) return new(true, 0, Array.Empty<OrbKind>());
        _resolving = true;
        try
        {
            var expired = BlockLedger.Expire(OwnerTurn, BlockExpiryPhase.OwnerTurnEnd, includeOrdinary: false);
            if (expired.TotalRemoved > 0 && expireBlockAsync is not null) await expireBlockAsync(expired);
            if (delayedAsync is not null) await delayedAsync();

            _snapshotFrozen = true;
            var suppressed = new HashSet<OrbKind>(_suppressed);
            var dueTasks = _extraTasks.Where(t => t.DueTurn <= OwnerTurn)
                .Select(t => (Task: t, Target: ResolveSelector(t.Selector, random))).ToArray();
            _extraTasks.RemoveAll(t => t.DueTurn <= OwnerTurn);
            var natural = new List<OrbKind>();
            int executed = 0;
            // Re-read ring order, activation and values after every effect/callback.
            while (canSettle?.Invoke() != false)
            {
                var next = Select(rules.NaturalScope, ActivationFilter.Active)
                    .Where(k => !natural.Contains(k) && !suppressed.Contains(k) && !IsLocked(k))
                    .Select(k => (OrbKind?)k).FirstOrDefault();
                if (next is not { } kind) break;
                bool background = kind != Foreground;
                natural.Add(kind);
                await settleAsync(new(OwnerId, OwnerTurn, kind, Value(kind), SettlementReason.NaturalEndTurn,
                    "natural", 1, 1, rules.HalfBackground && background));
                SettlementsThisTurn = checked(SettlementsThisTurn + 1);
                SettlementsThisCombat = checked(SettlementsThisCombat + 1);
                executed++;
                if (!rules.PreserveActivation && !_preserved.Contains(kind)
                    && !(rules.PreserveBackgroundActivation && background))
                {
                    var operation = Extinguish(kind, OrbScope.All, new("natural-end-turn"));
                    if (afterOperationAsync is not null && operation.Events.Count > 0) await afterOperationAsync(operation);
                }
            }
            foreach (var (task, target) in dueTasks)
            {
                if (target is not { } kind || suppressed.Contains(kind) || IsLocked(kind) || canSettle?.Invoke() == false) continue;
                for (int i = 0; i < task.Count; i++)
                {
                    if (IsLocked(kind) || canSettle?.Invoke() == false) break;
                    await settleAsync(new(OwnerId, OwnerTurn, kind, Value(kind), SettlementReason.ExtraEndTurn,
                        task.Source, i + 1, task.Count));
                    SettlementsThisTurn = checked(SettlementsThisTurn + 1);
                    SettlementsThisCombat = checked(SettlementsThisCombat + 1);
                    executed++;
                }
                if (task.LoseAllAfter && canSettle?.Invoke() != false)
                {
                    var loss = LoseAll(kind, OrbScope.All, new(task.Source));
                    if (afterOperationAsync is not null && loss.Events.Count > 0) await afterOperationAsync(loss);
                }
            }

            _resolvedTurn = OwnerTurn;
            return new(false, executed, natural.AsReadOnly());
        }
        catch { _faulted = true; throw; }
        finally { _snapshotFrozen = false; _resolving = false; }
    }

    private void SwitchTo(OrbKind kind, OrbOrigin origin, List<OrbEvent> events)
    {
        // Orb locks prevent activation and settlement, not movement. Only SwitchLocked anchors the front slot.
        var movable = Enumerable.Range(0, 3).Where(i => !(i == 0 && SwitchLocked)).ToArray();
        var ordered = movable.Select(i => _positions[i]).ToList();
        if (!ordered.Contains(kind) || ordered[0] == kind) return;
        var before = Foreground;
        ordered.Remove(kind); ordered.Insert(0, kind);
        for (int i = 0; i < movable.Length; i++) _positions[movable[i]] = ordered[i];
        RecordPositionChange(kind, before, origin, events);
    }

    private void RecordPositionChange(OrbKind kind, OrbKind before, OrbOrigin origin, List<OrbEvent> events)
    {
        if (before != Foreground)
        {
            SwitchesThisTurn++;
            events.Add(Event(OrbEventKind.ForegroundChanged, Foreground, Value(Foreground), Value(Foreground), 0, origin, before));
        }
        else events.Add(Event(OrbEventKind.PositionsChanged, kind, Value(kind), Value(kind), 0, origin));
    }

    private void ActivateInternal(OrbKind kind, OrbOrigin origin, List<OrbEvent> events)
    {
        if (IsLocked(kind) || _active[(int)kind]) return;
        _active[(int)kind] = true;
        events.Add(Event(OrbEventKind.Activated, kind, Value(kind), Value(kind), 0, origin));
    }

    private OrbEvent Event(OrbEventKind type, OrbKind kind, int before, int after, int actual, OrbOrigin origin, OrbKind? previous = null)
    {
        _highWater[(int)kind] = Math.Max(_highWater[(int)kind], Math.Max(before, after));
        return new(++_eventSequence, OwnerId, OwnerTurn, type, kind, before, after, actual, previous, origin);
    }
    private OrbOrigin Normalize(OrbOrigin? origin, string fallback)
    {
        origin ??= new(fallback);
        return origin with { OriginOwnerId = origin.OriginOwnerId ?? OwnerId };
    }
    private static OrbOperationResult Result(int actual, List<OrbEvent> events) => new(OrbOperationStatus.Applied, actual, events.AsReadOnly());
    private bool InScope(OrbKind kind, OrbScope scope) => scope == OrbScope.All || (scope == OrbScope.Foreground ? kind == Foreground : kind != Foreground);
    private bool MatchesActivation(OrbKind kind, ActivationFilter filter) => filter == ActivationFilter.Any || _active[(int)kind] == (filter == ActivationFilter.Active);
    private void EnsureHealthy() { if (_faulted) throw new InvalidOperationException("This combat core has a failed external resolution; rebuild from the saved room boundary."); }
    private static void Nonnegative(int amount) { if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "Amounts cannot be negative."); }
    private static void Validate<T>(T value) where T : struct, Enum { if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value)); }
    private static void ValidateSelector(OrbSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Validate(selector.Scope); Validate(selector.Selection); Validate(selector.Activation);
        if (selector.Selection == OrbSelection.Named)
        {
            if (selector.Kind is not { } kind) throw new ArgumentException("Named selector requires an orb.");
            Validate(kind);
        }
        else if (selector.Kind is not null) throw new ArgumentException("Only a named selector accepts a kind.");
    }
    private sealed record ExtraTask(OrbSelector Selector, int Count, string Source, bool LoseAllAfter, long DueTurn);
}
