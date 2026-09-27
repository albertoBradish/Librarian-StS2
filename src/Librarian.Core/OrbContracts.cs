namespace Librarian.Core;

public enum OrbKind { Fire, Tide, Growth }
public enum OrbScope { Foreground, Background, All }
public enum ActivationFilter { Any, Active, Inactive }
public enum OrbSelection { Named, Lowest, Highest, Random }
public enum OrbEventKind { ForegroundChanged, Gained, Strengthened, Activated, Lost, Zeroed, Extinguished, ValuesPermuted, PositionsChanged, Locked, Unlocked, Channelled, Imbued }
public enum OrbOperationStatus { Applied, OutOfScope, Blocked }
public enum SettlementReason { Immediate, NaturalEndTurn, ExtraEndTurn }

/// <summary>Only action resolution may call this; snapshot/candidate queries never do.</summary>
public interface IOrbRandom { int NextInt(int exclusiveMax); }

public sealed record OrbOrigin(string Source, string? OriginOwnerId = null, string? PropagationId = null, bool IsPropagated = false);
public sealed record OrbView(OrbKind Kind, int Value, bool IsActivated, bool IsForeground, int LockedTurns = 0)
{
    public const int PermanentLock = -1;
    public bool IsLocked => LockedTurns != 0;
    public bool IsPermanentlyLocked => LockedTurns == PermanentLock;
}
public sealed record OrbCombatSnapshot(string OwnerId, long OwnerTurn, OrbKind Foreground,
    IReadOnlyList<OrbView> Orbs, long ZeroCount, long LossCount, long SwitchesThisTurn, bool TideGainBlocked)
{
    public OrbView this[OrbKind kind] => Orbs.Single(x => x.Kind == kind);
}

/// <summary>Sequence is local to one combat instance. Origin is carried through for future propagation guards.</summary>
public sealed record OrbEvent(long Sequence, string OwnerId, long OwnerTurn, OrbEventKind Kind,
    OrbKind Orb, int BeforeValue, int AfterValue, int ActualAmount, OrbKind? PreviousForeground, OrbOrigin Origin);

/// <summary>State has already changed. The adapter dispatches these events before starting its next operation.</summary>
public sealed record OrbOperationResult(OrbOperationStatus Status, int ActualAmount, IReadOnlyList<OrbEvent> Events)
{
    internal static OrbOperationResult Empty(OrbOperationStatus status) => new(status, 0, Array.Empty<OrbEvent>());
}

/// <summary>Scope/filter is resolved at action time; extra end-turn selectors resolve once when tasks freeze.</summary>
public sealed record OrbSelector(OrbScope Scope, OrbSelection Selection, OrbKind? Kind = null,
    ActivationFilter Activation = ActivationFilter.Any)
{
    public static OrbSelector Named(OrbKind kind, OrbScope scope = OrbScope.Foreground,
        ActivationFilter activation = ActivationFilter.Any) => new(scope, OrbSelection.Named, kind, activation);
    public static OrbSelector Lowest(OrbScope scope, ActivationFilter activation = ActivationFilter.Any) => new(scope, OrbSelection.Lowest, null, activation);
    public static OrbSelector Highest(OrbScope scope, ActivationFilter activation = ActivationFilter.Any) => new(scope, OrbSelection.Highest, null, activation);
    public static OrbSelector Random(OrbScope scope, ActivationFilter activation = ActivationFilter.Any) => new(scope, OrbSelection.Random, null, activation);
}

public sealed record EndTurnRules(OrbScope NaturalScope = OrbScope.Foreground, bool PreserveActivation = false, bool HalfBackground = false, bool PreserveBackgroundActivation = false);
public sealed record SettlementRequest(string OwnerId, long OwnerTurn, OrbKind Orb, int Value,
    SettlementReason Reason, string Source, int Repetition, int RepetitionCount, bool HalfEffect = false)
{
    public int EffectAmount(int multiplier = 1)
    {
        int full = checked(Value * multiplier);
        return HalfEffect ? full / 2 : full;
    }
}
public sealed record EndTurnResult(bool AlreadyResolved, int SettlementsExecuted, IReadOnlyList<OrbKind> NaturalParticipants);
