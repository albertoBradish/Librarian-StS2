using Godot;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace Librarian.Mechanics;

internal static partial class DevelopmentRuntimeAudit
{
    private static async Task Run041()
    {
        if (!OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("041 fixture requires isolated validation profile");
        string? focus = System.Environment.GetEnvironmentVariable("LIBRARIAN_041_FOCUS");
        bool revisionBeta3 = focus is "beta3" or "stable";
        bool revisionBeta2 = focus == "beta2";
        bool revision054 = focus == "054";
        bool revision061 = focus == "061";
        bool revision060 = focus == "060";
        bool revision053 = focus == "053";
        bool revision052 = focus == "052";
        bool revision051 = focus == "051";
        bool revision050 = focus == "050";
        bool revision044 = focus == "044";
        bool revision043 = focus == "043";
        bool revision042 = focus is "042" or "042-cards";
        bool visualOnly = focus == "visual";
        bool focused = revisionBeta3 || revisionBeta2 || visualOnly || focus == "interactions" || revision042 || revision043 || revision044 || revision050 || revision051 || revision052 || revision053 || revision054 || revision060 || revision061;
        if (revision051 || revision053)
        {
            await DevelopmentNotice051Audit.Run(NGame.Instance!.MainMenu!);
            if (System.Environment.GetEnvironmentVariable("LIBRARIAN_051_NOTICE_PHASE") == "restart") return;
        }
        if (!focused)
        {
            if (!revision042) await DevelopmentRevision041UiAudit.Run();
            DevelopmentRevision040UnlockAudit.Run();
        }
        if (revision050 || revision051) await DevelopmentRevision050Audit.Settings();
        if (revision060) await DevelopmentRevision060Audit.Settings();
        LibrarianUnlocks040.ApplyChoice(SaveManager.Instance.Progress, true);
        if (revisionBeta2) await DevelopmentBeta2Audit.Timeline(NGame.Instance!.MainMenu!);
        if (DevelopmentVisualAudit.Enabled && (!focused || revision050 || revision051))
            await DevelopmentVisualAudit.CaptureCharacterSelect(NGame.Instance!.MainMenu!);
        var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<LibrarianCharacter>(), true,
            ActModel.GetDefaultList(), [], "LIBRARIAN041", GameMode.Standard);
        _player = run.Players.Single();
        await FreshFight();
        if (!focused)
        {
            await DevelopmentRevision041CardsAudit.Run(_player, FreshFight);
            await FreshFight();
            await DevelopmentRevision041MechanicsAudit.Run(_player, FreshFight);
            await FreshFight();
        }
        if (!revisionBeta3 && !revisionBeta2 && !visualOnly && focus != "042-cards" && !revision043 && !revision044 && !revision050 && !revision051 && !revision052 && !revision053 && !revision054 && !revision060 && !revision061)
        {
            await DevelopmentRevision041InteractionsAudit.Run(_player, FreshFight);
            await FreshFight();
        }
        if (revisionBeta3) await DevelopmentBeta3Audit.Run(_player, FreshFight);
        else if (revisionBeta2) await DevelopmentBeta2Audit.Run(_player, FreshFight);
        else if (revision061) await DevelopmentRevision061Audit.Run(_player, FreshFight);
        else if (revision060) await DevelopmentRevision060Audit.Run(_player);
        else if (revision054) await DevelopmentRevision054Audit.Run(_player, FreshFight);
        else if (revision053)
        {
            await DevelopmentRevision052Audit.Run(_player, FreshFight);
            await DevelopmentStatus051Audit.Run(_player, FreshFight);
            await DevelopmentMerchant051Audit.Run(_player, FreshFight);
        }
        else if (revision052) await DevelopmentRevision052Audit.Run(_player, FreshFight);
        else if (revision051)
        {
            await DevelopmentStatus051Audit.Run(_player, FreshFight);
            await DevelopmentRevision050Audit.Run(_player, FreshFight);
            await DevelopmentMerchant051Audit.Run(_player, FreshFight);
        }
        else if (revision050) await DevelopmentRevision050Audit.Run(_player, FreshFight);
        else if (revision044) await DevelopmentRevision044Audit.Run(_player, FreshFight);
        else if (revision043) await DevelopmentRevision043Audit.Run(_player, FreshFight);
        else if (revision042) await DevelopmentRevision042Audit.Run(_player, FreshFight);
        else await DevelopmentRevision041VisualAudit.Run(_player);
        if (focus == "stable") await DevelopmentStableApiAudit.Run(_player, FreshFight);
        await SaveManager.Instance.SaveRun(null);
        var saved = SaveManager.Instance.LoadRunSave();
        Equal(true, saved.Success && saved.SaveData is not null, "041 real isolated save read");
        var restored = RunState.FromSerializable(saved.SaveData!);
        Equal(_player.Character.Id, restored.Players.Single().Character.Id, "041 serialized character");
        await NGame.Instance.ReturnToMainMenu();
        await RunManager.Instance.SetUpSavedSingleplayer(restored, saved.SaveData!);
        await NGame.Instance.LoadRun(restored, saved.SaveData!.PreFinishedRoom);
        await NGame.Instance.Transition.FadeIn();
        Equal(true, RunManager.Instance.IsInProgress, "041 real menu/new run/combat/save/reload");
        if (revisionBeta2) MainFile.Logger.Info("RUNTIME_BETA2_AUDIT_PASS menu=True newRun=True combat=True save=True reload=True liveMulticlient=False");
        MainFile.Logger.Info($"SAVE_RELOAD_AUDIT_PASS revision={(revision061 ? "061" : revision060 ? "060" : revision054 ? "054" : revision053 ? "053" : revision052 ? "052" : revision051 ? "051" : revision050 ? "050" : revision044 ? "044" : revision043 ? "043" : revision042 ? "042" : "041")} checks=3");
        if (revision061) MainFile.Logger.Info("RUNTIME_061_AUDIT_PASS menu=True newRun=True combat=True save=True reload=True liveMulticlient=False");
        if (revision060) MainFile.Logger.Info("RUNTIME_060_AUDIT_PASS menu=True newRun=True combat=True save=True reload=True liveMulticlient=False");
        if (revision054) MainFile.Logger.Info("RUNTIME_054_AUDIT_PASS menu=True newRun=True combat=True save=True reload=True liveMulticlient=False");
        if (revision053) MainFile.Logger.Info("RUNTIME_053_AUDIT_PASS menu=True newRun=True combat=True save=True reload=True liveMulticlient=False");
        if (revision052) MainFile.Logger.Info("RUNTIME_052_AUDIT_PASS menu=True newRun=True combat=True save=True reload=True liveMulticlient=False");
        if (revision051) MainFile.Logger.Info("RUNTIME_051_AUDIT_PASS menu=True newRun=True combat=True save=True reload=True liveMulticlient=False");
        if (revision050) MainFile.Logger.Info("RUNTIME_050_AUDIT_PASS menu=True newRun=True combat=True save=True reload=True liveMulticlient=False");
        if (revision044) MainFile.Logger.Info("RUNTIME_044_AUDIT_PASS menu=True newRun=True combat=True save=True reload=True liveMulticlient=False");
        if (revision042) MainFile.Logger.Info("RUNTIME_042_AUDIT_PASS native=True liveMulticlient=False");
        if (revision043) MainFile.Logger.Info("RUNTIME_043_AUDIT_PASS menu=True newRun=True combat=True save=True reload=True liveMulticlient=False");
        MainFile.Logger.Info($"RUNTIME_041_AUDIT_PASS native=True liveMulticlient=False focus={(visualOnly ? "visual-save" : focused ? "interactions-visual-save" : "full")}");
        if (revisionBeta3) { MainFile.Logger.Info("RUNTIME_BETA3_AUDIT_PASS menu=True newRun=True combat=True save=True reload=True"); await DevelopmentBeta3Audit.Architect(); }
        if ((revisionBeta3 || revisionBeta2 || revision060 || revision061) && System.Environment.GetEnvironmentVariable("LIBRARIAN_060_EXIT") == "1")
        {
            await NGame.Instance.ReturnToMainMenu();
            await NGame.Instance.ToSignal(NGame.Instance.GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
            NGame.Instance.Quit();
        }
    }
}
