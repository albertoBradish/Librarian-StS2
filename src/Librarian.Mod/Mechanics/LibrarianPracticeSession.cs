using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.OrbAdvancedBasics;
using Librarian.LibrarianCode.Cards.OrbBasics;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Achievements;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Actions;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Saves.Validation;
using STS2RitsuLib.Settings;
using NativePlayer = MegaCrit.Sts2.Core.Entities.Players.Player;
using NativeRunState = MegaCrit.Sts2.Core.Runs.RunState;

namespace Librarian.Mechanics;

/// <summary>
/// A real single-player Custom run using the native combat/action pipeline. API research
/// was checked independently against stable 0.107.1/59260271 and beta 0.111.0/41cef1ea.
/// NGame.StartRun is private; Create/SetUp/FinalizeStartingRelics/LoadRun/EnterRoom are
/// public in both versions. RunManager.ShouldSave=false suppresses end-of-run history,
/// statistics and ordinary run saves, but does NOT suppress combat discovery/FTUE saves
/// or replay writes. Consequently this session uses an independent ProgressState clone,
/// guards the actual ProgressSaveManager.SaveProgress entry and replay writer, and keeps
/// the disk's existing normal run untouched. No native source or BaseLib UI is embedded.
/// A fixed seed is repeatable within a release channel, not a cross-version RNG contract.
/// </summary>
public partial class LibrarianPracticeSession : Node
{
    internal const string Seed = "LIBRARIAN_LESSON_V1";
    internal const string Completed = "librarian_tutorial_completed_v1";
    internal static LibrarianPracticeSession? Instance { get; private set; }
    internal static bool Active => Instance is { _restored: false };
    internal NativePlayer Player { get; private set; } = null!;
    internal NativeRunState RunState { get; private set; } = null!;
    internal int Step { get; private set; }
    internal CardModel? CurrentCard { get; private set; }
    internal bool AwaitingAction { get; private set; }
    internal bool GuidanceActionPending => _actionRequested || _exiting;
    internal bool AllowEndTurn => AwaitingAction && IsEndTurnStep && !_actionRequested && !_exiting;
    private bool IsEndTurnStep => Step is 2 or 6 or 10;
    private ProgressState? _originalProgress;
    private FieldInfo? _mirrorBusyField;
    private bool _mirrorBusyOriginal, _mirrorHeld;
    private ProgressSaveManager? _progressManager;
    private FieldInfo? _progressStoreField;
    private ISaveStore? _originalProgressStore;
    private bool _starting = true, _runOwned, _busy, _exiting, _restored;
    private bool _played, _actionRequested, _overlayShown;
    private int _turnBeforeAction;
    private GameAction? _requestedAction;
    private Task? _exitTask;
    private NGame Game => NGame.Instance ?? throw new InvalidOperationException("Native game unavailable.");

