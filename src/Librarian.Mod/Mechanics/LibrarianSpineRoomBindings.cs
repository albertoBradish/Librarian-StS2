using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Events.Custom;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace Librarian.Mechanics;

/// <summary>One explicit native room owner per own root; never guesses a global Player.</summary>
internal static class LibrarianSpineRoomBindings
{
    private sealed record MerchantRegistration(Node Sprite, Player Player, Node Room,
        LibrarianSpinePoseDriver Driver, int Generation);
    private static readonly ConditionalWeakTable<NMerchantCharacter, MerchantRegistration> Merchants = new();
    private static readonly ConditionalWeakTable<Player, MerchantRegistration> MerchantOwners = new();
    private static readonly AccessTools.FieldRef<MerchantEntry, Player> EntryOwner =
        AccessTools.FieldRefAccess<MerchantEntry, Player>("_player");
    private static readonly AccessTools.FieldRef<NMerchantRoom, List<Player>> MerchantPlayers =
        AccessTools.FieldRefAccess<NMerchantRoom, List<Player>>("_players");
    private static readonly AccessTools.FieldRef<NFakeMerchant, List<Player>> FakePlayers =
        AccessTools.FieldRefAccess<NFakeMerchant, List<Player>>("_players");
    private static readonly AccessTools.FieldRef<NFakeMerchant, Control> FakeContainer =
        AccessTools.FieldRefAccess<NFakeMerchant, Control>("_characterContainer");
    private static long _nativeOccurrence;

    internal static void Forget(LibrarianSpineMerchantCharacter root)
    {
        if (Merchants.TryGetValue(root, out var registration)
            && MerchantOwners.TryGetValue(registration.Player, out var current) && ReferenceEquals(registration, current))
            MerchantOwners.Remove(registration.Player);
        Merchants.Remove(root);
    }

    internal static void Begin(Node root, Node sprite, Player player, Node room, LibrarianSpineRoom kind,
        int act, int generation, Func<bool> current)
        => TaskHelper.RunSafely(AttachWhenReady(root, sprite, player, room, kind, act, generation, current));

    private static async Task AttachWhenReady(Node root, Node sprite, Player player, Node room,
        LibrarianSpineRoom kind, int act, int generation, Func<bool> current)
    {
        try
        {
            LibrarianSpineIntegration.AssertMainThread();
            if (!Alive()) return;
            var tree = root.GetTree();
            var megaSprite = new MegaSprite(sprite);
            while (Alive() && !megaSprite.IsAnimationStateReady())
                await root.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (!Alive()) return;
            // Base native Ready may be another waiter. Give its one-time setup a full frame.
            await root.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (!Alive() || !megaSprite.IsAnimationStateReady()) return;
            LibrarianSpineIntegration.ValidateSprite(sprite);
            if (!LibrarianSpineIntegration.IsOwnPlayer(player)) return;
            LibrarianSpineRuntime.AttachRoom(sprite, player, kind, act);
            var driver = LibrarianSpineRuntime.Find(sprite)
                ?? throw new InvalidOperationException("Own room registration did not create a live driver.");
            if (root is LibrarianSpineMerchantCharacter merchant)
            {
                if (Merchants.TryGetValue(merchant, out var previous))
                {
                    if (!ReferenceEquals(previous.Sprite, sprite) || !ReferenceEquals(previous.Player, player)
                        || !ReferenceEquals(previous.Room, room) || !ReferenceEquals(previous.Driver, driver))
                        throw new InvalidOperationException("One merchant root cannot change owner behind its driver.");
                    return;
                }
                var registration = new MerchantRegistration(sprite, player, room, driver, generation);
                Merchants.Add(merchant, registration);
                MerchantOwners.Remove(player);
                MerchantOwners.Add(player, registration);
            }
            GC.KeepAlive(megaSprite); GC.KeepAlive(sprite);
        }
        catch (Exception error) { LibrarianSpineIntegration.Report(error); }
        bool Alive() => GodotObject.IsInstanceValid(root) && GodotObject.IsInstanceValid(sprite)
            && GodotObject.IsInstanceValid(room) && root.IsInsideTree() && sprite.IsInsideTree() && room.IsInsideTree()
            && current();
    }

    private static void BindMerchantRoom(NMerchantRoom room)
    {
        var players = MerchantPlayers(room);
        if (!players.Any(LibrarianSpineIntegration.IsOwnPlayer)) return;
        var visuals = room.PlayerVisuals;
        if (players.Count != visuals.Count) throw new InvalidDataException("Actual merchant player/visual counts disagree.");
        for (int i = 0; i < players.Count; i++)
        {
            if (!LibrarianSpineIntegration.IsOwnPlayer(players[i])) continue;
            if (visuals[i] is not LibrarianSpineMerchantCharacter own
                || !ReferenceEquals(LibrarianSpineIntegration.Ancestor<NMerchantRoom>(own), room))
                throw new InvalidDataException("Own player did not instantiate the typed merchant scene in this room.");
            own.Bind(players[i], room);
        }
    }

