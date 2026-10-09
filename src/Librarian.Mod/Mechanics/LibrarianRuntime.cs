using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Integration;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.Mechanics;

public sealed class LibrarianSession(Player player)
{
    public Player Player { get; } = player;
    public bool HasCharacterOrbs => Player.Character is LibrarianCharacter;
    public OrbCombatState Orbs { get; } = new(player.NetId.ToString());
    public WaveState Waves { get; } = new(() =>
        player.Creature.GetPower<Librarian.LibrarianCode.Powers.Implemented.EndlessTidePower>() is null);
    public EndTurnRules EndTurnRules { get; set; } = new();
    public bool ResolvingEndTurn { get; internal set; }
    public long FireLostThisCombat { get; internal set; }
    public OrbKind? LastSettledOrb { get; internal set; }
    public long LastSettledTurn { get; internal set; } = -1;
    public OrbKind? PreviousTurnsLastSettledOrb { get; internal set; }
    public CardModel? LastPlayedSnapshot { get; internal set; }
    internal Dictionary<CardPlay, CardModel> PendingPlaySnapshots { get; } = new();
    internal List<(long Turn, Func<PlayerChoiceContext, Task> Action)> StartTasks { get; } = [];
    internal List<Func<PlayerChoiceContext, Task>> EndTasks { get; } = [];
    internal List<Action<EndTurnBlockProjection>?> EndTaskPreviews { get; } = [];
    internal Queue<Func<PlayerChoiceContext, Task>> AfterHandCleanup { get; } = new();
    internal HashSet<CardModel> EndTurnHandCards { get; } = [];
    internal LibrarianBottomPlay041.EndTurnBottomSnapshot? EndTurnBottomSnapshot { get; set; }
    public void QueueNextTurn(Func<PlayerChoiceContext, Task> action) => StartTasks.Add((Orbs.OwnerTurn + 1, action));
    public void QueueEndTurn(Func<PlayerChoiceContext, Task> action, Action<EndTurnBlockProjection>? preview = null)
    {
        EndTasks.Add(action);
        EndTaskPreviews.Add(preview);
    }
    public Task AfterCleanupOrNow(PlayerChoiceContext context, Func<PlayerChoiceContext, Task> action)
    {
        if (!ResolvingEndTurn) return action(context);
        AfterHandCleanup.Enqueue(action);
        return Task.CompletedTask;
    }
    public void SyncTurn()
    {
        while (Orbs.OwnerTurn < (Player.PlayerCombatState?.TurnNumber ?? 1))
        {
            PreviousTurnsLastSettledOrb = LastSettledOrb;
            Orbs.BeginOwnerTurn();
        }
    }
}

public interface IOrbEventListener
{
    Task OnOrbEvent(LibrarianSession session, PlayerChoiceContext context, OrbEvent change);
}

public interface IOrbSettlementListener
{
    Task AfterOrbSettlement(LibrarianSession session, PlayerChoiceContext context,
        SettlementRequest request, OrbKind? growthHigherTarget);
}

/// <summary>Runs after tidal expiry and delayed cards, before the settlement task list freezes.</summary>
public interface IOrbEndTurnListener
{
    Task BeforeOrbSettlements(LibrarianSession session, PlayerChoiceContext context);
}

public interface IOrbAfterEndTurnListener
{
    Task AfterOrbSettlements(LibrarianSession session, PlayerChoiceContext context);
}

public interface IOrbEndTurnRuleProvider
{
    bool SettleAllActivated { get; }
    bool PreserveActivation { get; }
    bool PreserveBackgroundActivation => false;
}

/// <summary>State is owned by the game's per-combat player object, never by a shared character model.</summary>
public sealed class LibrarianRuntime : ILibrarianMechanics
{
    private static readonly ConditionalWeakTable<PlayerCombatState, LibrarianSession> Sessions = new();
    public static LibrarianRuntime Instance { get; } = new();
    public static event Func<LibrarianSession, PlayerChoiceContext, OrbEvent, Task>? OrbChanged;
    internal static Creature? NativeTideGainOwner;
    internal static Creature? BlockReconciliationOwner;

    public static void Initialize()
    {
        LibrarianMechanicsBridge.Current = Instance;
        // ModelDb constructs and registers SingletonModel after mod initializers finish.
        MainFile.Logger.Info("Librarian mechanics connected: independent three-orb state and tidal Block ledger.");
    }

