using Librarian.Core;

var tests = new List<(string Id, string Name, Func<Task> Run)>();
void Test(string id, string name, Action run) => tests.Add((id, name, () => { run(); return Task.CompletedTask; }));
void AsyncTest(string id, string name, Func<Task> run) => tests.Add((id, name, run));

Test("061Waves", "One-shot retention preserves combined Waves without altering frozen payout or future decay", () =>
{
    var w=new WaveState();w.Add(9);w.BeginEndTurnPhase(1);w.RecordEndTurnTideContribution(1,4);w.Add(4);
    Check.Equal(5,w.TakeEndTurnAmount(1,preventDecay:true));Check.Equal(13,w.Amount);
    Check.Equal(0,w.TakeEndTurnAmount(1));Check.Equal(13,w.Amount);
    w.BeginEndTurnPhase(2);Check.Equal(13,w.TakeEndTurnAmount(2));Check.Equal(6,w.Amount);
    w.RetentionFloor=5;w.BeginEndTurnPhase(3);Check.Equal(6,w.TakeEndTurnAmount(3,preventDecay:true));Check.Equal(6,w.Amount);
    w.BeginEndTurnPhase(4);w.TakeEndTurnAmount(4);Check.Equal(5,w.Amount);
});

Test("043Peak", "Orb history records permutations and gains, survives loss/turns, stays combat-local", () =>
{
    var s=new OrbCombatState("peak");s.Strengthen(OrbKind.Fire,20,OrbScope.All);s.Gain(OrbKind.Tide,9);
    s.PermuteValues(OrbKind.Growth,OrbKind.Fire,OrbKind.Tide);s.LoseAll(OrbKind.Tide,OrbScope.All);s.BeginOwnerTurn();
    Check.Equal(20,s.HighestValueThisCombat(OrbKind.Fire));Check.Equal(20,s.HighestValueThisCombat(OrbKind.Tide));Check.Equal(9,s.HighestValueThisCombat(OrbKind.Growth));
    Check.Equal(0,new OrbCombatState("next").HighestValueThisCombat(OrbKind.Fire));
});
Test("043Channel", "One imbue per successful operation including repeat/zero; locked gain not an imbue", () =>
{
    var s=new OrbCombatState("imbue");
    Check.Equal(1,s.Gain(OrbKind.Fire,0).Events.Count(e=>e.Kind==OrbEventKind.Imbued));
    Check.Equal(1,s.Activate(OrbKind.Fire).Events.Count(e=>e.Kind==OrbEventKind.Imbued));
    Check.Equal(1,s.ActivateWithoutSwitch(OrbKind.Fire).Events.Count(e=>e.Kind==OrbEventKind.Imbued));
    s.Lock(OrbKind.Fire);Check.Equal(0,s.Gain(OrbKind.Fire,9).Events.Count(e=>e.Kind==OrbEventKind.Imbued));
    Check.Equal(0,s.Activate(OrbKind.Fire).Events.Count(e=>e.Kind==OrbEventKind.Imbued));
});
Test("043Waves", "Full frozen payout excludes new waves and decays once", () =>
{
    var w=new WaveState();w.Add(15);w.BeginEndTurnPhase(1);w.RecordEndTurnTideContribution(1,10);w.Add(10);
    Check.Equal(15,w.TakeEndTurnAmount(1,true));Check.Equal(12,w.Amount);Check.Equal(0,w.TakeEndTurnAmount(1,true));Check.Equal(12,w.Amount);
});

Test("036Shared", "Borrowed settlement counts without mutating recipient locked orb state", () =>
{
    var s = new OrbCombatState("recipient");
    s.Gain(OrbKind.Fire, 31); s.Lock(OrbKind.Fire);
    var ring = s.Positions.ToArray();
    s.RecordSharedSettlement();
    Check.Equal(1, s.SettlementsThisTurn); Check.Equal(1, s.SettlementsThisCombat);
    Check.Equal(31, s.Value(OrbKind.Fire)); Check.True(s.IsLocked(OrbKind.Fire)); Check.False(s.IsActivated(OrbKind.Fire));
    Check.Sequence(ring, s.Positions);
    s.BeginOwnerTurn();
    Check.Equal(0, s.SettlementsThisTurn); Check.Equal(1, s.SettlementsThisCombat);
});

Test("035Unlock", "Explicit unlock clears finite and permanent locks without activating or moving", () =>
{
    var s = new OrbCombatState("unlock");
    s.Gain(OrbKind.Fire, 8); s.Gain(OrbKind.Tide, 6);
    s.Lock(OrbKind.Fire); s.Lock(OrbKind.Tide, 3);
    var before = s.Positions.ToArray();
    foreach (var kind in before) s.Unlock(kind);
    Check.Sequence(before, s.Positions);
    Check.Equal(8, s.Value(OrbKind.Fire)); Check.Equal(6, s.Value(OrbKind.Tide));
    Check.True(s.Snapshot().Orbs.All(o => !o.IsLocked && !o.IsActivated));
    Check.Equal(0, s.Unlock(OrbKind.Fire).Events.Count);
});

Test("035Higher", "Growth higher target excludes source and permits locked strengthening targets", () =>
{
    var s = new OrbCombatState("vines");
    s.Strengthen(OrbKind.Growth, 99, OrbScope.All);
    s.Strengthen(OrbKind.Fire, 4, OrbScope.All); s.Strengthen(OrbKind.Tide, 7, OrbScope.All);
    s.Lock(OrbKind.Tide);
    Check.Equal<OrbKind?>(OrbKind.Tide, s.HighestOther(OrbKind.Growth));
    s.Strengthen(OrbKind.Fire, 3, OrbScope.All);
    Check.Equal(s.LowestOther(OrbKind.Growth), s.HighestOther(OrbKind.Growth));
});

Test("VIS01", "Centered values and lock counters preserve finite and permanent semantics", () =>
{
    foreach (int turns in new[] { 0, 1, 99, 100, OrbView.PermanentLock })
    {
        var view = new OrbView(OrbKind.Fire, 123, false, true, turns);
        string expected = turns == 0 ? "123" : turns == 100 || turns == OrbView.PermanentLock ? "∞" : turns.ToString();
        Check.Equal(expected, OrbPresentation.CenterText(view));
        Check.Equal(turns == OrbView.PermanentLock, view.IsPermanentlyLocked);
    }
    var s = new OrbCombatState("display");
    s.Lock(OrbKind.Fire, 99); s.Lock(OrbKind.Fire, 1);
    Check.Equal(100, s.LockedTurns(OrbKind.Fire));
    Check.False(s.Snapshot()[OrbKind.Fire].IsPermanentlyLocked);
    Check.Equal("∞", OrbPresentation.CenterText(s.Snapshot()[OrbKind.Fire]));
    s.BeginOwnerTurn();
    Check.Equal("99", OrbPresentation.CenterText(s.Snapshot()[OrbKind.Fire]));
    s.Strengthen(OrbKind.Fire, 7);
    Check.Equal(7, s.Value(OrbKind.Fire));
    s.Lock(OrbKind.Fire); s.Lock(OrbKind.Fire, 100); s.BeginOwnerTurn();
    Check.Equal(OrbView.PermanentLock, s.LockedTurns(OrbKind.Fire));
    Check.Equal("∞", OrbPresentation.CenterText(s.Snapshot()[OrbKind.Fire]));
    var one = new OrbCombatState("one"); one.Lock(OrbKind.Tide, 1); one.BeginOwnerTurn();
    Check.Equal("0", OrbPresentation.CenterText(one.Snapshot()[OrbKind.Tide]));
});