    private static void BindFakeMerchant(NFakeMerchant screen)
    {
        var players = FakePlayers(screen);
        if (!players.Any(LibrarianSpineIntegration.IsOwnPlayer)) return;
        var container = FakeContainer(screen);
        // Fixed Ritsu0.6.4 rewrites ALL booths to NMerchantCharacter when any merchant override applies.
        var visuals = container.GetChildren().OfType<NMerchantCharacter>().ToArray();
        if (players.Count != visuals.Length)
            throw new InvalidDataException("Fixed Ritsu FakeMerchant branch did not create one merchant root per actual player.");
        for (int i = 0; i < players.Count; i++)
        {
            if (!LibrarianSpineIntegration.IsOwnPlayer(players[i])) continue;
            if (visuals[visuals.Length - 1 - i] is not LibrarianSpineMerchantCharacter own
                || !ReferenceEquals(LibrarianSpineIntegration.Ancestor<NFakeMerchant>(own), screen))
                throw new InvalidDataException("FakeMerchant owned booth did not match its actual reversed player index.");
            own.Bind(players[i], screen);
        }
    }

    private static bool ConsumeMerchantRequest(NMerchantCharacter instance, string animation, bool loop)
    {
        if (!Merchants.TryGetValue(instance, out var registration)) return false;
        // Registration, exact own C# root and exact bank were proven before installation.
        if (instance is not LibrarianSpineMerchantCharacter own || own.Generation != registration.Generation
            || !instance.IsInsideTree() || !GodotObject.IsInstanceValid(registration.Room)
            || !registration.Room.IsInsideTree()) return false;
        if (registration.Driver.IsDisposed) return true; // Presentation fault must not create a second native owner.
        LibrarianSpineIntegration.PresentationSafely(() =>
        {
            if (!ReferenceEquals(LibrarianSpineRuntime.Find(registration.Sprite), registration.Driver))
                throw new InvalidOperationException("Owned merchant driver identity changed.");
            string trigger = animation switch {
                "idle" or "idle_loop" => "Idle", "relaxed" or "relaxed_loop" => "Relaxed",
                "attack" => "Attack", "cast" => "Cast", "power" => "PowerUp", "hurt" or "hit" => "Hit",
                "die" or "dead" or "death" => "Dead", "revive" => "Revive",
                _ => throw new InvalidDataException("Unreviewed own merchant animation request: " + animation) };
            // Native loop argument never overrides the authored 60-clip Loop contract.
            long executionId = 0;
            bool merge = (trigger is "Attack" or "Cast" or "PowerUp")
                && LibrarianSpineCardExecution.TryHandled(registration.Player.Creature, out executionId);
            registration.Driver.NativeTrigger(trigger, merge, merge ? executionId : Interlocked.Increment(ref _nativeOccurrence));
        });
        return true;
    }

    [HarmonyPatch(typeof(NMerchantRoom), "AfterRoomIsLoaded")]
    private static class AfterActualMerchantLayout
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(NMerchantRoom __instance)
            => LibrarianSpineIntegration.PresentationSafely(() => BindMerchantRoom(__instance));
    }
    [HarmonyPatch(typeof(NFakeMerchant), "AfterRoomIsLoaded")]
    private static class AfterActualFakeLayout
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(NFakeMerchant __instance)
            // Postfix also executes when Ritsu Prefix replaced the original. Never gate on __runOriginal.
            => LibrarianSpineIntegration.PresentationSafely(() => BindFakeMerchant(__instance));
    }
    [HarmonyPatch(typeof(NMerchantCharacter), nameof(NMerchantCharacter.PlayAnimation), [typeof(string), typeof(bool)])]
    private static class OwnedMerchantNativePlay
    {
        [HarmonyPriority(801)] // Fixed Ritsu playback Prefix is 800 and can otherwise route before our driver.
        private static bool Prefix(NMerchantCharacter __instance, string anim, bool loop)
            => !ConsumeMerchantRequest(__instance, anim, loop);
    }
    [HarmonyPatch(typeof(MerchantEntry), nameof(MerchantEntry.InvokePurchaseCompleted))]
    private static class ActualMerchantPurchaseCompleted
    {
        // Both fixed normal and removal wrappers invoke this only after their success branch.
        // Do not intercept the asynchronous purchase task, input, gold, cancellation or selection.
        private static void Postfix(MerchantEntry __instance, MerchantEntry entry)
            => LibrarianSpineIntegration.PresentationSafely(() =>
            {
                if (!ReferenceEquals(__instance, entry)) return;
                Player player = EntryOwner(__instance);
                if (!MerchantOwners.TryGetValue(player, out var registration)
                    || registration.Driver.IsDisposed || !GodotObject.IsInstanceValid(registration.Room)
                    || !registration.Room.IsInsideTree() || !GodotObject.IsInstanceValid(registration.Sprite)
                    || registration.Sprite is not CanvasItem sprite || !sprite.IsVisibleInTree()
                    || !ReferenceEquals(LibrarianSpineRuntime.Find(registration.Sprite), registration.Driver)) return;
                registration.Driver.MerchantPurchaseConfirmed(Interlocked.Increment(ref _nativeOccurrence));
            });
    }
}