    public static LibrarianSession Get(Player player)
    {
        var combat = player.PlayerCombatState ?? throw new InvalidOperationException("Orb effects require active combat.");
        var session = Sessions.GetValue(combat, _ =>
        {
            var created = new LibrarianSession(player);
            // A teammate can first acquire Waves halfway through a fight with ordinary Block.
            if (player.Creature.Block > 0)
                created.Orbs.BlockLedger.RecordOrdinaryGain(player.Creature.Block, created.Orbs.OwnerTurn);
            return created;
        });
        session.SyncTurn();
        return session;
    }

    /// <summary>Preview/UI lookup: no allocation, turn changes, events or randomness.</summary>
    public static bool TryGet(Player player, out LibrarianSession? session)
    {
        session = null;
        return player.PlayerCombatState is { } combat && Sessions.TryGetValue(combat, out session);
    }

    internal static LibrarianSession? For(Creature creature)
        => creature.Player is { PlayerCombatState: not null } player
            && (player.Character is LibrarianCharacter || TryGet(player, out _)) ? Get(player) : null;

    public async Task GainAsync(PlayerChoiceContext choiceContext, Player player, LibrarianElement element,
        decimal amount, AbstractModel source, CardPlay? cardPlay = null)
    {
        var session = Get(player);
        var kind = element switch { LibrarianElement.Fire => OrbKind.Fire, LibrarianElement.Water => OrbKind.Tide, _ => OrbKind.Growth };
        await Dispatch(session, choiceContext, session.Orbs.Gain(kind, checked((int)decimal.Floor(amount)), new(source.Id.ToString())));
    }

    public static async Task Dispatch(LibrarianSession session, PlayerChoiceContext context, OrbOperationResult operation)
    {
        LibrarianOrbAudio.OnOperation(session, operation);
        LibrarianCardVfx050.OrbChanged(session, operation);
        foreach (var change in operation.Events)
        {
            if (change.Kind == OrbEventKind.Lost && change.Orb == OrbKind.Fire)
                session.FireLostThisCombat = checked(session.FireLostThisCombat + change.ActualAmount);
            MainFile.Logger.Info($"ORB {session.Player.NetId} {change.Kind} {change.Orb} {change.BeforeValue}->{change.AfterValue}");
            foreach (var listener in session.Player.Creature.Powers.OfType<IOrbEventListener>().ToArray())
                await listener.OnOrbEvent(session, context, change);
            foreach (var listener in session.Player.Relics.Where(r => !r.IsMelted).OfType<IOrbEventListener>().ToArray())
                await listener.OnOrbEvent(session, context, change);
            if (OrbChanged is { } callbacks)
                foreach (Func<LibrarianSession, PlayerChoiceContext, OrbEvent, Task> callback in callbacks.GetInvocationList())
                    await callback(session, context, change);
        }
        LibrarianOrbPanel.Refresh(session);
        session.Player.PlayerCombatState?.RecalculateCardValues();
    }

    public static Task DrawOrDeferAsync(LibrarianSession session, PlayerChoiceContext context, int count)
        => session.AfterCleanupOrNow(context, async ctx => { await CardPileCmd.Draw(ctx, count, session.Player); });

    /// <summary>Marks only a card created during end-turn resolution for the native flush path.</summary>
    internal static void MarkEndTurnHandCard(CardModel card)
    {
        if (For(card.Owner.Creature) is { ResolvingEndTurn: true } session)
            session.EndTurnHandCards.Add(card);
    }

    internal static bool IsEndTurnHandCard(CardModel card)
        => For(card.Owner.Creature) is { } session
            && session.EndTurnHandCards.Contains(card);

    /// <summary>D25: the first single-player release has no other recipients. Propagated events must never be forwarded.</summary>
    public static Task PropagateTideAsync(LibrarianSession session, PlayerChoiceContext context, OrbEvent change)
    {
        if (change.Kind != OrbEventKind.Gained || change.Orb != OrbKind.Tide || change.Origin.IsPropagated)
            return Task.CompletedTask;
        return Task.CompletedTask;
    }