Test("O01", "Fresh combat has one foreground, no implicit relic, and player isolation", () =>
{
    var a = new OrbCombatState("a"); var b = new OrbCombatState("b");
    Check.Equal(OrbKind.Fire, a.Foreground);
    Check.Equal(1, a.Snapshot().Orbs.Count(o => o.IsForeground));
    Check.True(a.Snapshot().Orbs.All(o => o.Value == 0 && !o.IsActivated));
    a.Gain(OrbKind.Fire, 4, new("starting-relic"));
    a.BlockLedger.RecordTideGain(10, 0);
    Check.Equal(0, b.Value(OrbKind.Fire)); Check.Equal(0L, b.BlockLedger.Total);
});

Test("O02", "Gain switches then adds and activates; same foreground is not another switch", () =>
{
    var s = new OrbCombatState("a");
    var origin = new OrbOrigin("card-1", "source-owner", "prop-1", true);
    var result = s.Gain(OrbKind.Tide, 3, origin);
    Check.Sequence(new[] { OrbEventKind.ForegroundChanged, OrbEventKind.Gained, OrbEventKind.Activated, OrbEventKind.Imbued }, result.Events.Select(e => e.Kind));
    Check.True(result.Events.All(e => e.OwnerId == "a" && e.Origin == origin));
    Check.Sequence(new long[] { 1, 2, 3, 4 }, result.Events.Select(e => e.Sequence));
    var second = s.Gain(OrbKind.Tide, 2);
    Check.Sequence(new[] { OrbEventKind.Gained, OrbEventKind.Imbued }, second.Events.Select(e => e.Kind));
    Check.Equal(5, s.Value(OrbKind.Tide)); Check.Equal(1L, s.SwitchesThisTurn);
    Check.Equal(OrbKind.Tide, s.Foreground);
});

Test("O03", "Strengthening a background requires explicit scope and never activates or switches", () =>
{
    var s = new OrbCombatState("a");
    Check.Equal(OrbOperationStatus.OutOfScope, s.Strengthen(OrbKind.Growth, 7).Status);
    Check.Equal(0, s.Value(OrbKind.Growth));
    var result = s.Strengthen(OrbKind.Growth, 7, OrbScope.Background);
    Check.Equal(OrbKind.Fire, s.Foreground); Check.False(s.IsActivated(OrbKind.Growth));
    Check.Sequence(new[] { OrbEventKind.Strengthened }, result.Events.Select(e => e.Kind));
    s.Activate(OrbKind.Growth, OrbScope.All);
    Check.True(s.IsActivated(OrbKind.Growth)); Check.Equal(OrbKind.Growth, s.Foreground);
});

Test("O04", "Ordinary zero gain activates; blocked Tide gain cancels every aspect, strengthening remains", () =>
{
    var s = new OrbCombatState("a"); s.BeginOwnerTurn();
    s.BlockTideGainForThisTurn();
    var result = s.Gain(OrbKind.Tide, 9);
    Check.Equal(OrbOperationStatus.Blocked, result.Status); Check.Equal(0, result.Events.Count);
    Check.Equal(OrbOperationStatus.Blocked, s.Gain(OrbKind.Tide, 0).Status);
    Check.Equal(OrbKind.Fire, s.Foreground); Check.False(s.IsActivated(OrbKind.Tide));
    s.Strengthen(OrbKind.Tide, 2, OrbScope.All);
    Check.Equal(2, s.Value(OrbKind.Tide)); Check.False(s.IsActivated(OrbKind.Tide));
    s.BeginOwnerTurn(); var zero = s.Gain(OrbKind.Tide, 0);
    Check.Equal(OrbOperationStatus.Applied, zero.Status);
    Check.Equal(OrbKind.Tide, s.Foreground); Check.True(s.IsActivated(OrbKind.Tide));
    Check.Equal(2, s.Value(OrbKind.Tide));
});

Test("O05", "Loss uses actual amount; every positive-to-zero counts once, repeated zero does not", () =>
{
    var s = new OrbCombatState("a"); s.Gain(OrbKind.Tide, 2);
    var loss = s.Lose(OrbKind.Tide, 6);
    Check.Equal(2, loss.ActualAmount);
    Check.Sequence(new[] { OrbEventKind.Lost, OrbEventKind.Zeroed }, loss.Events.Select(e => e.Kind));
    Check.Equal(1L, s.ZeroCount); Check.True(s.IsActivated(OrbKind.Tide));
    Check.Equal(0, s.LoseAll(OrbKind.Tide).Events.Count);
    Check.Equal(0, s.Lose(OrbKind.Tide, 0).Events.Count);
    s.Gain(OrbKind.Tide, 1); s.Lose(OrbKind.Tide, 1);
    Check.Equal(2L, s.ZeroCount); Check.Equal(2L, s.LossCount);
    s.Gain(OrbKind.Fire, 5); s.LoseHalf(OrbKind.Fire);
    Check.Equal(3, s.Value(OrbKind.Fire));
});

Test("O06", "Named loss/extinguish respect foreground; extinguishing is a transition, independent from value", () =>
{
    var s = new OrbCombatState("a"); s.Gain(OrbKind.Fire, 4); s.Gain(OrbKind.Tide, 1);
    Check.Equal(OrbOperationStatus.OutOfScope, s.LoseAll(OrbKind.Fire).Status);
    Check.Equal(OrbOperationStatus.OutOfScope, s.Extinguish(OrbKind.Fire).Status);
    Check.True(s.IsActivated(OrbKind.Fire));
    var extinction = s.Extinguish(OrbKind.Fire, OrbScope.Background);
    Check.Sequence(new[] { OrbEventKind.Extinguished }, extinction.Events.Select(e => e.Kind));
    Check.Equal(4, s.Value(OrbKind.Fire)); Check.True(s.IsActivated(OrbKind.Tide));
    Check.Equal(0, s.Extinguish(OrbKind.Fire, OrbScope.All).Events.Count);
});

