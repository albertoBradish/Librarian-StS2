using Godot;
using Librarian.Core;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace Librarian.Mechanics;

/// <summary>Read-only presentation; custom textures and values, independent of native orb queues.</summary>
internal static class LibrarianOrbPanel
{
    public static void Refresh(LibrarianSession session)
    {
        RefreshOne(session);
        // Every character owns an independent display; never gate remote allies on LocalContext.
        if (session.Player.Creature.CombatState is not { } combat) return;
        foreach (var player in combat.Players)
            if (player != session.Player && player.Character is Librarian.LibrarianCode.Character.LibrarianCharacter)
                RefreshOne(LibrarianRuntime.Get(player));
    }

    private static void RefreshOne(LibrarianSession session)
    {
        if (!session.HasCharacterOrbs) return;
        var room = NCombatRoom.Instance;
        if (room is null || !GodotObject.IsInstanceValid(room)) return;
        var creature = room.GetCreatureNode(session.Player.Creature);
        if (creature is null || !GodotObject.IsInstanceValid(creature)) return;
        string name = "LibrarianOrbs" + session.Player.NetId;
        var display = creature.GetNodeOrNull<LibrarianOrbDisplay>(name);
        if (display is null)
        {
            display = new LibrarianOrbDisplay { Name = name, MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 0 };
            creature.AddChild(display);
            display.Initialize(session);
        }
        display.RefreshState();
    }

    public static LibrarianOrbDisplay? GetDisplay(LibrarianSession session)
    {
        var room = NCombatRoom.Instance;
        if (room is null || !GodotObject.IsInstanceValid(room) || !room.IsInsideTree()) return null;
        var creature = room.GetCreatureNode(session.Player.Creature);
        return creature?.GetNodeOrNull<LibrarianOrbDisplay>("LibrarianOrbs" + session.Player.NetId);
    }

    public static void Pulse(LibrarianSession session, OrbKind kind)
    {
        var creature = NCombatRoom.Instance?.GetCreatureNode(session.Player.Creature);
        creature?.GetNodeOrNull<LibrarianOrbDisplay>("LibrarianOrbs" + session.Player.NetId)?.Pulse(kind);
    }

    public static void ShowTideChange(LibrarianSession session, long amount, bool expired = false)
    {
        Refresh(session);
        var creature = NCombatRoom.Instance?.GetCreatureNode(session.Player.Creature);
        creature?.GetNodeOrNull<LibrarianOrbDisplay>("LibrarianOrbs" + session.Player.NetId)?.ShowTideChange(amount, expired);
    }
}

