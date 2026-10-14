using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;

namespace Librarian.Mechanics;

/// <summary>Exact native CardPlay correlation. Original Task, virtual dispatch, waits and exceptions stay native.</summary>
internal static class LibrarianSpineCardExecution
{
    internal sealed class Cell(CardPlay play, long id)
    {
        private readonly object _gate = new();
        private Creature? _presentedCreature;
        private bool _handled;
        private bool _wrapperClosed;
        private int _pins;
        internal readonly CardPlay Play = play;
        internal readonly long Id = id;
        internal void Handled(Creature creature)
        {
            lock (_gate) { if (!_wrapperClosed) { _presentedCreature = creature; _handled = true; } }
        }
        internal bool IsHandledFor(Creature creature, bool pinned = false)
        {
            lock (_gate)
                return (!_wrapperClosed || (pinned && _pins > 0)) && _handled && ReferenceEquals(_presentedCreature, creature);
        }
        internal Creature? SnapshotHandledCreature()
        {
            lock (_gate) return _handled ? _presentedCreature : null;
        }
        internal bool Pin()
        {
            lock (_gate) { if (_wrapperClosed) return false; _pins++; return true; }
        }
        internal void Unpin()
        {
            lock (_gate) { if (_pins > 0) _pins--; ForgetIfFinished(); }
        }
        internal void Close() { lock (_gate) { _wrapperClosed = true; ForgetIfFinished(); } }
        private void ForgetIfFinished()
        {
            // A native pause can advance the wrapper while this occurrence's real OnPlay Task stays active.
            if (_wrapperClosed && _pins == 0) { _handled = false; _presentedCreature = null; }
        }
    }
    internal sealed class ExecutionScope(CardModel card, ExecutionScope? parent)
    {
        internal readonly CardModel Card = card;
        internal readonly ExecutionScope? Parent = parent;
        private readonly object _gate = new();
        private Cell? _cell;
        private bool _ended;
        internal bool IsEnded { get { lock (_gate) return _ended; } }
        internal Cell? ActiveCell { get { lock (_gate) return _ended ? null : _cell; } }
        internal void Before(Cell cell)
        {
            lock (_gate)
            {
                if (_ended || !ReferenceEquals(Card, cell.Play.Card)) return;
                if (_cell is not null && !ReferenceEquals(_cell, cell)) _cell.Close();
                _cell = cell;
            }
        }
        internal void After(Cell cell)
        {
            lock (_gate) { if (ReferenceEquals(_cell, cell)) _cell = null; cell.Close(); }
        }
        // Completion may run on TaskScheduler.Default. Plain managed state only.
        internal void End()
        {
            lock (_gate) { if (_ended) return; _ended = true; _cell?.Close(); _cell = null; }
        }
    }
    private sealed class PinnedLease(Cell cell)
    {
        internal readonly Cell Cell = cell; // Identity cannot change when the surrounding replay loop advances.
        private readonly bool _acquired = cell.Pin();
        private readonly Creature? _consumedBy = cell.SnapshotHandledCreature();
        private int _ended;
        internal bool IsActive => _acquired && Volatile.Read(ref _ended) == 0;
        internal bool WasHandledFor(Creature creature) => ReferenceEquals(_consumedBy, creature);
        internal void End()
        {
            if (Interlocked.Exchange(ref _ended, 1) == 0 && _acquired) Cell.Unpin();
        }
    }
    private sealed class Invocation(ExecutionScope scope, PinnedLease? callerPin)
    {
        internal readonly ExecutionScope Scope = scope;
        internal readonly PinnedLease? CallerPin = callerPin;
        private bool _callerRestored;
        internal void RestoreCaller()
        {
            if (_callerRestored) return;
            Ambient.Value = Scope.Parent;
            Pinned.Value = CallerPin;
            _callerRestored = true;
        }
    }
    private static readonly AsyncLocal<ExecutionScope?> Ambient = new();
    private static readonly AsyncLocal<PinnedLease?> Pinned = new();
    private static readonly ConditionalWeakTable<CardPlay, Cell> Cells = new();
    private static long _nextId;
    private static int _dispatchPatchVerified;
    private static string? _dispatchPatchFault;
    private static Func<CardModel, PlayerChoiceContext, CardPlay, Task>? _originalOnPlay;
    internal static bool DispatchPatchVerified => Volatile.Read(ref _dispatchPatchVerified) != 0;
    internal static string? DispatchPatchFault => Volatile.Read(ref _dispatchPatchFault);

