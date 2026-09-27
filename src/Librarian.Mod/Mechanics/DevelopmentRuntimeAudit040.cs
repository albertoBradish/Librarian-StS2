using Godot;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;

namespace Librarian.Mechanics;

internal static partial class DevelopmentRuntimeAudit
{
    private static async Task Run040()
    {
        bool visualTail = System.Environment.GetEnvironmentVariable("LIBRARIAN_040_VISUAL_TAIL") == "1";
        DevelopmentRevision040UnlockAudit.Run();
        if (DevelopmentVisualAudit.Enabled && !visualTail)
        {
            await DevelopmentRevision040UnlockUiAudit.Run(NGame.Instance!.MainMenu!);
            await DevelopmentVisualAudit.CaptureCharacterSelect(NGame.Instance!.MainMenu!);
        }
        // Dedicated disposable fixture only: never use a tester's active profile for this choice.
        if (!OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("040 fixture requires isolated validation profile");
        LibrarianUnlocks040.ApplyChoice(SaveManager.Instance.Progress, true);
        if (DevelopmentVisualAudit.Enabled && !visualTail)
        {
            // Complete native introduction only in this disposable profile so the normal timeline can open.
            SaveManager.Instance.Progress.ObtainEpochOverride("NEOW_EPOCH", EpochState.Revealed);
            await DevelopmentRevision040NativeUnlockEventUiAudit.RunTimelineUi(NGame.Instance!.MainMenu!);
        }
        var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<LibrarianCharacter>(), true,
            ActModel.GetDefaultList(), [], "LIBRARIAN040", GameMode.Standard);
        _player = run.Players.Single();
        await FreshFight();
        string? boundaryMode = System.Environment.GetEnvironmentVariable("LIBRARIAN_040_BOUNDARY_ONLY");
        bool boundaryOnly = boundaryMode is "1" or "water";
        if (boundaryOnly)
        {
            if (boundaryMode != "water") await DevelopmentRevision0310CardsAudit.Run(_player, FreshFight);
            await FreshFight();
            await DevelopmentRevision040WaterBoundaryAudit.Run(_player);
        }
        if (!boundaryOnly && System.Environment.GetEnvironmentVariable("LIBRARIAN_040_VISUAL_ONLY") != "1")
        {
            if (System.Environment.GetEnvironmentVariable("LIBRARIAN_040_FOREIGN_ONLY") != "1")
                await DevelopmentRevision040CardsAudit.Run(_player, FreshFight);
            await FreshFight();
            for (int i=0; _player.PlayerCombatState!.Phase != PlayerTurnPhase.Play; i++)
            {
                if (i>900) throw new TimeoutException("040 multiplayer fixture phase");
                await NGame.Instance.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            var ally=Player.CreateForNewRun<Ironclad>(UnlockState.all, 40401);
            ally.RunState=_player.RunState;ally.ResetCombatState();
            var combat=(CombatState)_player.Creature.CombatState!;
            combat.AddPlayer(ally);
            var room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance!;
            if (room.GetCreatureNode(ally.Creature) is null) room.AddCreature(ally.Creature);
            try
            {
                DevelopmentRevision040UnlockAudit.RunCrossCharacter(ally);
                await DevelopmentRevision040CardsAudit.RunForeignTranscribe(ally);
                await DevelopmentRevision040SeaGlassPlayAudit.Run(ally);
                ally.Creature.LoseBlockInternal(ally.Creature.Block);
                await DevelopmentRevision040CardsAudit.RunWaterSpirit(_player,ally);
            }
            finally {combat.RemoveCreature(ally.Creature);}
        }
        await FreshFight();
        if (DevelopmentVisualAudit.Enabled)
        {
            if (!visualTail)
            {
                // Visual reruns also exercise the actual cross-character event selector.
                var eventRecipient = Player.CreateForNewRun<Ironclad>(UnlockState.all, 40402);
                eventRecipient.RunState = _player.RunState;
                eventRecipient.ResetCombatState();
                await DevelopmentRevision040NativeUnlockEventUiAudit.RunEventUi(eventRecipient);
                await DevelopmentRevision040VisualAudit.Run(_player);
                await DevelopmentRevision040RemoteVisualAudit.Run(_player);
            }
            await DevelopmentRevision040CombatVisualAudit.Run(_player);
            if (!visualTail)
            {
                await DevelopmentRevision0311OrbVisualAudit.Run(_player);
                await DevelopmentRevision038RelicVisualAudit.Run(_player);
            }
            await DevelopmentRevision040RoomsVisualAudit.Run(_player);
            await FreshFight();
        }
        await SaveManager.Instance.SaveRun(null);
        var saved=SaveManager.Instance.LoadRunSave();
        Equal(true,saved.Success && saved.SaveData is not null,"040 real isolated save read");
        var restored=RunState.FromSerializable(saved.SaveData!);
        Equal(_player.Character.Id,restored.Players.Single().Character.Id,"040 serialized character");
        await NGame.Instance.ReturnToMainMenu();
        await RunManager.Instance.SetUpSavedSingleplayer(restored,saved.SaveData!);
        await NGame.Instance.LoadRun(restored,saved.SaveData!.PreFinishedRoom);
        await NGame.Instance.Transition.FadeIn();
        Equal(true,RunManager.Instance.IsInProgress,"040 real menu/new run/combat/save/reload");
        MainFile.Logger.Info("SAVE_RELOAD_AUDIT_PASS revision=040 checks=3");
        MainFile.Logger.Info("RUNTIME_040_AUDIT_PASS native=True liveMulticlient=False");
    }
}
