namespace Librarian.Core;

/// <summary>Disposable planning context. Only this copy is mutated; no native hooks or RNG are invoked.</summary>
public sealed class EndTurnBlockProjection(OrbCombatState original, WaveState waves, bool allowWaveGain = true)
{
    public OrbCombatState Orbs { get; } = original.CopyForPreview();
    public WaveState Waves { get; } = waves.CopyForPreview(allowWaveGain);
    public int TideContribution { get; private set; }
    public int WaveContribution { get; private set; }
    public int NewTidalBlock => checked(TideContribution + WaveContribution);
    public long ExpiringBlock { get; private set; }
    public long PotentialDamage { get; private set; }
    public int GrowthSettlements { get; private set; }
    public Action<OrbOperationResult>? OnOperation { get; set; }
    public Action<SettlementRequest>? AfterSettlement { get; set; }
    public Action<SettlementRequest>? BeforeSettlement { get; set; }
    public Action<int>? OrdinaryBlockGained { get; set; }
    public Action<int>? TidalBlockGained { get; set; }
    public Action<long>? BlockExpired { get; set; }

    public void Dispatch(OrbOperationResult operation) => OnOperation?.Invoke(operation);
    public ValueTask Settle(SettlementRequest request)
    {
        BeforeSettlement?.Invoke(request);
        int amount = request.EffectAmount();
        switch (request.Orb)
        {
            case OrbKind.Tide:
                TideContribution = checked(TideContribution + amount);
                Waves.TryRecordEndTurnTideContribution(Orbs.OwnerTurn, amount);
                TidalBlockGained?.Invoke(amount);
                Waves.Add(amount);
                break;
            case OrbKind.Growth:
                GrowthSettlements++;
                if (Orbs.LowestOther(OrbKind.Growth) is { } target)
                    Dispatch(Orbs.Strengthen(target, amount, OrbScope.All, new(request.Source)));
                break;
            case OrbKind.Fire:
                PotentialDamage = checked(PotentialDamage + amount);
                break;
        }
        AfterSettlement?.Invoke(request);
        return ValueTask.CompletedTask;
    }

    public async ValueTask Resolve(EndTurnRules rules, Action<EndTurnBlockProjection>? delayed = null, bool preventDecay = false)
    {
        Waves.BeginEndTurnPhase(Orbs.OwnerTurn);
        await Orbs.ResolveEndTurnAsync(rules, Settle,
            operation => { Dispatch(operation); return ValueTask.CompletedTask; },
            expired => { ExpiringBlock = expired.TotalRemoved; BlockExpired?.Invoke(ExpiringBlock); return ValueTask.CompletedTask; },
            () => { delayed?.Invoke(this); return ValueTask.CompletedTask; }, random: new NoPreviewRandom());
        WaveContribution = Waves.TakeEndTurnAmount(Orbs.OwnerTurn, preventDecay: preventDecay && Waves.Amount > 0);
        TidalBlockGained?.Invoke(WaveContribution);
    }

    private sealed class NoPreviewRandom : IOrbRandom
    {
        public int NextInt(int exclusiveMax) => throw new PreviewRandomRequiredException();
    }
}

public sealed class PreviewRandomRequiredException : InvalidOperationException;