Test("O07", "Scope and activation queries are explicit and snapshots remain immutable", () =>
{
    var s = new OrbCombatState("a"); s.Gain(OrbKind.Fire, 2); s.Gain(OrbKind.Tide, 3);
    var snapshot = s.Snapshot();
    Check.Sequence(new[] { OrbKind.Fire, OrbKind.Growth }, s.Select(OrbScope.Background));
    Check.Sequence(new[] { OrbKind.Fire }, s.Select(OrbScope.Background, ActivationFilter.Active));
    s.Gain(OrbKind.Growth, 7);
    Check.Equal(OrbKind.Tide, snapshot.Foreground); Check.Equal(0, snapshot[OrbKind.Growth].Value);
    Check.Throws<NotSupportedException>(() => ((IList<OrbView>)snapshot.Orbs)[0] = new(OrbKind.Fire, 999, true, true));
});

Test("O08", "Preview and deterministic ties do not advance RNG; lowest recomputes after loss", () =>
{
    var s = new OrbCombatState("a"); var rng = new ScriptedRandom(1);
    for (int i = 0; i < 100; i++) { s.Snapshot(); s.GetCandidates(OrbSelector.Lowest(OrbScope.All)); }
    Check.Equal(0, rng.Calls);
    Check.Equal(OrbKind.Tide, s.ResolveSelector(OrbSelector.Lowest(OrbScope.All), rng)!.Value);
    Check.Equal(0, rng.Calls);
    s.Strengthen(OrbKind.Fire, 10, OrbScope.All); s.Strengthen(OrbKind.Tide, 8, OrbScope.All); s.Strengthen(OrbKind.Growth, 7, OrbScope.All);
    var highest = s.ResolveSelector(OrbSelector.Highest(OrbScope.All))!.Value;
    s.Lose(highest, 4, OrbScope.All);
    Check.Equal(OrbKind.Fire, s.ResolveSelector(OrbSelector.Lowest(OrbScope.All))!.Value);
    Check.Equal(0, rng.Calls);
});

Test("O09", "Value permutation preserves total, activation and positions without loss/zero events", () =>
{
    var s = new OrbCombatState("a"); s.Gain(OrbKind.Fire, 10); s.Strengthen(OrbKind.Tide, 2, OrbScope.All);
    var result = s.PermuteValues(OrbKind.Tide, OrbKind.Growth, OrbKind.Fire);
    Check.Sequence(new[] { 2, 0, 10 }, s.Snapshot().Orbs.Select(o => o.Value));
    Check.Equal(OrbKind.Fire, s.Foreground); Check.True(s.IsActivated(OrbKind.Fire));
    Check.False(s.IsActivated(OrbKind.Growth)); Check.Equal(0L, s.ZeroCount); Check.Equal(0L, s.LossCount);
    Check.True(result.Events.All(e => e.Kind == OrbEventKind.ValuesPermuted));
});

Test("O10", "First-per-turn and boolean rules do not stack, reset only with owner turn", () =>
{
    var s = new OrbCombatState("a");
    Check.True(s.TryMarkFirstThisTurn("first-loss")); Check.False(s.TryMarkFirstThisTurn("first-loss"));
    Check.True(s.TryMarkFirstThisTurn("different-effect"));
    s.Gain(OrbKind.Tide, 0); s.BlockTideGainForThisTurn(); s.BlockTideGainForThisTurn();
    s.BeginOwnerTurn(); Check.True(s.TryMarkFirstThisTurn("first-loss"));
    Check.Equal(0L, s.SwitchesThisTurn); Check.False(s.TideGainBlocked);
});

AsyncTest("O11", "Natural turn end settles only activated foreground and does not remove values", async () =>
{
    var s = new OrbCombatState("a"); s.BeginOwnerTurn(); s.Gain(OrbKind.Fire, 4); s.Gain(OrbKind.Tide, 2);
    var requests = new List<SettlementRequest>();
    var result = await s.ResolveEndTurnAsync(new(), r => { requests.Add(r); return ValueTask.CompletedTask; });
    Check.Equal(1, result.SettlementsExecuted); Check.Equal(OrbKind.Tide, requests.Single().Orb);
    Check.False(s.IsActivated(OrbKind.Tide)); Check.True(s.IsActivated(OrbKind.Fire)); Check.Equal(2, s.Value(OrbKind.Tide));
    Check.True((await s.ResolveEndTurnAsync(new(), _ => throw new Exception("Duplicate resolution"))).AlreadyResolved);
});

AsyncTest("O12", "Expiry precedes delayed gain; delayed phase changes natural snapshot", async () =>
{
    var s = new OrbCombatState("a"); s.BeginOwnerTurn(); s.Gain(OrbKind.Fire, 4);
    s.BlockLedger.RecordTideGain(5, 0); s.BlockLedger.RecordOrdinaryGain(9, 0);
    var order = new List<string>();
    await s.ResolveEndTurnAsync(new(), r =>
    {
        order.Add($"settle:{r.Orb}:{r.Value}");
        if (r.Orb == OrbKind.Tide) s.BlockLedger.RecordTideGain(r.Value, s.OwnerTurn);
        return ValueTask.CompletedTask;
    }, expireBlockAsync: change => { order.Add($"expire:{change.TotalRemoved}"); return ValueTask.CompletedTask; },
    delayedAsync: () => { order.Add("delay"); s.Gain(OrbKind.Tide, 3); return ValueTask.CompletedTask; });
    Check.Sequence(new[] { "expire:5", "delay", "settle:Tide:3" }, order);
    Check.Equal(9L, s.BlockLedger.OrdinaryRemaining); Check.Equal(3L, s.BlockLedger.TideRemaining);
});

AsyncTest("O13", "Natural all follows the ring and reads value at each execution", async () =>
{
    var s = new OrbCombatState("a"); s.Gain(OrbKind.Fire, 1); s.Gain(OrbKind.Tide, 2); s.Gain(OrbKind.Growth, 3);
    s.QueueExtraSettlement(OrbSelector.Named(OrbKind.Fire, OrbScope.All), 2, "extra-fire");
    var requests = new List<SettlementRequest>();
    await s.ResolveEndTurnAsync(new(OrbScope.All, true), r =>
    {
        requests.Add(r);
        if (r.Orb == OrbKind.Growth) s.Strengthen(OrbKind.Fire, r.Value, OrbScope.All);
        if (r.Reason == SettlementReason.ExtraEndTurn) s.Strengthen(OrbKind.Fire, 1, OrbScope.All);
        return ValueTask.CompletedTask;
    });
    Check.Sequence(new[] { OrbKind.Growth, OrbKind.Tide, OrbKind.Fire, OrbKind.Fire, OrbKind.Fire }, requests.Select(r => r.Orb));
    Check.Sequence(new[] { 3, 2, 4, 4, 5 }, requests.Select(r => r.Value));
    Check.True(s.Snapshot().Orbs.All(o => o.IsActivated));
});