    /// <summary>Binding copies a resolved kind/value, never a teammate's retained orb value.
    /// This runs once on the synchronized action queue, with no recursive propagation.</summary>
    public static async Task BindSharedAsync(PlayerChoiceContext context, Player owner, AbstractModel source)
    {
        var own = Get(owner);
        var combat = owner.Creature.CombatState;
        if (combat is null) return;
        SettlementRequest? copied = null;
        await own.Orbs.SettleImmediatelyAsync(OrbSelector.Named(own.Orbs.Foreground, OrbScope.All), 1,
            async request => { copied = request; await Settle(own, context, request); }, source.Id.ToString(),
            canSettle: () => !CombatManager.Instance.IsOverOrEnding && !owner.Creature.IsDead);
        if (copied is null) return;
        foreach (var recipient in combat.Players.Where(p => p != owner && !p.Creature.IsDead).OrderBy(p => p.NetId).ToArray())
        {
            if (CombatManager.Instance.IsOverOrEnding) break;
            if (copied.Orb == OrbKind.Growth && recipient.Character is not LibrarianCharacter) continue;
            var shared = Get(recipient);
            await Settle(shared, context, copied with
            {
                OwnerId = shared.Orbs.OwnerId, OwnerTurn = shared.Orbs.OwnerTurn,
                Source = source.Id + ":shared", HalfEffect = false
            });
            shared.Orbs.RecordSharedSettlement();
        }
    }

    public static async ValueTask Settle(LibrarianSession session, PlayerChoiceContext context, SettlementRequest request, Creature? fireTarget = null)
    {
        var player = session.Player;
        if (CombatManager.Instance.IsOverOrEnding || player.Creature.IsDead) return;
        OrbKind? growthHigherTarget = request.Orb == OrbKind.Growth ? session.Orbs.HighestOther(OrbKind.Growth) : null;
        LibrarianOrbVfx.Safely(() => LibrarianOrbPanel.Pulse(session, request.Orb));
        MainFile.Logger.Info($"SETTLE {player.NetId} {request.Orb}={request.Value} {request.Reason}");
        switch (request.Orb)
        {
            case OrbKind.Fire:
                var enemies = player.Creature.CombatState?.GetOpponentsOf(player.Creature).Where(c => c.IsHittable).ToList();
                if (enemies is not { Count: > 0 }) break;
                // A directed settlement never falls back to another enemy if the attack killed its target.
                var enemy = fireTarget ?? player.RunState.Rng.CombatTargets.NextItem(enemies);
                if (fireTarget is not null && !enemies.Contains(fireTarget)) break;
                if (enemy is null) break;
                LibrarianOrbAudio.OnSettlement(session, request);
                await LibrarianOrbVfx.Travel(session, OrbKind.Fire, enemy);
                if (CombatManager.Instance.IsOverOrEnding || player.Creature.IsDead || player.Creature.CombatState is null || !enemy.IsHittable) return;
                await CreatureCmd.Damage(context, enemy, request.EffectAmount(), ValueProp.Unpowered, player.Creature);
                break;
            case OrbKind.Tide:
                LibrarianOrbAudio.OnSettlement(session, request);
                await LibrarianOrbVfx.Travel(session, OrbKind.Tide, player.Creature);
                if (CombatManager.Instance.IsOverOrEnding || player.Creature.IsDead || player.Creature.CombatState is null) return;
                int tideAmount = request.EffectAmount();
                // v0.4.1 compares the pre-modifier contribution (including the
                // native background-half value).  Actual Block remains tracked by
                // the native ledger and may differ after hooks.
                session.Waves.TryRecordEndTurnTideContribution(session.Orbs.OwnerTurn, tideAmount);
                // Tide pays its Block before it creates the next Waves amount.
                await GainTidalBlock(session, tideAmount);
                session.Waves.Add(tideAmount);
                break;
            case OrbKind.Growth:
                if (session.Orbs.LowestOther(OrbKind.Growth) is { } kind)
                {
                    LibrarianOrbAudio.OnSettlement(session, request);
                    await LibrarianOrbVfx.Travel(session, OrbKind.Growth, receivingOrb: kind);
                    if (CombatManager.Instance.IsOverOrEnding || player.Creature.IsDead || player.Creature.CombatState is null) return;
                    await Dispatch(session, context, session.Orbs.Strengthen(kind, request.EffectAmount(), OrbScope.All, new(request.Source)));
                }
                break;
        }
        session.LastSettledOrb = request.Orb;
        session.LastSettledTurn = request.OwnerTurn;
        foreach (var listener in player.Relics.Where(r => !r.IsMelted).OfType<IOrbSettlementListener>().ToArray())
            await listener.AfterOrbSettlement(session, context, request, growthHigherTarget);
        LibrarianOrbVfx.Safely(() => LibrarianOrbPanel.Refresh(session));
    }