    internal static async Task StartAsync()
    {
        if (Active) throw new InvalidOperationException("A Librarian lesson is already active.");
        var game = NGame.Instance ?? throw new InvalidOperationException("Native game unavailable.");
        if (game.MainMenu is null || RunManager.Instance.IsInProgress)
            throw new InvalidOperationException("The Librarian lesson must start from the single-player main menu.");
        var session = new LibrarianPracticeSession { Name = "LibrarianPracticeSession" };
        Instance = session;
        game.AddChild(session);
        try
        {
            MainFile.Logger.Info($"LIBRARIAN_PRACTICE_STARTUP stage=requested submenu={game.MainMenu?.SubmenuStack.Peek()?.GetType().Name ?? "none"}");
            // A Ritsu settings action requests a refresh after its callback returns.
            // Defer closing that host until the input callback has completed, and close
            // every submenu through the native stack so its lobby/settings cleanup runs.
            await game.ToSignal(game.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (session._exiting) return;
            var mainMenu = game.MainMenu
                ?? throw new InvalidOperationException("The main menu changed before the lesson could start.");
            if (RunManager.Instance.IsInProgress)
                throw new InvalidOperationException("A run started before the lesson could start.");
            // RitsuLib 0.6.2/0.6.4 opens its settings on a persistent overlay stack,
            // separate from MainMenu.SubmenuStack. Pop the actual native parent stack;
            // its StackModified handler then hides the overlay and its input backstop.
            int closedSettings = 0;
            foreach (var settings in Descendants(game).OfType<RitsuModSettingsSubmenu>()
                .Where(settings => settings.IsVisibleInTree()).ToArray())
            {
                if (settings.GetParent() is not NSubmenuStack settingsStack)
                    throw new InvalidOperationException("The active Ritsu settings host is not a native submenu stack.");
                while (settingsStack.Peek() is not null)
                {
                    settingsStack.Pop();
                    closedSettings++;
                }
            }
            MainFile.Logger.Info($"LIBRARIAN_PRACTICE_STARTUP stage=settings_closed count={closedSettings}");
            int closedSubmenus = 0;
            while (mainMenu.SubmenuStack.Peek() is not null)
            {
                mainMenu.SubmenuStack.Pop();
                closedSubmenus++;
            }
            MainFile.Logger.Info($"LIBRARIAN_PRACTICE_STARTUP stage=menus_closed count={closedSubmenus}");
            await game.ToSignal(game.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (session._exiting) return;
            if (Descendants(game).OfType<RitsuModSettingsSubmenu>().Any(settings => settings.IsVisibleInTree()))
                throw new InvalidOperationException("The Ritsu settings host did not close before the lesson.");
            // Keep this main menu until LoadRun replaces it. ReturnToMainMenu also
            // calls global run cleanup when there is no run; dependency cleanup hooks
            // can assume a network service exists and fail after the screen fades out.
            await game.Transition.FadeOut();
            MainFile.Logger.Info("LIBRARIAN_PRACTICE_STARTUP stage=menu_faded_out");
            if (session._exiting) return;
            var pendingSave = SaveManager.Instance.CurrentRunSaveTask;
            if (pendingSave is not null) await pendingSave;
            session._originalProgress = SaveManager.Instance.Progress;
            await session.HoldProgressMirrorAsync();
            var serialized = JsonSerializer.Deserialize<SerializableProgress>(
                JsonSerializer.Serialize(session._originalProgress.ToSerializable()))
                ?? throw new InvalidOperationException("Could not clone native progress for the lesson.");
            SaveManager.Instance.Progress = ProgressState.FromSerializable(serialized, new DeserializationContext());
            MainFile.Logger.Info("LIBRARIAN_PRACTICE_STARTUP stage=progress_isolated");
            var character = ModelDb.Character<LibrarianCharacter>();
            session.Player = NativePlayer.CreateForNewRun(character,
                SaveManager.Instance.GenerateUnlockStateFromProgress(), 1UL);
            session.RunState = NativeRunState.CreateForNewRun([session.Player],
                ActModel.GetDefaultList().Select(act => act.ToMutable()).ToArray(), [], GameMode.Custom, 0, Seed);
            RunManager.Instance.SetUpNewSingleplayer(session.RunState, shouldSave: false);
            session._runOwned = true;
            MainFile.Logger.Info("LIBRARIAN_PRACTICE_STARTUP stage=run_setup");
            // SetUp initializes the character's native starting inventory, so replace the
            // deck afterwards and before relic finalization/combat copies are created.
            session.Player.Deck.Clear(silent: true);
            session.Player.Deck.AddInternal(session.RunState.CreateCard<Spark>(session.Player), silent: true);
            session.Player.Deck.AddInternal(session.RunState.CreateCard<Trickle>(session.Player), silent: true);
            // A five-card deck avoids the native insufficient-draw speech bubble
            // covering the Orb illustration when the opening hand draws five.
            for (int i = 0; i < 3; i++)
                session.Player.Deck.AddInternal(session.RunState.CreateCard<LibrarianDefend>(session.Player), silent: true);
            RunManager.Instance.CombatReplayWriter.IsEnabled = false;
            RunManager.Instance.CombatReplayWriter.StopRecording();
            await RunManager.Instance.FinalizeStartingRelics();
            if (session._exiting) return;
            await game.LoadRun(session.RunState, null);
            MainFile.Logger.Info("LIBRARIAN_PRACTICE_STARTUP stage=run_loaded");
            if (session._exiting) return;
            NMapScreen.Instance?.Close(animateOut: false);
            var encounter = ModelDb.Encounter<TunnelerWeak>().ToMutable();
            // EnterRoom intentionally does not append a traveled-map history record.
            // Populate that public record explicitly so combat/relic bookkeeping remains
            // valid even if an unexpected defeat ends this unsaved practice run.
            session.RunState.AppendToMapPointHistory(MapPointType.Monster, RoomType.Monster, encounter.Id);
            await RunManager.Instance.EnterRoom(new CombatRoom(encounter, session.RunState));
            MainFile.Logger.Info("LIBRARIAN_PRACTICE_STARTUP stage=combat_entered");
            if (session._exiting) return;
            NMapScreen.Instance?.Close(animateOut: false);
            await game.Transition.FadeIn();
            session._starting = false;
            MainFile.Logger.Info($"LIBRARIAN_PRACTICE_START seed={Seed} mode=Custom shouldSave={RunManager.Instance.ShouldSave} deck=5");
            await session.PrepareStepAsync();
        }
        catch (Exception error)
        {
            MainFile.Logger.Error("LIBRARIAN_PRACTICE_START_FAILED " + error);
            await session.ExitAsync();
            throw;
        }
    }

    private static IEnumerable<Node> Descendants(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private bool PlayerReady()
    {
        var manager = RunManager.Instance;
        return _runOwned && manager.IsInProgress && !manager.IsCleaningUp
            && CombatManager.Instance.IsInProgress && !CombatManager.Instance.IsOverOrEnding
            && !CombatManager.Instance.PlayerActionsDisabled
            && !CombatManager.Instance.IsPaused && !manager.ActionExecutor.IsPaused
            && Player.PlayerCombatState?.Phase == PlayerTurnPhase.Play
            && Player.Creature.CombatState?.CurrentSide == CombatSide.Player
            && manager.ActionExecutor.CurrentlyRunningAction is null && !manager.ActionExecutor.IsRunning;
    }

    private static Assembly RitsuRuntimeAssembly() => AppDomain.CurrentDomain.GetAssemblies()
        .Single(assembly => assembly.GetName().Name == "STS2-RitsuLib.Runtime");

    private async Task HoldProgressMirrorAsync()
    {
        // SaveMirror's exception-filter IL cannot be safely rewritten by the pinned
        // Harmony/MonoMod. Use its existing reentrancy gate for this isolated session.
        // Drain any current synchronous save before holding the gate; preserve merges
        // and serialization, and restore the exact held value during every exit path.
        var type = RitsuRuntimeAssembly().GetType("STS2RitsuLib.Saves.ProgressMirrorStore", throwOnError: true)!;
        _mirrorBusyField = type.GetField("_isSavingMirror", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException(type.FullName, "_isSavingMirror");
        if (_mirrorBusyField.FieldType != typeof(bool)) throw new InvalidOperationException("Unsupported Ritsu progress mirror gate.");
        ulong deadline = Time.GetTicksMsec() + 10000;
        while ((bool)_mirrorBusyField.GetValue(null)!)
        {
            if (Time.GetTicksMsec() > deadline) throw new TimeoutException("Ritsu progress mirror did not finish saving.");
            await Game.ToSignal(Game.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        _mirrorBusyOriginal = (bool)_mirrorBusyField.GetValue(null)!;
        _mirrorBusyField.SetValue(null, true);
        _mirrorHeld = true;
        // The raw bridge also has nested exception regions that the pinned detour
        // emitter cannot rewrite. Its manager still writes through public ISaveStore.
        // Replace only that progress manager's store; reads retain the ordinary
        // profile, and all mutations are suppressed until native cleanup completes.
        var managerField = typeof(SaveManager).GetField("_progressSaveManager", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException(typeof(SaveManager).FullName, "_progressSaveManager");
        _progressManager = (ProgressSaveManager)managerField.GetValue(SaveManager.Instance)!;
        _progressStoreField = typeof(ProgressSaveManager).GetField("_saveStore", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException(typeof(ProgressSaveManager).FullName, "_saveStore");
        _originalProgressStore = (ISaveStore)_progressStoreField.GetValue(_progressManager)!;
        _progressStoreField.SetValue(_progressManager, new LibrarianPracticeProgressStore(_originalProgressStore));
    }

    private async Task WaitReadyAsync()
    {
        ulong deadline = Time.GetTicksMsec() + 30000;
        while (!_exiting && !PlayerReady())
        {
            if (Time.GetTicksMsec() > deadline) throw new TimeoutException("The lesson did not reach a ready native player turn.");
            await Game.ToSignal(Game.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private CardModel RequiredModel() => Step switch
    {
        0 or 5 => ModelDb.Card<Spark>(),
        1 or 3 or 8 => ModelDb.Card<Trickle>(),
        4 => ModelDb.Card<Springwater>(),
        7 => ModelDb.Card<TidalGravity>(),
        9 => ModelDb.Card<Renewal>(),
        _ => throw new InvalidOperationException("This lesson step has no card action.")
    };

    private async Task PrepareStepAsync()
    {
        if (_busy || _exiting) return;
        _busy = true;
        AwaitingAction = false;
        _played = _actionRequested = false;
        _requestedAction = null;
        UnsubscribeCard();
        try
        {
            await WaitReadyAsync();
            if (_exiting) return;
            if (!IsEndTurnStep && Step < 11)
            {
                var expected = RequiredModel();
                var hand = Player.PlayerCombatState!.Hand;
                CurrentCard = hand.Cards.FirstOrDefault(card => card.Id == expected.Id);
                if (CurrentCard is null)
                {
                    // A generated teaching card must be visible/playable even if a future
                    // character relic fills the hand. Remove only this isolated combat's filler.
                    if (hand.Cards.Count >= 10)
                        await CardPileCmd.RemoveFromCombat(hand.Cards.Last(), skipVisuals: false);
                    if (_exiting) return;
                    CurrentCard = Player.Creature.CombatState!.CreateCard(expected, Player);
                    await CardPileCmd.AddGeneratedCardToCombat(CurrentCard, PileType.Hand, Player);
                    if (_exiting) return;
                }
                if (CurrentCard.Pile?.Type != PileType.Hand)
                    throw new InvalidOperationException("The required teaching card could not enter the hand.");
                CurrentCard.Played += OnCurrentCardPlayed;
            }
            await WaitReadyAsync();
            while (!_exiting && NModalContainer.Instance?.OpenModal is not null)
                await Game.ToSignal(Game.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_exiting) return;
            await Game.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (_exiting || !PlayerReady()) return;
            _overlayShown = true;
            LibrarianPracticeOverlay.Create(this);
            MainFile.Logger.Info($"LIBRARIAN_PRACTICE_STEP step={Step} turn={Player.PlayerCombatState!.TurnNumber} card={CurrentCard?.Id.Entry ?? "none"}");
        }
        catch (Exception error)
        {
            MainFile.Logger.Error("LIBRARIAN_PRACTICE_STEP_FAILED " + error);
            _ = ExitAsync();
        }
        finally { _busy = false; }
    }

    private void OnCurrentCardPlayed() => _played = true;
    private void UnsubscribeCard()
    {
        if (CurrentCard is not null) CurrentCard.Played -= OnCurrentCardPlayed;
        CurrentCard = null;
    }

    internal void Continue()
    {
        if (_busy || _exiting || AwaitingAction || !_overlayShown || !PlayerReady()) return;
        var modal = NModalContainer.Instance?.OpenModal;
        if (modal is not null && modal is not LibrarianPracticeOverlay) return;
        if (modal is LibrarianPracticeOverlay) NModalContainer.Instance!.Clear();
        _overlayShown = false;
        if (Step == 11) { _ = ExitAsync(completed: true); return; }
        _turnBeforeAction = Player.PlayerCombatState!.TurnNumber;
        AwaitingAction = true;
        // The native hand cached CanPlay while the explanation blocked actions.
        // Refresh only this real target after changing the lesson gate; UpdateCard
        // re-evaluates native rules and removes the cached unplayable cost icon.
        if (CurrentCard is { } card && card.Pile?.Type == PileType.Hand
            && NPlayerHand.Instance?.GetCardHolder(card) is NHandCardHolder holder
            && GodotObject.IsInstanceValid(holder) && holder.IsInsideTree()
            && ReferenceEquals(holder.CardNode?.Model, card))
            holder.UpdateCard();
        MainFile.Logger.Info($"LIBRARIAN_PRACTICE_ACTION_READY step={Step} endTurn={AllowEndTurn}");
    }

    internal bool AllowsCard(CardModel card)
        => !_exiting && AwaitingAction && !IsEndTurnStep && ReferenceEquals(card, CurrentCard);

    internal bool AcceptAction(GameAction action)
    {
        if (action is PlayCardAction play && ReferenceEquals(play.Player, Player))
        {
            var card = play.NetCombatCard.ToCardModelOrNull();
            if (_actionRequested || card is null || !AllowsCard(card) || !PlayerReady()) return false;
            _actionRequested = true;
            _requestedAction = action;
        }
        else if (action is EndPlayerTurnAction && action.OwnerId == Player.NetId)
        {
            if (!AllowEndTurn || !PlayerReady()) return false;
            _actionRequested = true;
            _requestedAction = action;
            _turnBeforeAction = Player.PlayerCombatState!.TurnNumber;
        }
        else if (action is UndoEndPlayerTurnAction && action.OwnerId == Player.NetId) return false;
        return true;
    }

    public override void _Process(double delta)
    {
        if (_starting || _busy || _exiting || _restored) return;
        if (!RunManager.Instance.IsInProgress || Game.MainMenu is not null)
        {
            NativeCleanupFinished();
            return;
        }
        if (CombatManager.Instance.IsOverOrEnding || Player.Creature.IsDead)
        {
            _ = ExitAsync();
            return;
        }
        bool finishedAction = AwaitingAction && (IsEndTurnStep
            ? _actionRequested && Player.PlayerCombatState!.TurnNumber > _turnBeforeAction
            : _played && CurrentCard?.Pile?.Type != PileType.Hand);
        if (finishedAction && PlayerReady())
        {
            AwaitingAction = false;
            _overlayShown = false;
            Step++;
            _ = PrepareStepAsync();
        }
        else if (AwaitingAction && _actionRequested && !_played && PlayerReady()
            && (_requestedAction?.CompletionTask.IsCompleted == true
                || _requestedAction?.State == GameActionState.Canceled))
        {
            // Native cancellation/fizzle is not proof of playing the card or ending the
            // turn. Re-enable the same task; regenerate its card if native cancellation
            // already moved it out of the hand, and never advance the lesson here.
            _actionRequested = false;
            _requestedAction = null;
            if (!IsEndTurnStep && CurrentCard?.Pile?.Type != PileType.Hand)
            {
                AwaitingAction = false;
                _overlayShown = false;
                _ = PrepareStepAsync();
            }
            MainFile.Logger.Info($"LIBRARIAN_PRACTICE_RETRY step={Step}");
        }
        else if (!AwaitingAction && !_overlayShown && NModalContainer.Instance?.OpenModal is null)
        {
            // Retry if a pause/menu transition occupied the modal slot for a frame.
            _ = PrepareStepAsync();
        }
    }

    internal Task ExitAsync(bool completed = false)
        => _exitTask ??= ExitCoreAsync(completed && Step == 11 && !AwaitingAction);

    private async Task ExitCoreAsync(bool completed)
    {
        _exiting = true;
        AwaitingAction = false;
        UnsubscribeCard();
        try
        {
            if (NModalContainer.Instance?.OpenModal is LibrarianPracticeOverlay)
                NModalContainer.Instance.Clear();
            if (_runOwned && RunManager.Instance.IsInProgress)
            {
                // All lesson cards are self-targeted and never require a player choice.
                // Let an already executing native effect finish before restoring progress;
                // new card/end-turn requests are already blocked by _exiting.
                CombatManager.Instance.Unpause();
                RunManager.Instance.ActionExecutor.Unpause();
                await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
            }
            if (_runOwned)
            {
                // Native cleanup cancels combat/choices/action queues and disposes the
                // replay writer. Keep all write guards active until it has finished.
                await Game.ReturnToMainMenu();
            }
            else if (Game.MainMenu is not null)
            {
                // A failure before setup has no native run to clean up. Restore the
                // existing main menu's visibility instead of invoking global cleanup.
                await Game.Transition.FadeIn();
                MainFile.Logger.Info("LIBRARIAN_PRACTICE_EXIT reason=pre_run_menu_restored");
            }
        }
        catch (Exception error)
        {
            MainFile.Logger.Error("LIBRARIAN_PRACTICE_EXIT_FAILED " + error);
            if (_runOwned && RunManager.Instance.IsInProgress) RunManager.Instance.CleanUp();
            throw;
        }
        finally
        {
            RestoreProgress();
            if (completed) LibrarianOnboarding.Record(Completed);
            MainFile.Logger.Info($"LIBRARIAN_PRACTICE_EXIT completed={completed} step={Step} originalProgressRestored={_restored}");
            QueueFree();
        }
    }

    internal void NativeCleanupFinished()
    {
        if (!_runOwned || _exiting || _restored) return;
        _exiting = true;
        AwaitingAction = false;
        UnsubscribeCard();
        RestoreProgress();
        MainFile.Logger.Info($"LIBRARIAN_PRACTICE_EXIT completed=False step={Step} reason=native_cleanup");
        QueueFree();
    }

    private void RestoreProgress()
    {
        if (_restored) return;
        if (_originalProgress is not null) SaveManager.Instance.Progress = _originalProgress;
        if (_originalProgressStore is not null)
            _progressStoreField!.SetValue(_progressManager, _originalProgressStore);
        if (_mirrorHeld)
        {
            _mirrorBusyField!.SetValue(null, _mirrorBusyOriginal);
            _mirrorHeld = false;
        }
        _restored = true;
        if (ReferenceEquals(Instance, this)) Instance = null;
        // Deliberately do not save here: the original disk profile/run was never written.
        // Only a successful completion records one character-specific receipt afterwards.
    }

    public override void _ExitTree()
    {
        if (!_restored)
        {
            _exiting = true;
            if (_runOwned && RunManager.Instance.IsInProgress) RunManager.Instance.CleanUp(graceful: false);
            UnsubscribeCard();
            RestoreProgress();
        }
    }
}

/// <summary>Read-through storage for only the native progress manager of an unsaved lesson.</summary>
internal sealed class LibrarianPracticeProgressStore(ISaveStore original) : ISaveStore
{
    internal int SuppressedWrites { get; private set; }
    public string? ReadFile(string path) => original.ReadFile(path);
    public Task<string?> ReadFileAsync(string path) => original.ReadFileAsync(path);
    public bool FileExists(string path) => original.FileExists(path);
    public bool DirectoryExists(string path) => original.DirectoryExists(path);
    public string[] GetFilesInDirectory(string path) => original.GetFilesInDirectory(path);
    public string[] GetDirectoriesInDirectory(string path) => original.GetDirectoriesInDirectory(path);
    public DateTimeOffset GetLastModifiedTime(string path) => original.GetLastModifiedTime(path);
    public int GetFileSize(string path) => original.GetFileSize(path);
    public string GetFullPath(string path) => original.GetFullPath(path);
    public void WriteFile(string path, string content) => SuppressedWrites++;
    public void WriteFile(string path, byte[] content) => SuppressedWrites++;
    public Task WriteFileAsync(string path, string content) { SuppressedWrites++; return Task.CompletedTask; }
    public Task WriteFileAsync(string path, byte[] content) { SuppressedWrites++; return Task.CompletedTask; }
    public void DeleteFile(string path) { }
    public void RenameFile(string sourcePath, string destinationPath) { }
    public void CreateDirectory(string path) { }
    public void DeleteDirectory(string path) { }
    public void DeleteTemporaryFiles(string path) { }
    public void SetLastModifiedTime(string path, DateTimeOffset time) { }
}

// Every guard is limited to the lifetime of the dedicated single-player lesson. Ordinary
// games and multiplayer keep their native progression, persistence and input paths.
[HarmonyPatch]
internal static class LibrarianPracticeCardGuard
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(CardModel), nameof(CardModel.CanPlay),
        [typeof(UnplayableReason).MakeByRefType(), typeof(AbstractModel).MakeByRefType()]);
    [HarmonyPostfix] private static void Postfix(CardModel __instance, ref bool __result,
        ref UnplayableReason reason, ref AbstractModel? preventer)
    {
        var session = LibrarianPracticeSession.Instance;
        if (!LibrarianPracticeSession.Active || session is null || session.Player is null || !__instance.IsMutable
            || !ReferenceEquals(__instance.RunState, session.RunState) || !ReferenceEquals(__instance.Owner, session.Player)
            || session.AllowsCard(__instance)) return;
        __result = false;
        reason |= UnplayableReason.BlockedByCardLogic;
        preventer = null;
    }
}

[HarmonyPatch(typeof(NEndTurnButton), nameof(NEndTurnButton.CallReleaseLogic))]
internal static class LibrarianPracticeEndTurnGuard
{
    [HarmonyPrefix] private static bool Prefix()
        => !LibrarianPracticeSession.Active || LibrarianPracticeSession.Instance!.AllowEndTurn;
}

[HarmonyPatch(typeof(ActionQueueSynchronizer), nameof(ActionQueueSynchronizer.RequestEnqueue))]
internal static class LibrarianPracticeActionGuard
{
    [HarmonyPrefix] private static bool Prefix(GameAction action)
        => !LibrarianPracticeSession.Active || LibrarianPracticeSession.Instance!.AcceptAction(action);
}

[HarmonyPatch]
internal static class LibrarianPracticeProgressGuard
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(ProgressSaveManager), nameof(ProgressSaveManager.SaveProgress));
        yield return AccessTools.Method(typeof(SaveManager), nameof(SaveManager.SaveProgressFile));
    }
    [HarmonyPrefix, HarmonyPriority(Priority.First)] private static bool Prefix() => !LibrarianPracticeSession.Active;
}

[HarmonyPatch]
internal static class LibrarianPracticeRunSaveGuard
{
    private static IEnumerable<MethodBase> TargetMethods()
        => new[] { typeof(SaveManager), typeof(RunSaveManager) }
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.Name == "SaveRun" && method.ReturnType == typeof(Task));
    [HarmonyPrefix] private static bool Prefix(ref Task __result)
    {
        if (!LibrarianPracticeSession.Active) return true;
        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch]
internal static class LibrarianPracticeRunDeleteGuard
{
    private static IEnumerable<MethodBase> TargetMethods()
        => new[] { typeof(SaveManager), typeof(RunSaveManager) }
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.Name is "DeleteCurrentRun" or "DeleteCurrentMultiplayerRun");
    [HarmonyPrefix] private static bool Prefix() => !LibrarianPracticeSession.Active;
}

[HarmonyPatch]
internal static class LibrarianPracticeReplayGuard
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(RunManager), nameof(RunManager.WriteReplay));
        yield return AccessTools.Method(typeof(CombatReplayWriter), nameof(CombatReplayWriter.WriteReplay));
    }
    [HarmonyPrefix] private static bool Prefix() => !LibrarianPracticeSession.Active;
}

[HarmonyPatch]
internal static class LibrarianPracticeAchievementGuard
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        // These four AchievementsHelper methods are empty in both fixed releases. The
        // scoped guards also exclude direct platform Architect writes during the lesson.
        foreach (var method in typeof(AchievementsHelper).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            if (method.ReturnType == typeof(void)) yield return method;
        yield return AccessTools.Method(typeof(StatsManager), nameof(StatsManager.IncrementArchitectDamage));
        yield return AccessTools.Method(typeof(SteamStatsManager), nameof(SteamStatsManager.IncrementArchitectDamage));
    }
    [HarmonyPrefix] private static bool Prefix() => !LibrarianPracticeSession.Active;
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SeenFtue))]
internal static class LibrarianPracticeNativeFtueGuard
{
    [HarmonyPrefix] private static bool Prefix(ref bool __result)
    {
        if (!LibrarianPracticeSession.Active) return true;
        // Prevent unrelated original combat FTUEs competing for the lesson's modal slot;
        // do not change either the original or cloned global tutorial switch/receipts.
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
internal static class LibrarianPracticeCleanupGuard
{
    [HarmonyPostfix] private static void Postfix()
        => LibrarianPracticeSession.Instance?.NativeCleanupFinished();
}