AsyncTest("O14", "Settled participants may reignite without repeating their natural settlement", async () =>
{
    var s = new OrbCombatState("a"); s.Gain(OrbKind.Fire, 12); s.Gain(OrbKind.Tide, 2);
    int callbackCount = 0;
    var result = await s.ResolveEndTurnAsync(new(OrbScope.All), _ => ValueTask.CompletedTask, afterOperationAsync: operation =>
    {
        callbackCount++;
        if (operation.Events.Single().Orb == OrbKind.Fire)
        {
            Check.False(s.IsActivated(OrbKind.Tide));
            s.Gain(OrbKind.Fire, 1);
            s.Gain(OrbKind.Tide, 1);
        }
        return ValueTask.CompletedTask;
    });
    Check.Equal(2, result.SettlementsExecuted); Check.Equal(2, callbackCount);
    Check.True(s.IsActivated(OrbKind.Fire)); Check.True(s.IsActivated(OrbKind.Tide));
    Check.Equal(13, s.Value(OrbKind.Fire)); Check.Equal(3, s.Value(OrbKind.Tide));
});

AsyncTest("O15", "Suppression skips natural and extra Fire, preserves activation, immediate still works", async () =>
{
    var s = new OrbCombatState("a"); s.BeginOwnerTurn(); s.Gain(OrbKind.Fire, 9);
    s.SuppressEndTurnSettlement(OrbKind.Fire); s.QueueExtraSettlement(OrbSelector.Named(OrbKind.Fire), 3, "extra");
    int immediate = await s.SettleImmediatelyAsync(OrbSelector.Named(OrbKind.Fire), 2, _ => ValueTask.CompletedTask, "now");
    Check.Equal(2, immediate);
    var end = await s.ResolveEndTurnAsync(new(), _ => throw new Exception("Suppressed Fire settled"));
    Check.Equal(0, end.SettlementsExecuted); Check.True(s.IsActivated(OrbKind.Fire));
    s.BeginOwnerTurn();
    Check.Equal(1, (await s.ResolveEndTurnAsync(new(), _ => ValueTask.CompletedTask)).SettlementsExecuted);
});

AsyncTest("O16", "Extra target/count lock at snapshot, repeat reads current value and clears once", async () =>
{
    var s = new OrbCombatState("a");
    s.Strengthen(OrbKind.Fire, 5, OrbScope.All); s.Strengthen(OrbKind.Tide, 4, OrbScope.All); s.Strengthen(OrbKind.Growth, 2, OrbScope.All);
    s.QueueExtraSettlement(OrbSelector.Lowest(OrbScope.All), 2, "captured-x", true);
    var requests = new List<SettlementRequest>(); var lossEvents = new List<OrbEvent>();
    await s.ResolveEndTurnAsync(new(), r =>
    {
        requests.Add(r); s.Strengthen(OrbKind.Growth, 10, OrbScope.All); return ValueTask.CompletedTask;
    }, afterOperationAsync: op => { lossEvents.AddRange(op.Events); return ValueTask.CompletedTask; });
    Check.Sequence(new[] { OrbKind.Growth, OrbKind.Growth }, requests.Select(r => r.Orb));
    Check.Sequence(new[] { 2, 12 }, requests.Select(r => r.Value));
    Check.Equal(0, s.Value(OrbKind.Growth)); Check.Equal(1L, s.ZeroCount);
    Check.Equal(22, lossEvents.Single(e => e.Kind == OrbEventKind.Lost).ActualAmount);
});

AsyncTest("O17", "Zero repeat extra task still clears chosen orb; inactive background is allowed only explicitly", async () =>
{
    var s = new OrbCombatState("a"); s.Strengthen(OrbKind.Tide, 2, OrbScope.All);
    s.QueueExtraSettlement(OrbSelector.Named(OrbKind.Tide, OrbScope.Background), 0, "x-zero", true);
    var result = await s.ResolveEndTurnAsync(new(), _ => throw new Exception("Zero repeat settled"));
    Check.Equal(0, result.SettlementsExecuted); Check.Equal(0, s.Value(OrbKind.Tide)); Check.Equal(1L, s.ZeroCount);
});

AsyncTest("O18", "Tasks added during frozen settlement wait until next owner turn", async () =>
{
    var s = new OrbCombatState("a"); s.BeginOwnerTurn(); s.Gain(OrbKind.Fire, 1);
    var first = await s.ResolveEndTurnAsync(new(), _ =>
    {
        s.QueueExtraSettlement(OrbSelector.Named(OrbKind.Tide, OrbScope.All), 1, "late");
        return ValueTask.CompletedTask;
    });
    Check.Equal(1, first.SettlementsExecuted);
    s.BeginOwnerTurn(); var requests = new List<SettlementRequest>();
    await s.ResolveEndTurnAsync(new(), r => { requests.Add(r); return ValueTask.CompletedTask; });
    Check.Equal("late", requests.Single().Source); Check.Equal(OrbKind.Tide, requests.Single().Orb);
});

AsyncTest("O19", "Failed external settlement is not silently replayed; a fresh combat is independent", async () =>
{
    var s = new OrbCombatState("a"); s.Gain(OrbKind.Fire, 1); int calls = 0;
    await Check.ThrowsAsync<ApplicationException>(async () => await s.ResolveEndTurnAsync(new(), _ => { calls++; throw new ApplicationException("engine failure"); }));
    Check.True(s.IsFaulted);
    await Check.ThrowsAsync<InvalidOperationException>(async () => await s.ResolveEndTurnAsync(new(), _ => { calls++; return ValueTask.CompletedTask; }));
    Check.Equal(1, calls); Check.Throws<InvalidOperationException>(() => s.Gain(OrbKind.Fire, 1));
    Check.Equal(0, new OrbCombatState("a").Value(OrbKind.Fire));
});

AsyncTest("O20", "Nested end-turn execution is rejected without creating duplicate effects", async () =>
{
    var s = new OrbCombatState("a"); s.Gain(OrbKind.Fire, 1);
    var result = await s.ResolveEndTurnAsync(new(), async _ =>
    {
        await Check.ThrowsAsync<InvalidOperationException>(async () => await s.ResolveEndTurnAsync(new(), _ => throw new Exception("nested")));
    });
    Check.Equal(1, result.SettlementsExecuted); Check.False(s.IsFaulted);
});

