using System.IO;
using Godot;
using Librarian.LibrarianCode;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace Librarian.Mechanics;

/// <summary>Real room composition on the isolated validation run. Never selects an option or purchases an item.</summary>
internal static class DevelopmentRevision040RoomsVisualAudit
{
    private static void Require(bool condition, string text)
    {
        if (!condition) throw new InvalidOperationException("040 native rooms: " + text);
        MainFile.Logger.Info("VISUAL_040_ROOM_CHECK_PASS " + text);
    }
    private static IEnumerable<Node> Descendants(Node node)
    {
        yield return node;
        foreach (Node child in node.GetChildren())
            foreach (var nested in Descendants(child)) yield return nested;
    }
    private static async Task Wait(double seconds) => await NGame.Instance!.ToSignal(
        NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    internal static async Task Run(Player player)
    {
        Require(OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase), "isolated profile");
        Require(DisplayServer.GetName() != "headless", "native rendering");
        var game = NGame.Instance!;
        var window = game.GetWindow();
        var oldSize = window.Size;
        string output = System.Environment.GetEnvironmentVariable("LIBRARIAN_VISUAL_OUTPUT")
            ?? @"D:\Slay The Spire_Mod Dev\.research\revision-v040-screenshots";
        Directory.CreateDirectory(output);
        // Suppress native first-use explanations only in this disposable visual fixture.
        SaveManager.Instance.Progress.MarkFtueAsComplete("rest_site_ftue");
        SaveManager.Instance.Progress.MarkFtueAsComplete("merchant_ftue");
        var deck = player.Deck.Cards.Select(c => c.Id).ToArray();
        var gold = player.Gold;
        try
        {
            foreach (var roomType in new[] { RoomType.RestSite, RoomType.Shop })
            {
                await RunManager.Instance.EnterRoomDebug(roomType);
                await Wait(1.5);
                Node room = roomType == RoomType.RestSite ? NRestSiteRoom.Instance! : NMerchantRoom.Instance!;
                Require(room is not null && room.IsInsideTree(), "actual " + roomType + " scene mounted");
                var motion = Descendants(room!).OfType<LibrarianCharacterMotion>().Single();
                Require(motion.HasSeparateHands, roomType + " body and independent hands");
                Require(Math.Abs(motion.DisplayHeight - (roomType == RoomType.RestSite ? 480f : 310f)) < 0.1f,
                    roomType + " intended display height " + motion.DisplayHeight);
                var shadow = Descendants(room!).OfType<LibrarianGroundShadow>().Single();
                var body = motion.GetParent().GetNode<Sprite2D>(motion.VisualPath + "/Body");
                Require(shadow.IsVisibleInTree() && body.IsVisibleInTree(), roomType + " live shadow and body visible");
                Require(!ReferenceEquals(shadow.GetParent(), body.GetParent()), roomType + " shadow independent of floating body");
                foreach (var resolution in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
                {
                    window.Size = resolution;
                    await Wait(0.8);
                    Require(body.GetGlobalTransformWithCanvas().Origin.Y > 0, roomType + " body inside screen vertically");
                    await game.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var image = game.GetViewport().GetTexture().GetImage();
                    string name = $"040-native-room-{roomType}-{resolution.X}x{resolution.Y}.png";
                    Require(image.SavePng(Path.Combine(output, name)) == Error.Ok, "saved " + name);
                }
            }
            Require(gold == player.Gold && deck.SequenceEqual(player.Deck.Cards.Select(c => c.Id)), "no purchase or card modification");
            MainFile.Logger.Info("VISUAL_040_NATIVE_ROOMS_AUDIT_PASS rooms=2 resolutions=2");
        }
        finally { window.Size = oldSize; }
    }
}
