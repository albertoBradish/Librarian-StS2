using System.IO;
using Godot;
using HarmonyLib;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Cards.Stateful;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Timeline;
using STS2RitsuLib.Settings;

namespace Librarian.Mechanics;

/// <summary>Explicit opt-in checks in the disposable beta profile, never normal player startup.</summary>
internal static class DevelopmentRevision102Audit
{
    private static int _checks;
    private static string Output => System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT")
        ?? @"D:\Slay The Spire_Mod Dev\outputs\revision-v1.0.2-beta1\screenshots";
    private static void Check(bool ok, string text)
    {
        if (!ok) throw new InvalidOperationException("102: " + text);
        _checks++; MainFile.Logger.Info("V102_CHECK_PASS " + text);
    }
    private static async Task Wait(double seconds = .2) => await NGame.Instance!.ToSignal(
        NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private static IEnumerable<Node> Desc(Node root)
    {
        foreach (var node in root.GetChildren()) { yield return node; foreach (var child in Desc(node)) yield return child; }
    }
    private static async Task Shot(string name)
    {
        Directory.CreateDirectory(Output);
        await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
        Check(image.SavePng(Path.Combine(Output, name + ".png")) == Error.Ok, "capture " + name);
    }
    private static void DisableSchedulers(NMainMenu menu)
    {
        foreach (var node in Desc(menu).Where(n => n is LibrarianUpdateNotice051 or LibrarianArchitectReview102)) node.SetProcess(false);
    }
    private static void Fixture(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) File.Copy(path, path + ".before-102-audit", true);
        File.WriteAllText(path, "{}");
    }
    internal static async Task Menu(NMainMenu menu, string expectedVersion = "1.0.2-beta1")
    {
        Check(OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase), "isolated validation profile");
        Check(LibrarianUpdateNotice051.CurrentVersion == expectedVersion, "actual loaded candidate version");
        var librarianId = ModelDb.Character<LibrarianCharacter>().Id;
        Check(LibrarianArchitectVictoryReview102.IsEligible(true,true,librarianId,1), "first local Architect victory qualifies");
        Check(!LibrarianArchitectVictoryReview102.IsEligible(true,true,librarianId,2), "later victory excluded");
        Check(!LibrarianArchitectVictoryReview102.IsEligible(true,false,librarianId,1), "ordinary victory excluded");
        Check(!LibrarianArchitectVictoryReview102.IsEligible(false,true,librarianId,1), "defeat excluded");
        Check(!LibrarianArchitectVictoryReview102.IsEligible(true,true,ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Ironclad>().Id,1), "other local character excluded");
        if (System.Environment.GetEnvironmentVariable("LIBRARIAN_102_PHASE") == "restart")
        {
            Check(LibrarianNoticeHistory051.IsAcknowledged(expectedVersion), "suppression read from previous process");
            Check(LibrarianArchitectReviewHistory102.Shown && !LibrarianArchitectReviewHistory102.Pending, "actual review consumed across process restart");
            await Wait(3);
            Check(NModalContainer.Instance!.OpenModal is null, "suppressed welcome and consumed review stay quiet on restart");
            MainFile.Logger.Info("V102_RESTART_AUDIT_PASS");
            NGame.Instance!.Quit(); return;
        }
        DisableSchedulers(menu);
        var container = NModalContainer.Instance!;
        container.Clear(); await Wait();
        await Wait(2.2); // Capture after the native initial menu fade.
        Fixture(LibrarianNoticeHistory051.FilePath);
        Fixture(LibrarianArchitectReviewHistory102.FilePath);
        LibrarianUpdateNotice051.ResetSessionForAudit();
        LibrarianArchitectReviewHistory102.ForgetSessionForAudit();
        var progress = SaveManager.Instance.Progress;
        var epochs = progress.Epochs.Select(e => (e.Id, e.State, e.ObtainDate)).ToArray();
        var markers = progress.FtueCompleted.Order().ToArray();
        int opened = 0; string? url = null;
        foreach (string lang in new[] { "zhs", "eng" })
        {
            LibrarianLanguage.Select(lang);
            foreach (var size in new[] { new Vector2I(1280,720), new Vector2I(1920,1080) })
            {
                DisplayServer.WindowSetSize(size); await Wait(.3);
                var popup = LibrarianUpdateNotice051.Show(expectedVersion, u => { opened++; url = u; })!; await Wait();
                var panel = popup.GetNode<NVerticalPopup>("VerticalPopup");
                var third = Desc(panel).OfType<NPopupYesNoButton>().Single(b => b != panel.YesButton && b != panel.NoButton);
                string body = panel.GetNode<RichTextLabel>("Description").GetParsedText();
                string firstLine = body.Split('\n')[0];
                Check(!firstLine.Contains("BaseLib") && firstLine.Contains("RitsuLib")
                    && body.Contains(expectedVersion) && body.Contains("1091648383"), "welcome starts with the required RitsuLib dependency and settings route " + lang);
                Check(third.Visible && third.Position.X > panel.NoButton.Position.X && third.Position.X < panel.YesButton.Position.X, "suppression button in middle " + lang);
                await Shot("welcome-" + lang + "-" + size.X);
                panel.YesButton.EmitSignal(NClickableControl.SignalName.Released, panel.YesButton); await Wait();
                Check(container.OpenModal is null && !LibrarianNoticeHistory051.IsAcknowledged(expectedVersion), "OK closes without suppression " + lang);
            }
            var notice = LibrarianUpdateNotice051.Show(expectedVersion, u => { opened++; url = u; })!; await Wait();
            var buttons = notice.GetNode<NVerticalPopup>("VerticalPopup");
            buttons.NoButton.EmitSignal(NClickableControl.SignalName.Released, buttons.NoButton);
            Check(url == LibrarianUpdateNotice051.WorkshopUrl && ReferenceEquals(container.OpenModal, notice), "workshop link exact and dialog retained " + lang);
            int before = opened;
            var hotkeys = (Dictionary<StringName,List<Action>>)AccessTools.Field(typeof(NHotkeyManager), "_hotkeyReleasedBindings").GetValue(NHotkeyManager.Instance)!;
            hotkeys[MegaInput.cancel][^1](); await Wait();
            Check(opened == before && container.OpenModal is null && !LibrarianNoticeHistory051.IsAcknowledged(expectedVersion), "escape dismisses safely " + lang);
            foreach (string section in new[] { "display", "diagnostics" })
            {
                var result = await ModSettingsNavigator.OpenByIdsAsync("Librarian", LibrarianSettings041.PageId, sectionId: section,
                    options: new ModSettingsOpenOptions { Highlight=false, Focus=true });
                Check(result.Success, "settings section available " + section + " " + lang); await Wait(.4);
                await Shot("settings-" + section + "-" + lang);
                var submenu = Desc(NGame.Instance!).OfType<RitsuModSettingsSubmenu>().Last(n => n.Visible);
                if (submenu.GetParent() is NSubmenuStack stack && ReferenceEquals(stack.Peek(), submenu)) stack.Pop(); await Wait();
            }
            var review = LibrarianArchitectReview102.Show(preview:true, openLink:u => { opened++; url=u; })!; await Wait();
            var reviewPanel = review.GetNode<NVerticalPopup>("VerticalPopup");
            string reviewText = reviewPanel.GetNode<RichTextLabel>("Description").GetParsedText();
            Check(reviewText.Contains("1091648383") && reviewText.Contains("GitHub Issues"), "review feedback channels " + lang);
            await Shot("review-preview-" + lang);
            reviewPanel.NoButton.EmitSignal(NClickableControl.SignalName.Released, reviewPanel.NoButton);
            Check(url == LibrarianUpdateNotice051.WorkshopUrl, "review links corresponding beta item " + lang);
            var feedback = Desc(reviewPanel).OfType<NPopupYesNoButton>().Single(b => b != reviewPanel.YesButton && b != reviewPanel.NoButton);
            feedback.EmitSignal(NClickableControl.SignalName.Released, feedback);
            Check(url == "https://github.com/albertoBradish/Librarian-StS2/issues", "review feedback dispatches Issues " + lang);
            reviewPanel.YesButton.EmitSignal(NClickableControl.SignalName.Released, reviewPanel.YesButton); await Wait();
            Check(!LibrarianArchitectReviewHistory102.Shown && !LibrarianArchitectReviewHistory102.Pending, "preview leaves victory history untouched " + lang);
            foreach (int n in Enumerable.Range(1,7))
            {
                string story = new LocString("epochs", LibrarianUnlocks040.Id(n) + ".description").GetFormattedText();
                Check(story.Length > 80 && !story.Contains("yet to be revealed") && !story.Contains("未来揭晓"), "timeline narrative resolves " + lang + " " + n);
            }
            var card = ModelDb.Card<Transcribe>().ToMutable();
            string text = card.GetDescriptionForPile(PileType.None);
            Check(lang == "zhs" ? text.Contains("复制品") && !text.Contains("副本") : text.Contains("cop"), "Transcribe description only " + lang);
        }
        LibrarianLanguage.Select("zhs"); DisplayServer.WindowSetSize(new(1280,720));
        var suppress = LibrarianUpdateNotice051.Show(expectedVersion, _ => { })!; await Wait();
        var suppressPanel = suppress.GetNode<NVerticalPopup>("VerticalPopup");
        Desc(suppressPanel).OfType<NPopupYesNoButton>().Single(b => b != suppressPanel.YesButton && b != suppressPanel.NoButton)
            .EmitSignal(NClickableControl.SignalName.Released, Desc(suppressPanel).OfType<NPopupYesNoButton>().Single(b => b != suppressPanel.YesButton && b != suppressPanel.NoButton));
        await Wait(); LibrarianNoticeHistory051.ForgetSessionForAudit();
        Check(LibrarianNoticeHistory051.IsAcknowledged(expectedVersion), "explicit suppression persisted on disk");
        Check(LibrarianUpdateNotice051.RequestRedisplay(), "debug welcome replay schedules");
        Check(!LibrarianNoticeHistory051.IsAcknowledged(expectedVersion), "debug welcome replay clears suppression");
        await Wait(3);
        Check(container.OpenModal is Node { Name: var welcomeName } && welcomeName == "LibrarianUpdateNotice", "debug welcome redisplays at main menu");
        container.Clear(); await Wait(); DisableSchedulers(menu);
        LibrarianNoticeHistory051.Acknowledge(expectedVersion);
        Check(LibrarianArchitectReview102.RequestPreview(), "debug review preview schedules"); await Wait(3);
        Check(container.OpenModal is Node { Name: var previewName } && previewName == "LibrarianArchitectReview", "debug review preview displayed");
        container.Clear(); await Wait(); DisableSchedulers(menu);
        Check(epochs.SequenceEqual(progress.Epochs.Select(e => (e.Id,e.State,e.ObtainDate))) && markers.SequenceEqual(progress.FtueCompleted.Order()), "notice/debug actions preserve progression");
        MainFile.Logger.Info($"V102_MENU_AUDIT_PASS checks={_checks}");
    }
    internal static async Task Timeline(NMainMenu menu)
    {
        DisableSchedulers(menu);
        SaveManager.Instance.Progress.ObtainEpochOverride("NEOW_EPOCH", EpochState.Revealed);
        foreach (string lang in new[] { "zhs", "eng" })
        {
            LibrarianLanguage.Select(lang);
            var timeline = menu.SubmenuStack.PushSubmenuType<NTimelineScreen>(); await Wait(1);
            var inspect = timeline.GetNode<NEpochInspectScreen>("%EpochInspectScreen");
            foreach (int number in Enumerable.Range(1,7))
            {
                var slot = Desc(timeline).OfType<NEpochSlot>().Single(s => s.model.Id == LibrarianUnlocks040.Id(number));
                timeline.OpenInspectScreen(slot,playAnimation:false); await Wait(.3);
                await Shot("story-" + lang + "-" + number); inspect.Close(); await Wait(.15);
            }
            menu.SubmenuStack.Pop(); await Wait(.3);
        }
        LibrarianLanguage.Select("zhs"); MainFile.Logger.Info("V102_TIMELINE_AUDIT_PASS languages=2 chapters=7");
    }
    internal static async Task Architect()
    {
        await NGame.Instance!.ReturnToMainMenu(); DisableSchedulers(NGame.Instance.MainMenu!);
        Check(!LibrarianArchitectReviewHistory102.Pending && !LibrarianArchitectReviewHistory102.Shown, "ordinary combat/run save did not qualify for review");
        await NGame.Instance.StartNewSingleplayerRun(ModelDb.Character<LibrarianCharacter>(), true, ActModel.GetDefaultList(), [], "LIBRARIAN102ARCHITECT", GameMode.Standard);
        SaveManager.Instance.Progress.GetOrCreateCharacterStats(ModelDb.Character<LibrarianCharacter>().Id).TotalWins = 0;
        var room = (EventRoom)await RunManager.Instance.EnterRoomDebug(RoomType.Event, model:ModelDb.Event<TheArchitect>());
        await Wait(3.5); await SaveManager.Instance.SaveRun(null);
        var ev = room.LocalMutableEvent;
        for (int i=0;i<3;i++) { Check(ev.CurrentOptions.Count == 1, "native Architect option " + i); await ev.CurrentOptions[0].Chosen(); await Wait(i==2 ? 2 : .5); }
        Check(LibrarianArchitectReviewHistory102.Pending && !LibrarianArchitectReviewHistory102.Shown, "native Architect victory persists pending review");
        await NGame.Instance.ReturnToMainMenu(); await Wait(3);
        Check(NModalContainer.Instance!.OpenModal is Node { Name: var reviewName } && reviewName == "LibrarianArchitectReview", "first Architect completion prompts on free main menu");
        Check(LibrarianArchitectReviewHistory102.Shown && !LibrarianArchitectReviewHistory102.Pending, "actual display consumes first reminder");
        await Shot("review-after-native-architect");
        NModalContainer.Instance.Clear(); await Wait();
        await NGame.Instance.ReturnToMainMenu(); await Wait(3);
        Check(NModalContainer.Instance.OpenModal is null, "review not repeated on subsequent menu visit");
        MainFile.Logger.Info($"V102_ARCHITECT_AUDIT_PASS checks={_checks} nativeFinale=True");
    }
}