Test("O21", "Invalid and overflowing inputs leave meaningful state unchanged", () =>
{
    var s = new OrbCombatState("a"); s.Strengthen(OrbKind.Tide, int.MaxValue, OrbScope.All);
    Check.Throws<OverflowException>(() => s.Gain(OrbKind.Tide, 1));
    Check.Equal(OrbKind.Fire, s.Foreground); Check.False(s.IsActivated(OrbKind.Tide)); Check.Equal(int.MaxValue, s.Value(OrbKind.Tide));
    Check.Throws<ArgumentOutOfRangeException>(() => s.Gain(OrbKind.Fire, -1));
    Check.Throws<ArgumentOutOfRangeException>(() => s.Lose(OrbKind.Fire, -1));
    Check.Throws<ArgumentOutOfRangeException>(() => s.Select((OrbScope)99));
    Check.Throws<ArgumentException>(() => s.PermuteValues(OrbKind.Fire, OrbKind.Fire, OrbKind.Growth));
    Check.Throws<ArgumentNullException>(() => s.ResolveSelector(OrbSelector.Random(OrbScope.All)));
    Check.Equal(0L, s.ZeroCount);
});

Test("O22", "Same room boundary and action sequence reproduce selection and state despite previews", () =>
{
    static (OrbCombatSnapshot Snapshot, OrbKind[] Choices) Play(bool preview)
    {
        var s = new OrbCombatState("a"); var rng = new SeededRandom(739); var choices = new List<OrbKind>();
        s.Gain(OrbKind.Fire, 4); s.BeginOwnerTurn();
        for (int i = 0; i < 30; i++)
        {
            if (preview) for (int p = 0; p < 100; p++) { s.Snapshot(); s.GetCandidates(OrbSelector.Random(OrbScope.All)); }
            var kind = s.ResolveSelector(OrbSelector.Random(OrbScope.All), rng)!.Value;
            choices.Add(kind); s.Gain(kind, i % 4); s.LoseHalf(kind);
        }
        return (s.Snapshot(), choices.ToArray());
    }
    var a = Play(false); var b = Play(true);
    Check.Sequence(a.Choices, b.Choices); Check.Sequence(a.Snapshot.Orbs, b.Snapshot.Orbs);
    Check.Equal(a.Snapshot.ZeroCount, b.Snapshot.ZeroCount);
});

AsyncTest("O23", "Zero settlements without a final loss have no effects and do not advance RNG", async () =>
{
    var s = new OrbCombatState("a"); var rng = new ScriptedRandom(1);
    s.QueueExtraSettlement(OrbSelector.Lowest(OrbScope.All), 0, "no-extra");
    Check.Equal(0, await s.SettleImmediatelyAsync(OrbSelector.Random(OrbScope.All), 0,
        _ => throw new Exception("No immediate effect expected"), "no-immediate", rng));
    await s.ResolveEndTurnAsync(new(), _ => throw new Exception("No extra effect expected"), random: rng);
    Check.Equal(0, rng.Calls);
});

Test("B01", "Mixed ordinary/Tide loss consumes actual earliest amount and Tide expires next owner end", () =>
{
    var ledger = new TideBlockLedger();
    ledger.RecordOrdinaryGain(10, 5); ledger.RecordTideGain(20, 5);
    var loss = ledger.Consume(12);
    Check.Equal(12L, loss.TotalRemoved);
    Check.Sequence(new[] { BlockBatchKind.Ordinary, BlockBatchKind.Tide }, loss.Batches.Select(b => b.Kind));
    Check.Sequence(new[] { 10, 2 }, loss.Batches.Select(b => b.Removed));
    Check.Equal(0L, ledger.Expire(5, BlockExpiryPhase.OwnerTurnEnd, false).TotalRemoved);
    Check.Equal(0L, ledger.Expire(6, BlockExpiryPhase.OwnerTurnStart, false).TotalRemoved);
    Check.Equal(18L, ledger.Expire(6, BlockExpiryPhase.OwnerTurnEnd, false).TotalRemoved);
    Check.Equal(0L, ledger.Expire(6, BlockExpiryPhase.OwnerTurnEnd, false).TotalRemoved);
});

Test("B02", "Priority follows actual deadline; equal deadlines use FIFO", () =>
{
    var ledger = new TideBlockLedger();
    var ordinary = ledger.RecordOrdinaryGain(8, 1, new(5, BlockExpiryPhase.OwnerTurnStart))!;
    var tide1 = ledger.RecordTideGain(3, 1)!; var tide2 = ledger.RecordTideGain(7, 1)!;
    var loss = ledger.Consume(5);
    Check.Sequence(new[] { tide1.Id, tide2.Id }, loss.Batches.Select(b => b.BatchId));
    Check.Equal(8L, ledger.OrdinaryRemaining); Check.Equal(5L, ledger.TideRemaining);
    Check.True(ledger.Snapshot().Any(b => b.Id == ordinary.Id));
});

Test("B03", "Permanent Tide converts existing/future Tide only, and is consumed last", () =>
{
    var ledger = new TideBlockLedger(); ledger.RecordTideGain(4, 1); ledger.RecordOrdinaryGain(3, 1);
    Check.Equal(1, ledger.MakeTidePermanent()); Check.Equal(0, ledger.MakeTidePermanent());
    ledger.RecordTideGain(5, 2);
    Check.True(ledger.Snapshot().Where(b => b.Kind == BlockBatchKind.Tide).All(b => b.IsPermanent && b.Expiry is null));
    Check.False(ledger.Snapshot().Single(b => b.Kind == BlockBatchKind.Ordinary).IsPermanent);
    ledger.Consume(5); Check.Equal(0L, ledger.OrdinaryRemaining); Check.Equal(7L, ledger.TideRemaining);
    Check.Equal(0L, ledger.Expire(500, BlockExpiryPhase.OwnerTurnEnd).TotalRemoved);
});

Test("B04", "Prevented native clear retains ordinary; confirmed clear removes only ordinary", () =>
{
    var ledger = new TideBlockLedger(); ledger.RecordOrdinaryGain(5, 1); ledger.RecordTideGain(7, 1);
    ledger.Expire(2, BlockExpiryPhase.OwnerTurnStart, false);
    Check.Equal(12L, ledger.Total);
    Check.Equal(5L, ledger.ClearOrdinary().TotalRemoved); Check.Equal(7L, ledger.TideRemaining);
    Check.Equal(0L, ledger.ClearOrdinary().TotalRemoved);
    ledger.RecordOrdinaryGain(4, 2);
    Check.Equal(7L, ledger.Expire(2, BlockExpiryPhase.OwnerTurnEnd, false).TotalRemoved);
    Check.Equal(4L, ledger.OrdinaryRemaining);
});

Test("B05", "Ledger caps actual loss, does not count zero gain, and snapshots are detached", () =>
{
    var ledger = new TideBlockLedger(); Check.True(ledger.RecordTideGain(0, 0) is null);
    var first = ledger.RecordTideGain(4, 0)!; Check.Equal(1L, first.Id);
    var snapshot = ledger.Snapshot(); Check.Equal(0L, ledger.Consume(0).TotalRemoved);
    Check.Equal(4L, ledger.Consume(99).TotalRemoved); Check.Equal(0L, ledger.Total);
    Check.Equal(4, snapshot.Single().Remaining); Check.Equal(4, first.Remaining);
});

