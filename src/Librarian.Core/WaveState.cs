namespace Librarian.Core;

/// <summary>Renewable end-turn Block source, independent of Block consumed by damage.</summary>
public sealed class WaveState
{
    public int Amount { get; private set; }
    public bool Retained { get; set; }
    public int RetentionFloor { get; set; }
    private long _lastEnd = -1;
    private long _legacyTideSettlement = -1;
    private long _phaseTurn = -1;
    private long _completedPhaseTurn = -1;
    private int _phaseFrozenAmount;
    private int _phaseTideContribution;

    /// <summary>
    /// Freezes the Waves value used by the current owner's end-turn payout.  The
    /// freeze deliberately happens before any end-turn card/orb action, so Waves
    /// created by one of those actions cannot raise the same turn's comparison
    /// baseline.
    /// </summary>
    public void BeginEndTurnPhase(long turn)
    {
        ValidateTurn(turn);
        if (_completedPhaseTurn == turn)
            return;
        if (_phaseTurn == turn)
            return;
        if (_phaseTurn > turn)
            throw new ArgumentOutOfRangeException(nameof(turn), "End-turn phases must be monotonic.");
        _phaseTurn = turn;
        _phaseFrozenAmount = Amount;
        _phaseTideContribution = 0;
    }

    /// <summary>
    /// Records an end-turn Tide contribution after the native Unpowered and
    /// background-half calculation, but before native Block modifiers.  The
    /// ledger receives the actual Block separately; this number is only the
    /// comparison contribution and is never derived from the Block hook result.
    /// </summary>
    public void RecordEndTurnTideContribution(long turn, int contribution)
    {
        ValidateTurn(turn);
        if (contribution < 0)
            throw new ArgumentOutOfRangeException(nameof(contribution));
        if (_phaseTurn != turn)
            throw new InvalidOperationException("BeginEndTurnPhase must precede end-turn Tide settlement.");
        _phaseTideContribution = checked(_phaseTideContribution + contribution);
    }

    public bool IsEndTurnPhase(long turn) => _phaseTurn == turn;

    /// <summary>Records only while an explicit end-turn phase is open.</summary>
    public bool TryRecordEndTurnTideContribution(long turn, int contribution)
    {
        if (!IsEndTurnPhase(turn)) return false;
        RecordEndTurnTideContribution(turn, contribution);
        return true;
    }

    /// <summary>Compatibility marker for callers that predate the v0.4.1 phase API.</summary>
    public void RecordTideSettlement(long turn) => _legacyTideSettlement = turn;

    /// <summary>
    /// Compatibility overload.  During an explicit end-turn phase the supplied
    /// amount is the pre-modifier settlement contribution; outside that phase it
    /// retains the old once-per-turn suppression behavior.
    /// </summary>
    public void RecordTideSettlement(long turn, int contribution)
    {
        if (_phaseTurn == turn)
            RecordEndTurnTideContribution(turn, contribution);
        else
            RecordTideSettlement(turn);
    }

    public int TakeEndTurnAmount(long turn, bool fullFrozenWaves = false, bool preventDecay = false)
    {
        ValidateTurn(turn);
        if (turn <= _lastEnd) return 0;
        _lastEnd = turn;
        int payout;
        if (_phaseTurn == turn)
        {
            // Tide already paid its own pre-modifier contribution as Block.  Only
            // the frozen Waves shortfall is paid here; newly created Waves remain
            // in Amount and decay using the normal retention rules below.
            int target = Math.Max(_phaseFrozenAmount, _phaseTideContribution);
            payout = fullFrozenWaves ? _phaseFrozenAmount : Math.Max(0, target - _phaseTideContribution);
            _phaseTurn = -1;
            _completedPhaseTurn = turn;
            _phaseFrozenAmount = 0;
            _phaseTideContribution = 0;
        }
        else
        {
            payout = !fullFrozenWaves && _legacyTideSettlement == turn ? 0 : Amount;
        }
        if (!Retained && !preventDecay) Amount = Math.Min(Amount, Math.Max(Amount / 2, RetentionFloor));
        return payout;
    }
    public void Add(int amount)
    {
        if (amount < 0) throw new System.ArgumentOutOfRangeException(nameof(amount));
        Amount = checked(Amount + amount);
    }
    public void StartTurn(long turn)
    {
        // Decay belongs to the completed end turn, after deciding its payout.
    }

    private static void ValidateTurn(long turn)
    {
        if (turn < 0) throw new ArgumentOutOfRangeException(nameof(turn));
    }
}