    internal static void AssertDispatchPatchVerified()
    {
        if (!DispatchPatchVerified)
            throw new InvalidOperationException(DispatchPatchFault ?? "Exact CardModel.OnPlayWrapper MoveNext dispatch has not been patched; owned Spine registration is closed.");
    }
    internal static Cell Get(CardPlay play) => Cells.GetValue(play, p => new Cell(p, Interlocked.Increment(ref _nextId)));
    internal static void Before(CardPlay play)
    {
        // A genuine nested wrapper clears the inherited pin before its first BeforeCardPlayed.
        if (Pinned.Value is not null) return;
        var scope = Ambient.Value;
        if (scope is not null && !scope.IsEnded && ReferenceEquals(scope.Card, play.Card)) scope.Before(Get(play));
    }
    internal static void After(CardPlay play)
    {
        var cell = Get(play);
        var scope = Ambient.Value;
        if (scope is not null && ReferenceEquals(scope.Card, play.Card)) scope.After(cell);
        else cell.Close();
    }
    internal static bool TryHandled(Creature creature, out long id)
    {
        id = 0;
        var pin = Pinned.Value;
        if (pin is not null)
        {
            // This immutable historical actor/id survives plain cell cleanup. Late Attack/Cast/PowerUp
            // still merge with the occurrence that already drove presentation, so they cannot replay it.
            // A foreign/unhandled pin never falls through to the next replay's mutable active cell.
            if (!pin.WasHandledFor(creature)) return false;
            id = pin.Cell.Id;
            return true;
        }
        var scope = Ambient.Value;
        var cell = scope?.ActiveCell;
        if (scope is null || cell is null || !cell.IsHandledFor(creature) || scope.IsEnded) return false;
        id = cell.Id;
        return true;
    }