Test("B06", "Invalid ledger input and deadline overflow do not partially mutate; totals use long", () =>
{
    var ledger = new TideBlockLedger();
    Check.Throws<ArgumentOutOfRangeException>(() => ledger.RecordTideGain(-1, 1));
    Check.Throws<ArgumentOutOfRangeException>(() => ledger.RecordTideGain(1, -1));
    Check.Throws<ArgumentOutOfRangeException>(() => ledger.Expire(1, (BlockExpiryPhase)99));
    Check.Throws<OverflowException>(() => ledger.RecordTideGain(1, long.MaxValue));
    Check.Throws<OverflowException>(() => ledger.RecordOrdinaryGain(1, long.MaxValue));
    Check.Equal(0L, ledger.Total); Check.Equal(0, ledger.Snapshot().Count);
    ledger.RecordTideGain(int.MaxValue, 1); ledger.RecordTideGain(int.MaxValue, 1);
    Check.Equal(4294967294L, ledger.Total);
    Check.Equal(2147483647L, ledger.Consume(int.MaxValue).TotalRemoved);
});

Test("B07", "Long mixed sequence conserves all recorded Block across damage/clear/expiry", () =>
{
    var ledger = new TideBlockLedger(); var rng = new Random(842); long added = 0, removed = 0;
    for (int turn = 1; turn <= 100; turn++)
    {
        removed += ledger.ClearOrdinary().TotalRemoved;
        for (int i = 0; i < 5; i++)
        {
            int amount = rng.Next(1, 30); added += amount;
            if (i % 2 == 0) ledger.RecordTideGain(amount, turn); else ledger.RecordOrdinaryGain(amount, turn);
            removed += ledger.Consume(rng.Next(0, 30)).TotalRemoved;
            Check.Equal(added - removed, ledger.Total);
            Check.Equal(ledger.Total, ledger.Snapshot().Sum(b => (long)b.Remaining));
            Check.True(ledger.Snapshot().All(b => b.Remaining > 0));
        }
        removed += ledger.Expire(turn, BlockExpiryPhase.OwnerTurnEnd, false).TotalRemoved;
        if (turn == 50) ledger.MakeTidePermanent();
        Check.Equal(added - removed, ledger.Total);
    }
});

Test("W01", "Waves halve with floor once per owner turn", () =>
{
    var waves = new WaveState(); waves.Add(20);
    int turn = 1;
    foreach (int expected in new[] {10, 5, 2, 1, 0})
    {
        waves.StartTurn(turn); waves.TakeEndTurnAmount(turn); waves.TakeEndTurnAmount(turn++);
        Check.Equal(expected, waves.Amount);
    }
});
Test("W02", "Damage and native Block clearing do not consume Waves", () =>
{
    var waves = new WaveState(); waves.Add(20);
    var ledger = new TideBlockLedger(); ledger.RecordTideGain(20, 1, false);
    ledger.Consume(6); Check.Equal(20, waves.Amount);
    Check.Equal(0L, ledger.Expire(2, BlockExpiryPhase.OwnerTurnEnd, false).TotalRemoved);
    ledger.Consume(14); waves.StartTurn(2); Check.Equal(20, waves.Amount); waves.TakeEndTurnAmount(2);
    Check.Equal(10, waves.Amount); Check.Equal(0L, ledger.Total);
});
Test("W03", "New natural Tide pays its own Block and suppresses existing Waves", () =>
{
    var waves = new WaveState(); waves.Add(10);
    waves.RecordTideSettlement(1); waves.Add(20);
    int paid = 20 + waves.TakeEndTurnAmount(1);
    Check.Equal(20, paid); Check.Equal(15, waves.Amount);
    Check.Equal(0, waves.TakeEndTurnAmount(1));
    waves.StartTurn(2); Check.Equal(15, waves.TakeEndTurnAmount(2));
});
Test("W04", "Endless Tide prevents end-turn halving until removed", () =>
{
    var waves = new WaveState { Retained = true }; waves.Add(21);
    waves.StartTurn(1); waves.StartTurn(2); Check.Equal(21, waves.Amount);
    waves.Retained = false; waves.StartTurn(3); waves.TakeEndTurnAmount(3); Check.Equal(10, waves.Amount);
});

AsyncTest("O24", "Activated backgrounds settle half effect then extinguish; extras stay full", async () =>
{
    var state = new OrbCombatState("owner");
    state.Gain(OrbKind.Fire, 5); state.Gain(OrbKind.Growth, 3); state.Gain(OrbKind.Tide, 7);
    state.QueueExtraSettlement(OrbSelector.Named(OrbKind.Growth, OrbScope.All), 1, "extra");
    var requests = new List<SettlementRequest>();
    await state.ResolveEndTurnAsync(new(OrbScope.All, false, true), request => { requests.Add(request); return ValueTask.CompletedTask; });
    Check.Sequence(new[] { OrbKind.Tide, OrbKind.Growth, OrbKind.Fire, OrbKind.Growth }, requests.Select(r => r.Orb));
    Check.Sequence(new[] { 7, 1, 2, 3 }, requests.Select(r => r.EffectAmount()));
    Check.Equal(0, state.Select(OrbScope.All, ActivationFilter.Active).Count);
});
AsyncTest("O25", "Eternal Grimoire restores full effects without preserving activation", async () =>
{
    var state = new OrbCombatState("owner");
    state.Gain(OrbKind.Fire, 5); state.Gain(OrbKind.Tide, 7);
    var amounts = new List<int>();
    await state.ResolveEndTurnAsync(new(OrbScope.All, false, false), request => { amounts.Add(request.EffectAmount()); return ValueTask.CompletedTask; });
    Check.Sequence(new[] {7, 5}, amounts);
    Check.Equal(0, state.Select(OrbScope.All, ActivationFilter.Active).Count);
});

Test("W05", "Tide settlement suppresses Wave payout only for its owner turn", () =>
{
    var waves = new WaveState(); waves.Add(20);
    waves.RecordTideSettlement(1);
    Check.Equal(0, waves.TakeEndTurnAmount(1));
    waves.StartTurn(2); Check.Equal(10, waves.TakeEndTurnAmount(2));
    waves.StartTurn(3); waves.RecordTideSettlement(3); waves.Add(7);
    Check.Equal(0, waves.TakeEndTurnAmount(3)); Check.Equal(6, waves.Amount);
    waves.StartTurn(4); Check.Equal(6, waves.TakeEndTurnAmount(4));
});