    // Direct native commit marks exactly this gain as tidal. Hook-generated side gains remain ordinary.
    internal static async Task GainTidalBlock(LibrarianSession session, int value)
    {
        if (value <= 0) return;
        var creature = session.Player.Creature;
        var combat = creature.CombatState!;
        await Hook.BeforeBlockGained(combat, creature, value, ValueProp.Unpowered, null);
        // Orb/Wave Block follows the native unpowered path, unaffected by Frail or Dexterity.
        decimal adjusted = value;
        adjusted = Hook.ModifyBlock(combat, creature, adjusted, ValueProp.Unpowered, null, null, out var modifiers);
        adjusted = Math.Max(0m, adjusted);
        await Hook.AfterModifyingBlockAmount(combat, adjusted, null, null, modifiers);
        if (adjusted > 0)
        {
            var previous = NativeTideGainOwner;
            int blockBefore = creature.Block;
            NativeTideGainOwner = creature;
            try { creature.GainBlockInternal(adjusted); }
            finally { NativeTideGainOwner = previous; }
            CombatManager.Instance.History.BlockGained(combat, creature, (int)adjusted, ValueProp.Unpowered, null);
            SfxCmd.Play("event:/sfx/block_gain");
            VfxCmd.PlayOnCreatureCenter(creature, "vfx/vfx_block");
            LibrarianOrbPanel.ShowTideChange(session, creature.Block - blockBefore);
            await Cmd.CustomScaledWait(0.1f, 0.25f);
        }
        await Hook.AfterBlockGained(combat, creature, adjusted, ValueProp.Unpowered, null);
        VerifyBlock(session);
    }

    internal static void VerifyBlock(LibrarianSession session)
    {
        Librarian.LibrarianCode.Powers.Implemented.TidalBlockStatusPower.Synchronize(session);
        if (session.Orbs.BlockLedger.Total != session.Player.Creature.Block)
            MainFile.Logger.Warn($"BLOCK_LEDGER_MISMATCH player={session.Player.NetId} ledger={session.Orbs.BlockLedger.Total} native={session.Player.Creature.Block}");
        LibrarianOrbPanel.Refresh(session);
    }

    public sealed class GameOrbRandom(Player player) : IOrbRandom
    {
        public int NextInt(int exclusiveMax) => player.RunState.Rng.CombatTargets.NextInt(exclusiveMax);
    }
}

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.InitIds))]
internal static class LibrarianModelsReady
{
    [HarmonyPostfix] private static void Postfix()
        => MainFile.Logger.Info("Librarian combat hooks registered=" + ModelDb.Singleton<LibrarianCombatHooks>().ShouldReceiveCombatHooks);
}

public sealed class LibrarianCombatHooks : SingletonModel
{
    public override bool ShouldReceiveCombatHooks => true;
    internal static int SuppressedEtherealTriggers { get; private set; }

    public LibrarianCombatHooks()
    {
        MegaCrit.Sts2.Core.Modding.ModHelper.SubscribeForCombatStateHooks(Id.Entry, _ => [this]);
    }

    public override Task BeforeCardPlayed(CardPlay cardPlay)
    {
        if (LibrarianRuntime.For(cardPlay.Card.Owner.Creature) is { } session)
            // A history snapshot must not register an extra card in CombatState.AllCards.
            session.PendingPlaySnapshots[cardPlay] = (CardModel)cardPlay.Card.ClonePreservingMutability();
        return Task.CompletedTask;
    }

    public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (LibrarianRuntime.For(cardPlay.Card.Owner.Creature) is { } session
            && session.PendingPlaySnapshots.Remove(cardPlay, out var snapshot))
            session.LastPlayedSnapshot = snapshot;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Native DoTurnEnd asks the combat hook before calling CardCmd.Exhaust.
    /// Suppress only the marked end-turn instances here, so they never enter
    /// Exhaust or emit its history/coordination hooks. All ordinary Ethereal
    /// cards retain the native answer.
    /// </summary>
    public override bool ShouldEtherealTrigger(CardModel card)
    {
        if (LibrarianRuntime.IsEndTurnHandCard(card))
        {
            SuppressedEtherealTriggers++;
            return false;
        }
        return true;
    }

