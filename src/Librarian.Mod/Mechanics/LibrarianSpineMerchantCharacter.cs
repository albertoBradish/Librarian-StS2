using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace Librarian.Mechanics;

/// <summary>The authored scene's actual C# root; Ritsu typed-source fast path retains this instance.</summary>
public sealed partial class LibrarianSpineMerchantCharacter : NMerchantCharacter
{
    private int _generation;
    private Player? _owner;
    private Node? _room;
    internal int Generation => _generation;
    public override void _EnterTree() { _generation++; base._EnterTree(); }
    public override void _Ready()
    {
        LibrarianSpineIntegration.RequireRoomShape(this, LibrarianSpineAssetPins.MerchantScene);
        base._Ready(); // Native Spine readiness/startup, including Ritsu's own Ready patch.
    }
    internal void Bind(Player player, Node room)
    {
        if (!LibrarianSpineIntegration.IsOwnPlayer(player)) return;
        if (_owner is not null)
        {
            if (!ReferenceEquals(_owner, player) || !ReferenceEquals(_room, room))
                throw new InvalidOperationException("A typed merchant scene is already assigned to another actual player/room.");
            return;
        }
        var sprite = LibrarianSpineIntegration.RequireRoomShape(this, LibrarianSpineAssetPins.MerchantScene);
        _owner = player; _room = room;
        int generation = ++_generation;
        LibrarianSpineRoomBindings.Begin(this, sprite, player, room, LibrarianSpineRoom.Merchant, 0, generation,
            () => _generation == generation && ReferenceEquals(_owner, player) && ReferenceEquals(_room, room));
    }
    public override void _ExitTree()
    {
        _generation++; // Invalidate pending frame/readiness work before native teardown.
        LibrarianSpineRoomBindings.Forget(this);
        _owner = null; _room = null;
        base._ExitTree(); // E separately releases the sprite driver on actual TreeExiting.
    }
}
