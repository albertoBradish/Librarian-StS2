using System.IO;
using Godot;
using HarmonyLib;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Timeline;

namespace Librarian.Mechanics;

/// <summary>Explicit isolated visual checks. SeaGlass uses the reviewed broader foreign pool.</summary>
internal static class DevelopmentRevision040NativeUnlockEventUiAudit
{
    private const string Output = @"D:\Slay The Spire_Mod Dev\.research\revision-v040-screenshots";
    private static void Require(bool condition, string text)
    {
        if (!condition) throw new InvalidOperationException("040 native unlock/event UI: " + text);
        MainFile.Logger.Info("REVISION040_NATIVE_UNLOCK_EVENT_UI_PASS " + text);
    }
    private static void Guard()
    {
        Require(OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase), "isolated profile");
        Require(DisplayServer.GetName() != "headless", "native rendering enabled");
        Directory.CreateDirectory(Output);
    }
    private static async Task Wait(double seconds = 0.6) =>
        await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private static IEnumerable<Node> Descendants(Node node)
    {
        yield return node;
        foreach (Node child in node.GetChildren())
            foreach (Node nested in Descendants(child)) yield return nested;
    }
    private static bool IsLocalized(string text) => !string.IsNullOrWhiteSpace(text) &&
        !text.Contains("LIBRARIAN_V040_", StringComparison.Ordinal) && !text.Contains("LIBRARIAN-", StringComparison.Ordinal) &&
        !text.Contains("SEA_GLASS.", StringComparison.Ordinal) && !text.Contains("COLORFUL_PHILOSOPHERS.pages", StringComparison.Ordinal);
    private static async Task Capture(string name)
    {
        await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
        Require(image.SavePng(Path.Combine(Output, name + ".png")) == Error.Ok, "saved " + name);
    }

    internal static async Task RunTimelineUi(NMainMenu menu)
    {
        Guard();
        var progress = SaveManager.Instance.Progress;
        Require(Enumerable.Range(1, 7).All(n => progress.Epochs.Any(e => e.Id == LibrarianUnlocks040.Id(n))), "fixture already has seven slots");
        Require(progress.Epochs.Any(e => e.Id == "NEOW_EPOCH" && e.State == EpochState.Revealed), "native timeline introduction completed in fixture");
        var before = progress.Epochs.Select(e => (e.Id, e.State, e.ObtainDate)).ToArray();
        var window = NGame.Instance!.GetWindow();
        var oldSize = window.Size;
        var timeline = menu.SubmenuStack.PushSubmenuType<NTimelineScreen>();
        NEpochInspectScreen? inspect = null;
        try
        {
            await Wait(1.5);
            var slots = Descendants(timeline).OfType<NEpochSlot>().Where(s => s.model is LibrarianEpoch040).ToArray();
            Require(slots.Length == 7, "seven actual native slots mounted");
            inspect = timeline.GetNode<NEpochInspectScreen>("%EpochInspectScreen");
            foreach (var resolution in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                window.Size = resolution;
                await Wait();
                await Capture($"040-timeline-overview-{resolution.X}x{resolution.Y}");
                foreach (var slot in slots.OrderBy(s => s.model.ChapterIndex))
                {
                    Require(IsLocalized(slot.model.Title.GetFormattedText()) && IsLocalized(slot.model.Description) &&
                        IsLocalized(slot.model.UnlockText), "resolved timeline model text " + slot.model.Id);
                    // Inspect existing chapter without invoking reveal/unlock mutations.
                    timeline.OpenInspectScreen(slot, playAnimation: false);
                    await Wait(1.0);
                    Require(inspect.IsVisibleInTree(), "native epoch inspect visible " + slot.model.Id);
                    Require(IsLocalized(inspect.GetNode<MegaLabel>("%StoryLabel").Text), "native story label localized");
                    Require(IsLocalized(inspect.GetNode<MegaLabel>("%ChapterLabel").Text), "native chapter label localized");
                    Require(IsLocalized(inspect.GetNode<MegaRichTextLabel>("%FancyText").Text), "native story body localized");
                    Require(inspect.GetNode<TextureRect>("%Portrait").Texture is not null, "placeholder portrait loaded");
                    await Capture($"040-timeline-chapter-{slot.model.ChapterIndex}-{resolution.X}x{resolution.Y}");
                    inspect.Close();
                    await Wait(0.7);
                }
            }
        }
        finally
        {
            if (inspect is not null && GodotObject.IsInstanceValid(inspect) && inspect.Visible) inspect.Close();
            menu.SubmenuStack.Pop();
            window.Size = oldSize;
        }
        Require(before.SequenceEqual(progress.Epochs.Select(e => (e.Id, e.State, e.ObtainDate))), "inspection changed no unlock states");
    }

    internal static async Task RunEventUi(Player player)
    {
        Guard();
        Require(player.Character is not LibrarianCharacter, "foreign recipient fixture");
        bool hadSession = LibrarianRuntime.TryGet(player, out _);
        var glass = (SeaGlass)ModelDb.Relic<SeaGlass>().ToMutable();
        glass.CharacterId = ModelDb.Character<LibrarianCharacter>().Id;
        string title = glass.Title.GetFormattedText();
        Require(IsLocalized(title), "SeaGlass Librarian title resolves: " + title);
        Require(IsLocalized(glass.DynamicDescription.GetFormattedText()), "SeaGlass original description resolves");
        string optionKey = "COLORFUL_PHILOSOPHERS.pages.INITIAL.options." + ModelDb.CardPool<LibrarianCardPool>().EnergyColorName.ToUpperInvariant();
        Require(IsLocalized(new LocString("events", optionKey + ".title").GetFormattedText()), "orange event title resolves");
        Require(IsLocalized(new LocString("events", optionKey + ".description").GetFormattedText()), "orange event description resolves");
        var cards = LibrarianCrossCharacter040.CreateSeaGlassCards(player, 15);
        Require(cards.Count == 15 && cards.All(c => LibrarianCrossCharacter040.IsSeaGlassEligible(c.Card, player)), "fifteen-card reviewed list");
        Require(cards.GroupBy(c => c.Card.Rarity).All(g => g.Count() == 5), "five per rarity");
        Require(cards.All(c => IsLocalized(c.Card.Title)), "all card titles resolve");
        var prefs = new CardSelectorPrefs(new LocString("relics", "SEA_GLASS.selectionScreenPrompt"), 0, cards.Count);
        Require(prefs.MinSelect == 0 && prefs.MaxSelect == 15 && prefs.RequireManualConfirmation, "native SeaGlass selection 0..15 with confirmation");
        var game = NGame.Instance!;
        var window = game.GetWindow();
        var oldSize = window.Size;
        var relicScreen = game.GetInspectRelicScreen();
        NSimpleCardSelectScreen? selector = null;
        // Discovery is restricted to this explicitly isolated fixture, as in the existing native relic audit.
        SaveManager.Instance.MarkRelicAsSeen(glass);
        try
        {
            foreach (var resolution in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                window.Size = resolution;
                await Wait();
                relicScreen.Open([glass], glass);
                var unlocked = (HashSet<RelicModel>)AccessTools.Field(relicScreen.GetType(), "_allUnlockedRelics").GetValue(relicScreen)!;
                unlocked.Add(glass.CanonicalInstance);
                AccessTools.Method(relicScreen.GetType(), "UpdateRelicDisplay").Invoke(relicScreen, null);
                await Wait();
                Require(relicScreen.GetNode<MegaLabel>("%RelicName").Text == title, "actual relic inspect displays resolved title");
                await Capture($"040-seaglass-title-{resolution.X}x{resolution.Y}");
                relicScreen.Close();
                await Wait(0.3);
                selector = NSimpleCardSelectScreen.Create(cards, prefs);
                NOverlayStack.Instance!.Push(selector);
                await Wait();
                Require(ActiveScreenContext.Instance.IsCurrent(selector), "original simple grid active");
                var mountedPrefs = (CardSelectorPrefs)AccessTools.Field(typeof(NSimpleCardSelectScreen), "_prefs").GetValue(selector)!;
                var mountedCards = (IReadOnlyList<CardModel>)AccessTools.Field(typeof(NCardGridSelectionScreen), "_cards").GetValue(selector)!;
                Require(mountedPrefs.MinSelect == 0 && mountedPrefs.MaxSelect == 15 && mountedCards.Count == 15, "actual grid preserves native range and all fifteen choices");
                Require(mountedCards.All(c => LibrarianCrossCharacter040.IsSeaGlassEligible(c, player)), "actual grid uses reviewed useful and blank cards");
                await Capture($"040-seaglass-native-grid-{resolution.X}x{resolution.Y}");
                NOverlayStack.Instance.Remove(selector);
                selector = null;
                await Wait(0.3);
            }
        }
        finally
        {
            if (selector is not null) NOverlayStack.Instance!.Remove(selector);
            relicScreen.Close();
            window.Size = oldSize;
        }
        Require(LibrarianRuntime.TryGet(player, out _) == hadSession, "preview created no hidden foreign orb session");
    }
}
