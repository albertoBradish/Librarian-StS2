using Godot;
using HarmonyLib;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace Librarian.Mechanics;

/// <summary>
/// Opt-in isolated native shop regression. Purchases a real displayed card, removes
/// that exact card through the native two-step selection UI, closes/reopens the
/// inventory and map, then leaves and creates another shop. No TestMode/selector
/// replacement is used. Invocation belongs to the coordinator's guarded audit.
/// </summary>
internal static class DevelopmentMerchant051Audit
{
    private static int _checks;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("051 merchant: " + label);
        _checks++;
        MainFile.Logger.Info("V051_MERCHANT_CHECK_PASS " + label);
    }

    private static async Task Wait(double seconds = 0.25) => await NGame.Instance!.ToSignal(
        NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private static async Task Until(Func<bool> condition, string label)
    {
        for (int i = 0; i < 100; i++)
        {
            if (condition()) return;
            await Wait(0.1);
        }
        throw new TimeoutException("051 merchant: " + label);
    }

    private static async Task Capture(string output, string name)
    {
        await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
        Check(image.SavePng(Path.Combine(output, name + ".png")) == Error.Ok, "screenshot " + name);
    }

    // Await the actual slot's native click handler so failures reach the audit.
    // Emitting its mouse event would start an unobservable fire-and-forget Task.
    private static Task SelectSlot(NMerchantSlot slot) => (Task)(
        AccessTools.Method(typeof(NMerchantSlot), "OnSelected").Invoke(slot, null)
        ?? throw new InvalidOperationException("Native merchant selection returned no task."));

    private static async Task CloseInventory(NMerchantRoom room)
    {
        var back = room.Inventory.GetNode<NBackButton>("%BackButton");
        Check(back.IsEnabled, "native inventory back button enabled");
        back.EmitSignal(NClickableControl.SignalName.Released, back);
        await Wait(0.9);
        Check(!room.Inventory.IsOpen && room.MerchantButton.IsEnabled && room.ProceedButton.IsEnabled,
            "native close restores merchant and proceed buttons");
    }

    private static async Task OpenInventory(NMerchantRoom room)
    {
        Check(room.MerchantButton.IsEnabled, "native merchant button enabled");
        room.MerchantButton.EmitSignal(NMerchantButton.SignalName.MerchantOpened, room.MerchantButton);
        await Wait(0.9);
        Check(room.Inventory.IsOpen && ActiveScreenContext.Instance.IsCurrent(room.Inventory), "native inventory opened and focused");
    }

    private static async Task CheckCharacter(NMerchantRoom room)
    {
        var visual = room.PlayerVisuals.Single(v => LibrarianMerchantFactoryCompatibility.FindMotion(v) is not null);
        var motion = LibrarianMerchantFactoryCompatibility.FindMotion(visual)!;
        var body = motion.GetParent().GetNode<Sprite2D>(motion.VisualPath + "/Body");
        Check(body.Texture.ResourcePath == "res://Librarian/images/character/v0.4.0/body.png",
            "native merchant identity survives the current body texture replacement");
        Check(motion.Rig is not null && motion.HasSeparateHands, "current layered rig and both hands initialized");
        var mask = motion.GetParent().GetNode<Sprite2D>(motion.VisualPath + "/Mask");
        Check(mask.IsVisibleInTree() && room.GetViewport().GetVisibleRect().HasPoint(mask.GetGlobalTransformWithCanvas().Origin),
            "actual layered merchant character visible on screen");
        var animator = motion.GetNode<AnimationPlayer>("AnimationPlayer");
        foreach (var (requested, selected, state) in new[]
        {
            ("relaxed_loop", "Idle", "Idle"), ("Attack", "Attack", "Attack"),
            ("Hit", "Hit", "Hit"), ("die", "Dead", "Dead"),
            ("Revive", "Revive", "Revive"), ("unrecognized_native_animation", "Idle", "Idle")
        })
        {
            visual.PlayAnimation(requested, loop: requested == "relaxed_loop");
            await Wait(0.15);
            Check(animator.CurrentAnimation == selected && motion.Rig!.PerformanceState == state,
                "native merchant animation routes " + requested + " to " + selected);
        }
        await Wait(0.3);
    }

    private static async Task CheckNativeCharacter(NMerchantRoom room)
    {
        var nativeScene = GD.Load<PackedScene>(ModelDb.Character<Ironclad>().MerchantAnimPath);
        Check(nativeScene is not null, "native Ironclad merchant scene loads");
        var native = nativeScene!.Instantiate<NMerchantCharacter>();
        native.Position = new Vector2(-10000, -10000);
        room.AddChild(native);
        try
        {
            await Wait(0.4);
            Check(LibrarianMerchantFactoryCompatibility.FindMotion(native) is null, "native merchant is outside Librarian component routing");
            native.PlayAnimation("relaxed_loop", loop: true);
            await Wait(0.15);
            var current = new MegaSprite(native.GetChild(0)).GetAnimationState().GetCurrent(0);
            Check(current is not null && current.GetAnimationEnd() > 0, "native Spine merchant animation remains functional");
        }
        finally { native.QueueFree(); }
        await Wait();
    }

    internal static async Task Run(Player player, Func<Task> returnToCombat, string? output = null)
    {
        _checks = 0;
        Check(System.Environment.GetEnvironmentVariable("LIBRARIAN_RUNTIME_AUDIT") == "1" &&
            OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase), "explicit isolated runtime profile");
        Check(DisplayServer.GetName() != "headless", "native rendering enabled");
        Check(player.Character is LibrarianCharacter && player.RunState.Players.Count == 1, "single-player Librarian fixture");
        Check(CardSelectCmd.Selector is null, "native card selection without test selector");
        output ??= System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT") ?? @"D:\Slay The Spire_Mod Dev\outputs\revision-v1.0.0-stable\audit-history\revision-v0.5.1\screenshots";
        Directory.CreateDirectory(output);
        var originalDeck = player.Deck.Cards.ToArray();
        int originalGold = player.Gold;
        int originalRemovals = player.ExtraFields.CardShopRemovalsUsed;
        CardModel? bought = null;
        NDeckCardSelectScreen? selector = null;
        SaveManager.Instance.Progress.MarkFtueAsComplete("merchant_ftue");
        try
        {
            await RunManager.Instance.EnterRoomDebug(RoomType.Shop);
            await Wait(1.5);
            var room = NMerchantRoom.Instance ?? throw new InvalidOperationException("Native merchant room not mounted.");
            Check(room.IsInsideTree(), "real shop mounted by RunManager");
            await CheckCharacter(room);
            await CheckNativeCharacter(room);
            await Capture(output, "051-merchant-room");
            await OpenInventory(room);
            var inventory = room.Inventory.Inventory!;
            Check(inventory is not null && room.Inventory.GetAllSlots().Count(s => s.Entry.IsStocked) > 0, "native inventory populated");
            var slot = room.Inventory.GetAllSlots().OfType<NMerchantCard>().First(s => s.Entry.IsStocked);
            var entry = (MerchantCardEntry)slot.Entry;
            bought = entry.CreationResult!.Card;
            int cardCost = entry.Cost;

            await PlayerCmd.SetGold(0, player);
            await SelectSlot(slot);
            Check(entry.IsStocked && !player.Deck.Cards.Contains(bought) && player.Gold == 0,
                "native insufficient-gold purchase rejected without mutation");
            await PlayerCmd.SetGold(10000, player);
            int completed = 0;
            void Purchased(PurchaseStatus status, MerchantEntry _) { if (status == PurchaseStatus.Success) completed++; }
            entry.PurchaseCompleted += Purchased;
            try { await SelectSlot(slot); }
            finally { entry.PurchaseCompleted -= Purchased; }
            await Wait(0.7);
            Check(completed == 1 && player.Deck.Cards.Contains(bought) && player.Deck.Cards.Count == originalDeck.Length + 1,
                "native card purchase adds exact displayed card once");
            Check(player.Gold == 10000 - cardCost && !entry.IsStocked && !slot.Visible,
                "native purchase charges displayed price and clears slot");
            int afterPurchaseGold = player.Gold;
            // Native UI removes sold-out cards from hit testing. Calling their
            // private purchase handlers by reflection would bypass visibility and
            // emit an unsupported vanilla dialogue status, not simulate a click.
            Check(!entry.IsStocked && !slot.IsVisibleInTree() && player.Gold == afterPurchaseGold,
                "native sold-out slot hidden from interaction without additional charge");
            await Capture(output, "051-merchant-purchased");

            var removalSlot = room.Inventory.GetAllSlots().OfType<NMerchantCardRemoval>().Single();
            var removal = (MerchantCardRemovalEntry)removalSlot.Entry;
            int removalCost = removal.Cost;
            var removeTask = SelectSlot(removalSlot);
            await Until(() => NGame.Instance!.FindChildren("*", "", true, false)
                .OfType<NDeckCardSelectScreen>().Any(s => s.IsVisibleInTree()), "native removal selector opened");
            selector = NGame.Instance!.FindChildren("*", "", true, false).OfType<NDeckCardSelectScreen>().Single(s => s.IsVisibleInTree());
            Check(ActiveScreenContext.Instance.IsCurrent(selector), "native removal selection has focus");
            var cancel = selector.GetNode<NBackButton>("%Close");
            Check(cancel.IsEnabled, "native removal permits cancellation");
            cancel.EmitSignal(NClickableControl.SignalName.Released, cancel);
            await Until(() => removeTask.IsCompleted, "native removal cancellation completion");
            await removeTask;
            selector = null;
            await Wait(0.4);
            Check(!removal.Used && player.Gold == afterPurchaseGold && player.Deck.Cards.Contains(bought) &&
                player.ExtraFields.CardShopRemovalsUsed == originalRemovals, "native removal cancellation preserves card, gold and service");
            removeTask = SelectSlot(removalSlot);
            await Until(() => NGame.Instance!.FindChildren("*", "", true, false)
                .OfType<NDeckCardSelectScreen>().Any(s => s.IsVisibleInTree()), "native removal selector reopened after cancellation");
            selector = NGame.Instance!.FindChildren("*", "", true, false).OfType<NDeckCardSelectScreen>().Single(s => s.IsVisibleInTree());
            var grid = selector.GetNode<NCardGrid>("%CardGrid");
            var holder = grid.GetCardHolder(bought);
            Check(holder is not null && holder.IsVisibleInTree(), "purchased card selectable in native removal grid");
            holder!.EmitSignal(NCardHolder.SignalName.Pressed, holder);
            await Wait();
            var confirm = selector.GetNode<NConfirmButton>("%PreviewConfirm");
            Check(confirm.IsEnabled && selector.GetNode<Control>("%PreviewContainer").Visible,
                "native removal requires preview confirmation");
            await Capture(output, "051-merchant-removal-confirm");
            confirm.EmitSignal(NClickableControl.SignalName.Released, confirm);
            await Until(() => removeTask.IsCompleted, "native removal completion");
            await removeTask;
            selector = null;
            await Wait(0.7);
            Check(player.Deck.Cards.SequenceEqual(originalDeck), "native removal removes exact purchased card and preserves original deck");
            Check(player.Gold == afterPurchaseGold - removalCost && player.ExtraFields.CardShopRemovalsUsed == originalRemovals + 1,
                "native removal charges price once and increments history once");
            Check(removal.Used && !removal.IsStocked && removalSlot.FocusMode == Control.FocusModeEnum.None,
                "native removal service becomes unavailable");
            await Capture(output, "051-merchant-removed");

            await CloseInventory(room);
            await OpenInventory(room);
            Check(!entry.IsStocked && removal.Used, "closing and reopening preserves purchased and used states");
            await CloseInventory(room);
            room.ProceedButton.EmitSignal(NClickableControl.SignalName.Released, room.ProceedButton);
            await Wait(0.8);
            var map = NMapScreen.Instance ?? throw new InvalidOperationException("Native map screen was not mounted.");
            Check(map.IsOpen, "native proceed opens map");
            map.Close();
            await Wait(0.8);
            await OpenInventory(room);
            await CloseInventory(room);

            var previousMotion = LibrarianMerchantFactoryCompatibility.FindMotion(room.PlayerVisuals.Single())!;
            await RunManager.Instance.EnterRoomDebug(RoomType.RestSite);
            await Wait(1.0);
            Check(!GodotObject.IsInstanceValid(room) && !GodotObject.IsInstanceValid(previousMotion), "leaving shop releases previous room and motion");
            await RunManager.Instance.EnterRoomDebug(RoomType.Shop);
            await Wait(1.0);
            var next = NMerchantRoom.Instance!;
            Check(!ReferenceEquals(next, room) && next.IsInsideTree(), "another shop mounts a fresh room");
            await CheckCharacter(next);
            await OpenInventory(next);
            var nextRemoval = next.Inventory.Inventory!.CardRemovalEntry!;
            Check(!nextRemoval.Used && nextRemoval.Cost == removalCost + MerchantCardRemovalEntry.PriceIncrease,
                "new shop refreshes service and carries native removal price increase");
            await CloseInventory(next);
        }
        finally
        {
            // Only the dedicated disposable validation run can reach this fixture.
            if (selector is not null && GodotObject.IsInstanceValid(selector) && selector.IsInsideTree())
            {
                var close = selector.GetNode<NBackButton>("%Close");
                close.EmitSignal(NClickableControl.SignalName.Released, close);
                await Wait();
            }
            if (bought is not null && player.Deck.Cards.Contains(bought)) await CardPileCmd.RemoveFromDeck(bought);
            player.ExtraFields.CardShopRemovalsUsed = originalRemovals;
            await PlayerCmd.SetGold(originalGold, player);
            await returnToCombat();
        }
        Check(player.Deck.Cards.SequenceEqual(originalDeck) && player.Gold == originalGold &&
            player.ExtraFields.CardShopRemovalsUsed == originalRemovals, "fixture restores original deck, gold and removal history");
        MainFile.Logger.Info($"V051_MERCHANT_AUDIT_PASS checks={_checks} nativeCardPurchase=True nativeRemovalUI=True removalCancel=True inventoryReopen=True map=True roomReentry=True nativeSpineControl=True");
    }
}