    public override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side,
        IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        foreach (var creature in participants)
            if (LibrarianRuntime.For(creature) is { } session)
            {
                session.SyncTurn();
                session.Waves.Retained = creature.GetPower<Librarian.LibrarianCode.Powers.Implemented.RidgeWardPower>() is not null;
                session.Waves.RetentionFloor = creature.GetPower<Librarian.LibrarianCode.Powers.Implemented.UnretreatingTidePower>()?.Amount ?? 0;
                session.Waves.StartTurn(session.Orbs.OwnerTurn);
                LibrarianRuntime.VerifyBlock(session);
                LibrarianOrbPanel.Refresh(session);
            }
        return Task.CompletedTask;
    }

    // Native turn phase, energy setup and Block clearing have completed before BeforeHandDraw.
    public override async Task BeforeHandDraw(Player player, PlayerChoiceContext choiceContext, ICombatState combatState)
    {
        if (LibrarianRuntime.For(player.Creature) is not { } session) return;
        var due = session.StartTasks.Where(task => task.Turn <= session.Orbs.OwnerTurn).ToArray();
        session.StartTasks.RemoveAll(task => task.Turn <= session.Orbs.OwnerTurn);
        foreach (var task in due)
            if (!CombatManager.Instance.IsOverOrEnding && !player.Creature.IsDead) await task.Action(choiceContext);
    }

    public override async Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        foreach (var creature in participants.ToArray())
        {
            var session = LibrarianRuntime.For(creature);
            if (session is null) continue;
            var rules = creature.Powers.OfType<IOrbEndTurnRuleProvider>().ToArray();
            session.EndTurnRules = new(OrbScope.All,
                rules.Any(rule => rule.PreserveActivation), !rules.Any(rule => rule.SettleAllActivated), rules.Any(rule => rule.PreserveBackgroundActivation));
            // This is intentionally before the first await: Mainstem is checked
            // once and Fuel layers are frozen at the phase boundary.
            int fuelLayers = creature.GetPower<Librarian.LibrarianCode.Powers.Implemented.FuelTheFirePower>()?.Amount ?? 0;
            session.EndTurnBottomSnapshot = LibrarianBottomPlay041.CaptureEndTurnSnapshot(session.Player, fuelLayers);
            session.ResolvingEndTurn = true;
            session.Waves.BeginEndTurnPhase(session.Orbs.OwnerTurn);
            try
            {
            await session.Orbs.ResolveEndTurnAsync(session.EndTurnRules,
                request => LibrarianRuntime.Settle(session, choiceContext, request),
                result => new ValueTask(LibrarianRuntime.Dispatch(session, choiceContext, result)),
                async expired =>
                {
                    var previous = LibrarianRuntime.BlockReconciliationOwner;
                    LibrarianRuntime.BlockReconciliationOwner = creature;
                    try { creature.LoseBlockInternal(checked((int)expired.TotalRemoved)); }
                    finally { LibrarianRuntime.BlockReconciliationOwner = previous; }
                    LibrarianRuntime.VerifyBlock(session);
                    if (expired.TotalRemoved > 0)
                    {
                        LibrarianOrbPanel.ShowTideChange(session, expired.TotalRemoved, expired: true);
                        await Cmd.CustomScaledWait(0.25f, 0.45f);
                    }
                }, delayedAsync: async () =>
                {
                    if (session.EndTurnBottomSnapshot is { } bottomSnapshot)
                    {
                        await LibrarianBottomPlay041.ResolveEndTurnBottomStageAsync(choiceContext, session.Player, bottomSnapshot);
                        session.EndTurnBottomSnapshot = null;
                    }
                    var pending = session.EndTasks.ToArray();
                    session.EndTasks.Clear();
                    session.EndTaskPreviews.Clear();
                    foreach (var task in pending)
                        if (!CombatManager.Instance.IsOverOrEnding && !creature.IsDead) await task(choiceContext);
                    foreach (var listener in creature.Powers.OfType<IOrbEndTurnListener>().ToArray())
                        if (!CombatManager.Instance.IsOverOrEnding && !creature.IsDead)
                            await listener.BeforeOrbSettlements(session, choiceContext);
                    foreach (var listener in session.Player.Relics.Where(r => !r.IsMelted).OfType<IOrbEndTurnListener>().ToArray())
                        if (!CombatManager.Instance.IsOverOrEnding && !creature.IsDead)
                            await listener.BeforeOrbSettlements(session, choiceContext);
                }, random: new LibrarianRuntime.GameOrbRandom(session.Player), canSettle: () => !CombatManager.Instance.IsOverOrEnding && !creature.IsDead);
            foreach (var listener in creature.Powers.OfType<IOrbAfterEndTurnListener>().ToArray())
                if (!CombatManager.Instance.IsOverOrEnding && !creature.IsDead)
                    await listener.AfterOrbSettlements(session, choiceContext);
            // Decide only after delayed, natural and extra settlements have all completed.
            if (!CombatManager.Instance.IsOverOrEnding && !creature.IsDead)
            {
                session.Waves.Retained = creature.GetPower<Librarian.LibrarianCode.Powers.Implemented.RidgeWardPower>() is not null;
                session.Waves.RetentionFloor = creature.GetPower<Librarian.LibrarianCode.Powers.Implemented.UnretreatingTidePower>()?.Amount ?? 0;
                var cooldown = creature.GetPower<Librarian.LibrarianCode.Powers.Implemented.CooldownPower>();
                bool preserveWave = cooldown is not null && session.Waves.Amount > 0;
                int wavePayout = session.Waves.TakeEndTurnAmount(session.Orbs.OwnerTurn,
                    false,
                    preventDecay: preserveWave);
                if (preserveWave) await PowerCmd.ModifyAmount(choiceContext, cooldown!, -1, creature, null);
                decimal blockBeforeWave = creature.Block;
                await LibrarianRuntime.GainTidalBlock(session, wavePayout);
                if (wavePayout > 0 && creature.Block > blockBeforeWave)
                    LibrarianCardVfx050.Emit(creature, new(SpellElement050.Water, SpellShape050.Ripple), "impact", "WavePayout", session.Player, 50);
                LibrarianRuntime.VerifyBlock(session);
            }
            }
            finally { session.ResolvingEndTurn = false; }
            LibrarianOrbPanel.Refresh(session);
        }
    }

    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        foreach (var creature in participants.ToArray())
            if (LibrarianRuntime.For(creature) is { } session)
            {
                while (session.AfterHandCleanup.TryDequeue(out var action))
                    if (!CombatManager.Instance.IsOverOrEnding && !creature.IsDead) await action(choiceContext);
                session.EndTurnHandCards.Clear();
            }
    }
}