AsyncTest("O26", "Rekindle settles Fire normally but preserves activation for this turn", async () =>
{
    var state = new OrbCombatState("owner"); state.Gain(OrbKind.Fire, 7);
    state.PreserveEndTurnActivation(OrbKind.Fire);
    var values = new List<int>();
    await state.ResolveEndTurnAsync(new(OrbScope.All, false, true), r => { values.Add(r.EffectAmount()); return ValueTask.CompletedTask; });
    Check.Sequence(new[] {7}, values); Check.True(state.IsActivated(OrbKind.Fire));
    state.BeginOwnerTurn();
    await state.ResolveEndTurnAsync(new(OrbScope.All, false, true), _ => ValueTask.CompletedTask);
    Check.False(state.IsActivated(OrbKind.Fire));
});
AsyncTest("O27", "Legacy optional preservation rule permits explicit extinguish", async () =>
{
    var state = new OrbCombatState("owner"); state.Gain(OrbKind.Fire, 3); state.Gain(OrbKind.Tide, 5);
    await state.ResolveEndTurnAsync(new(OrbScope.All, false, true, true), _ => ValueTask.CompletedTask);
    Check.True(state.IsActivated(OrbKind.Fire)); Check.False(state.IsActivated(OrbKind.Tide));
    state.Extinguish(OrbKind.Fire, OrbScope.All); Check.False(state.IsActivated(OrbKind.Fire));
});
Test("O28", "Switch lock permits background gains and activation without switch events", () =>
{
    var state = new OrbCombatState("owner") { SwitchLocked = true };
    var gain = state.Gain(OrbKind.Tide, 6);
    Check.Equal(OrbKind.Fire, state.Foreground); Check.Equal(6, state.Value(OrbKind.Tide));
    Check.True(state.IsActivated(OrbKind.Tide)); Check.False(gain.Events.Any(e => e.Kind == OrbEventKind.ForegroundChanged));
    state.SwitchLocked = false; state.Gain(OrbKind.Tide, 1); Check.Equal(OrbKind.Tide, state.Foreground);
});
Test("O29", "Tied backgrounds activate without switching and repeated losses count per event", () =>
{
    var state = new OrbCombatState("owner"); state.Strengthen(OrbKind.Fire, 10);
    var targets = state.GetCandidates(OrbSelector.Lowest(OrbScope.Background)).ToArray();
    Check.Equal(2, targets.Length);
    foreach (var kind in targets) { state.Strengthen(kind, 6, OrbScope.All); state.ActivateWithoutSwitch(kind); }
    Check.Equal(OrbKind.Fire, state.Foreground); Check.Equal(2, state.Select(OrbScope.Background, ActivationFilter.Active).Count);
    state.Lose(OrbKind.Tide, 2, OrbScope.All); state.Lose(OrbKind.Tide, 9, OrbScope.All); state.Lose(OrbKind.Tide, 9, OrbScope.All);
    Check.Equal(2L, state.LossCount); Check.Equal(1L, state.ZeroCount); Check.Equal(0, state.Value(OrbKind.Tide));
});

Test("L01", "Lock extinguishes before callbacks, stacks duration, accepts gains and unlocks inactive", () =>
{
    var s = new OrbCombatState("owner"); s.BeginOwnerTurn(); s.Gain(OrbKind.Tide, 8);
    var locked = s.Lock(OrbKind.Tide, 2);
    Check.True(s.IsLocked(OrbKind.Tide)); Check.False(s.IsActivated(OrbKind.Tide));
    Check.Equal(1, locked.Events.Count(e => e.Kind == OrbEventKind.Extinguished));
    Check.Equal(OrbOperationStatus.Blocked, s.Activate(OrbKind.Tide, OrbScope.All).Status);
    s.Gain(OrbKind.Tide, 4); s.Strengthen(OrbKind.Tide, 2, OrbScope.All);
    Check.Equal(14, s.Value(OrbKind.Tide)); Check.False(s.IsActivated(OrbKind.Tide));
    Check.Equal(0, s.Lock(OrbKind.Tide, 1).Events.Count(e => e.Kind == OrbEventKind.Extinguished));
    s.BeginOwnerTurn(); Check.Equal(2, s.LockedTurns(OrbKind.Tide));
    s.BeginOwnerTurn(); Check.True(s.IsLocked(OrbKind.Tide));
    s.BeginOwnerTurn(); Check.False(s.IsLocked(OrbKind.Tide)); Check.False(s.IsActivated(OrbKind.Tide));
    s.Lock(OrbKind.Tide); s.Lock(OrbKind.Tide, 2); s.BeginOwnerTurn(); Check.Equal(-1, s.LockedTurns(OrbKind.Tide));
});
Test("L02", "Locked ring slots remain anchored while free slots rotate and turns preserve positions", () =>
{
    var s = new OrbCombatState("owner"); s.Lock(OrbKind.Fire, 2);
    s.Gain(OrbKind.Growth, 5);
    Check.Sequence(new[] { OrbKind.Fire, OrbKind.Growth, OrbKind.Tide }, s.Positions);
    s.Gain(OrbKind.Tide, 2);
    Check.Sequence(new[] { OrbKind.Fire, OrbKind.Tide, OrbKind.Growth }, s.Positions);
    s.Lock(OrbKind.Growth); s.Gain(OrbKind.Tide, 1); s.BeginOwnerTurn();
    Check.Sequence(new[] { OrbKind.Fire, OrbKind.Tide, OrbKind.Growth }, s.Positions);
    Check.Equal(OrbOperationStatus.Blocked, s.SwapPositions(OrbKind.Fire, OrbKind.Tide).Status);
    s.Lock(OrbKind.Tide); s.Activate(OrbKind.Fire, OrbScope.All);
    Check.Equal(0, s.Select(OrbScope.All, ActivationFilter.Active).Count);
});
AsyncTest("L03", "Lock blocks immediate, natural and extra settlements without inflating counters", async () =>
{
    var s = new OrbCombatState("owner"); s.Gain(OrbKind.Fire, 15); s.Lock(OrbKind.Fire, 1);
    s.QueueExtraSettlement(OrbSelector.Named(OrbKind.Fire), 3, "extra");
    Check.Equal(0, await s.SettleImmediatelyAsync(OrbSelector.Named(OrbKind.Fire), 2, _ => throw new Exception("locked"), "now"));
    await s.ResolveEndTurnAsync(new(OrbScope.All), _ => throw new Exception("locked"));
    Check.Equal(0, s.SettlementsThisTurn);
    s.BeginOwnerTurn();
    await s.SettleImmediatelyAsync(OrbSelector.Named(OrbKind.Fire), 2, _ => ValueTask.CompletedTask, "now");
    Check.Equal(2, s.SettlementsThisTurn);
    Check.Equal(0, await s.SettleImmediatelyAsync(OrbSelector.Named(OrbKind.Fire), 1, _ => throw new Exception("dead"), "dead", canSettle: () => false));
    Check.Equal(2, s.SettlementsThisTurn);
});
AsyncTest("L04", "Natural extinguish callbacks reorder pending orbs; a settled orb never repeats", async () =>
{
    var s = new OrbCombatState("owner");
    foreach (var kind in s.Positions.ToArray()) { s.Strengthen(kind, 7, OrbScope.All); s.ActivateWithoutSwitch(kind); }
    var seen = new List<SettlementRequest>();
    await s.ResolveEndTurnAsync(new(OrbScope.All, false, true), r => { seen.Add(r); return ValueTask.CompletedTask; }, op =>
    {
        if (op.Events.Single().Orb == OrbKind.Fire)
        {
            Check.True(s.IsActivated(OrbKind.Tide));
            s.Activate(OrbKind.Growth, OrbScope.All); s.Activate(OrbKind.Fire, OrbScope.All);
        }
        return ValueTask.CompletedTask;
    });
    Check.Sequence(new[] { OrbKind.Fire, OrbKind.Growth, OrbKind.Tide }, seen.Select(r => r.Orb));
    Check.Sequence(new[] { 7, 3, 3 }, seen.Select(r => r.EffectAmount()));
    Check.True(s.IsActivated(OrbKind.Fire)); Check.Equal(3, s.SettlementsThisTurn);
});
Test("L05", "Growth tie follows source around the ring and can select a locked recipient", () =>
{
    var s = new OrbCombatState("owner"); s.Lock(OrbKind.Fire);
    Check.Equal(OrbKind.Fire, s.LowestOther(OrbKind.Growth)!.Value);
    s.Strengthen(OrbKind.Fire, 4, OrbScope.All);
    Check.Equal(OrbKind.Tide, s.LowestOther(OrbKind.Growth)!.Value);
    s.SwitchLocked = true; s.Gain(OrbKind.Growth, 1);
    Check.Sequence(new[] { OrbKind.Fire, OrbKind.Growth, OrbKind.Tide }, s.Positions);
});
Test("W06", "Retention floor cannot create stacks and endless retention overrides the floor", () =>
{
    var waves = new WaveState { RetentionFloor = 6 }; waves.Add(3);
    Check.Equal(3, waves.TakeEndTurnAmount(1)); Check.Equal(3, waves.Amount);
    waves.Add(8); waves.RecordTideSettlement(2);
    Check.Equal(0, waves.TakeEndTurnAmount(2)); Check.Equal(6, waves.Amount);
    waves.Add(15); waves.Retained = true;
    Check.Equal(21, waves.TakeEndTurnAmount(3)); Check.Equal(21, waves.Amount);
});