    private static MethodInfo OriginalOnPlayMethod()
    {
        var method = typeof(CardModel).GetMethod("OnPlay", BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(PlayerChoiceContext), typeof(CardPlay)], null);
        if (method is null || !method.IsVirtual || method.ReturnType != typeof(Task))
            throw PatchFailure("protected virtual Task OnPlay(PlayerChoiceContext, CardPlay) is unavailable");
        return method;
    }
    // A virtual open delegate reaches the protected original without reflection Invoke's exception wrapping.
    private static Func<CardModel, PlayerChoiceContext, CardPlay, Task> OriginalOnPlay()
        => _originalOnPlay ??= AccessTools.MethodDelegate<Func<CardModel, PlayerChoiceContext, CardPlay, Task>>(
            OriginalOnPlayMethod(), virtualCall: true);

    private static readonly Func<CardModel, PlayerChoiceContext, CardPlay, Task> RitsuOnPlay
        = STS2RitsuLib.Cards.CardOnPlayHook.RunCardOnPlayHooks;
    private static MethodInfo RitsuOnPlayMethod()
    {
        var method = typeof(STS2RitsuLib.Cards.CardOnPlayHook).GetMethod(
            nameof(STS2RitsuLib.Cards.CardOnPlayHook.RunCardOnPlayHooks),
            BindingFlags.Public | BindingFlags.Static, null,
            [typeof(CardModel), typeof(PlayerChoiceContext), typeof(CardPlay)], null);
        if (method is null || method.ReturnType != typeof(Task))
            throw PatchFailure("exact RitsuLib Task RunCardOnPlayHooks(CardModel, PlayerChoiceContext, CardPlay) is unavailable");
        return method;
    }
    private static Task DispatchOriginalOnPlay(CardModel model, PlayerChoiceContext context, CardPlay play)
        => DispatchOnPlay(model, context, play, OriginalOnPlay());
    private static Task DispatchRitsuOnPlay(CardModel model, PlayerChoiceContext context, CardPlay play)
        => DispatchOnPlay(model, context, play, RitsuOnPlay);
    private static Task DispatchOnPlay(CardModel model, PlayerChoiceContext context, CardPlay play,
        Func<CardModel, PlayerChoiceContext, CardPlay, Task> original)
    {
        // The fixed native IL always supplies a new non-null CardPlay. Preserve a foreign null call as-is.
        if (play is null) return original(model, context, play!);
        var lease = new PinnedLease(Get(play));
        var caller = Pinned.Value;
        Pinned.Value = lease;
        try
        {
            var task = original(model, context, play); // One selected dispatcher invocation; original Task unchanged.
            if (task is null || task.IsCompleted) lease.End();
            else _ = task.ContinueWith(static (_, state) => ((PinnedLease)state!).End(), lease,
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task!;
        }
        catch { lease.End(); throw; }
        finally { Pinned.Value = caller; }
    }
    private static InvalidOperationException PatchFailure(string detail)
    {
        string message = "Librarian Spine exact CardPlay dispatch patch closed: " + detail;
        Volatile.Write(ref _dispatchPatchFault, message);
        Volatile.Write(ref _dispatchPatchVerified, 0);
        // Harmony PatchAll reports this in its normal startup log. No silent partial patch.
        return new InvalidOperationException(message);
    }

    [HarmonyPatch]
    private static class NativeOnPlayDispatch
    {
        private static MethodBase TargetMethod()
        {
            var wrapper = typeof(CardModel).GetMethod(nameof(CardModel.OnPlayWrapper), BindingFlags.Instance | BindingFlags.Public);
            var state = wrapper?.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
            return state?.GetMethod(nameof(IAsyncStateMachine.MoveNext), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw PatchFailure("OnPlayWrapper AsyncStateMachineAttribute/MoveNext is unavailable");
        }
        [HarmonyPriority(Priority.Last)]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var codes = instructions.ToList();
            var original = OriginalOnPlayMethod();
            var ritsu = RitsuOnPlayMethod();
            int match = -1, count = 0;
            bool usesRitsu = false;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].operand is not MethodInfo called) continue;
                bool native = codes[i].opcode == OpCodes.Callvirt && called.Equals(original);
                bool redirected = codes[i].opcode == OpCodes.Call && called.Equals(ritsu);
                if (!native && !redirected) continue;
                match = i; count++; usesRitsu = redirected;
            }
            if (count != 1)
                throw PatchFailure($"{__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name}: expected one exact native or RitsuLib dispatch; found {count}");
            if (!usesRitsu) _ = OriginalOnPlay();
            // Preserve the dispatcher already selected by RitsuLib, including before/suppress/after hooks.
            // Replace only the exact verified call; labels, exception blocks and returned Task remain native.
            codes[match].opcode = OpCodes.Call;
            codes[match].operand = typeof(LibrarianSpineCardExecution).GetMethod(
                usesRitsu ? nameof(DispatchRitsuOnPlay) : nameof(DispatchOriginalOnPlay),
                BindingFlags.Static | BindingFlags.NonPublic)!;
            Volatile.Write(ref _dispatchPatchFault, null);
            Volatile.Write(ref _dispatchPatchVerified, 1);
            return codes;
        }
    }
    [HarmonyPatch(typeof(CardModel), nameof(CardModel.OnPlayWrapper))]
    private static class NativeExecution
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(CardModel __instance, out Invocation __state)
        {
            __state = new Invocation(new ExecutionScope(__instance, Ambient.Value), Pinned.Value);
            Ambient.Value = __state.Scope;
            Pinned.Value = null; // A nested/automatic wrapper owns its own Before scope and dispatch lease.
        }
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Task? __result, Invocation __state)
        {
            __state.RestoreCaller();
            if (__result is null || __result.IsCompleted) { __state.Scope.End(); return; }
            _ = __result.ContinueWith(static (_, state) => ((ExecutionScope)state!).End(), __state.Scope,
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            // __result is neither replaced nor awaited. Its native owner still observes fault/cancellation.
        }
        private static Exception? Finalizer(Exception? __exception, Invocation? __state)
        {
            if (__state is null) return __exception;
            __state.RestoreCaller();
            if (__exception is not null) __state.Scope.End();
            return __exception;
        }
    }
    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCardPlayed))]
    private static class BeforeNativeCardPlay
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(CardPlay cardPlay) => Before(cardPlay);
    }
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardPlayed))]
    private static class AfterNativeCardPlay
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(CardPlay cardPlay) => After(cardPlay);
    }
}