[HarmonyPatch(typeof(Creature), nameof(Creature.GainBlockInternal))]
internal static class RecordBlockGain
{
    [HarmonyPrefix] private static void Prefix(Creature __instance, out int __state) => __state = __instance.Block;
    [HarmonyPostfix] private static void Postfix(Creature __instance, int __state)
    {
        if (ReferenceEquals(__instance, LibrarianRuntime.BlockReconciliationOwner) || LibrarianRuntime.For(__instance) is not { } session) return;
        int actual = __instance.Block - __state;
        if (ReferenceEquals(__instance, LibrarianRuntime.NativeTideGainOwner)) session.Orbs.BlockLedger.RecordTideGain(actual, session.Orbs.OwnerTurn, expireAtNextEnd: false);
        else session.Orbs.BlockLedger.RecordOrdinaryGain(actual, session.Orbs.OwnerTurn);
        LibrarianRuntime.VerifyBlock(session);
    }
}

[HarmonyPatch]
internal static class RecordBlockLoss
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Creature), nameof(Creature.DamageBlockInternal));
        yield return AccessTools.Method(typeof(Creature), nameof(Creature.LoseBlockInternal));
    }
    [HarmonyPrefix] private static void Prefix(Creature __instance, out int __state) => __state = __instance.Block;
    [HarmonyPostfix] private static void Postfix(Creature __instance, int __state)
    {
        if (ReferenceEquals(__instance, LibrarianRuntime.BlockReconciliationOwner) || LibrarianRuntime.For(__instance) is not { } session) return;
        session.Orbs.BlockLedger.Consume(Math.Max(0, __state - __instance.Block));
        LibrarianRuntime.VerifyBlock(session);
    }
}

[HarmonyPatch(typeof(Creature), "ClearBlock")]
internal static class ReconcileNativeBlockClear
{
    [HarmonyPostfix] private static void Postfix(Creature __instance, ref Task __result) => __result = AfterClear(__result, __instance);
    private static async Task AfterClear(Task original, Creature creature)
    {
        await original;
        if (LibrarianRuntime.For(creature) is not { } session) return;
        if (creature.Block == 0)
        {
            session.Orbs.BlockLedger.Consume(checked((int)session.Orbs.BlockLedger.Total));
        }
        LibrarianRuntime.VerifyBlock(session);
    }
}