Test("041W01", "End-turn Waves freeze and only the frozen shortfall is paid", () =>
{
    var waves = new WaveState(); waves.Add(15);
    waves.BeginEndTurnPhase(1);
    waves.RecordEndTurnTideContribution(1, 10);
    waves.Add(10); // Newly created Waves must not raise this phase's target.
    Check.Equal(5, waves.TakeEndTurnAmount(1));
    Check.Equal(12, waves.Amount); // 25 halves using the native floor rule.
});

Test("041W02", "Multiple end-turn Tide settlements accumulate pre-modifier contribution", () =>
{
    var waves = new WaveState(); waves.Add(3);
    waves.BeginEndTurnPhase(2);
    waves.RecordEndTurnTideContribution(2, 4);
    waves.RecordEndTurnTideContribution(2, 6);
    Check.Equal(0, waves.TakeEndTurnAmount(2));
});

Test("041W03", "Active-play Tide is not treated as end-turn suppression", () =>
{
    var waves = new WaveState(); waves.Add(10);
    Check.False(waves.TryRecordEndTurnTideContribution(1, 7));
    Check.Equal(10, waves.TakeEndTurnAmount(1));
});

Test("041L01", "Lock history is distinct by orb kind and survives unlock", () =>
{
    var state = new OrbCombatState("owner");
    state.Lock(OrbKind.Fire); state.Lock(OrbKind.Fire, 2); state.Unlock(OrbKind.Fire);
    state.Lock(OrbKind.Tide);
    Check.Equal(2, state.LockedKindsThisCombatCount);
    Check.True(state.WasEverLocked(OrbKind.Fire)); Check.True(state.WasEverLocked(OrbKind.Tide));
    Check.False(state.WasEverLocked(OrbKind.Growth));
    Check.Sequence(new[] { OrbKind.Fire, OrbKind.Tide }, state.LockedKindsThisCombat.OrderBy(k => k));
});

AsyncTest("L06", "Combat settlement count survives turns, ignores locked attempts, and resets in a fresh combat", async () =>
{
    var s = new OrbCombatState("owner");
    s.Gain(OrbKind.Fire, 7);
    await s.SettleImmediatelyAsync(OrbSelector.Named(OrbKind.Fire), 2, _ => ValueTask.CompletedTask, "now");
    Check.Equal(2, s.SettlementsThisCombat);
    s.BeginOwnerTurn();
    Check.Equal(0, s.SettlementsThisTurn); Check.Equal(2, s.SettlementsThisCombat);
    s.Lock(OrbKind.Fire);
    await s.SettleImmediatelyAsync(OrbSelector.Named(OrbKind.Fire), 3, _ => ValueTask.CompletedTask, "locked");
    Check.Equal(2, s.SettlementsThisCombat);
    Check.Equal(0, new OrbCombatState("owner").SettlementsThisCombat);
});

Test("W07", "Gain policy blocks every addition without erasing existing Waves or changing decay", () =>
{
    bool allowed = true;
    var waves = new WaveState(() => allowed);
    waves.Add(10);
    allowed = false;
    waves.Add(int.MaxValue);
    Check.Equal(10, waves.Amount);
    Check.Throws<ArgumentOutOfRangeException>(() => waves.Add(-1));
    Check.Equal(10, waves.TakeEndTurnAmount(1));
    Check.Equal(5, waves.Amount);
    allowed = true;
    waves.Add(3);
    Check.Equal(8, waves.Amount);
    var other = new WaveState();
    other.Add(14);
    Check.Equal(14, other.Amount);
});

int failed = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine($"PASS {test.Id} {test.Name}"); }
    catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {test.Id} {test.Name}\n{error}"); }
}
Console.WriteLine($"RESULT {tests.Count - failed}/{tests.Count} passed; {failed} failed");
return failed == 0 ? 0 : 1;

static class Check
{
    public static void True(bool condition) { if (!condition) throw new Exception("Expected true."); }
    public static void False(bool condition) => True(!condition);
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}.");
    }
    public static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual)) throw new Exception($"Expected [{string.Join(",", expected)}], got [{string.Join(",", actual)}].");
    }
    public static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
    public static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}

sealed class ScriptedRandom(params int[] indices) : IOrbRandom
{
    public int Calls { get; private set; }
    public int NextInt(int exclusiveMax) => indices[Calls++];
}

sealed class SeededRandom(int seed) : IOrbRandom
{
    private readonly Random _random = new(seed);
    public int NextInt(int exclusiveMax) => _random.Next(exclusiveMax);
}
