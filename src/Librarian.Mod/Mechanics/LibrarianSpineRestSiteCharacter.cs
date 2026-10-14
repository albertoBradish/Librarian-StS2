using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace Librarian.Mechanics;

/// <summary>Preserves base rest UI/Player and attaches only after real Spine readiness plus one frame.</summary>
public sealed partial class LibrarianSpineRestSiteCharacter : NRestSiteCharacter
{
    private const string NativeReticleScene = "res://scenes/ui/selection_reticle.tscn";
    private int _generation;
    public override void _EnterTree()
    {
        _generation++;
        base._EnterTree();
        LibrarianSpineIntegration.RequireRoomShape(this, LibrarianSpineAssetPins.RestScene);
        EnsureNativeReticle(); // All native UI exists before base Ready can query unique names.
    }
    public override void _Ready()
    {
        var sprite = LibrarianSpineIntegration.RequireRoomShape(this, LibrarianSpineAssetPins.RestScene);
        base._Ready(); // Real Player, act loop, focus, hitbox, selection, thought bubbles remain native.
        var player = Player;
        if (!LibrarianSpineIntegration.IsOwnPlayer(player)) return;
        var room = LibrarianSpineIntegration.Ancestor<NRestSiteRoom>(this)
            ?? throw new InvalidDataException("Owned rest character has no actual native rest-room ancestor.");
        int act = player.RunState.CurrentActIndex;
        if (act is < 0 or > 2) throw new InvalidDataException("Unsupported native rest act.");
        int generation = ++_generation;
        LibrarianSpineRoomBindings.Begin(this, sprite, player, room, LibrarianSpineRoom.Rest, act, generation,
            () => _generation == generation && ReferenceEquals(Player, player)
                && player.RunState.CurrentActIndex == act
                && ReferenceEquals(LibrarianSpineIntegration.Ancestor<NRestSiteRoom>(this), room));
    }
    public override void _ExitTree() { _generation++; base._ExitTree(); }

    private void EnsureNativeReticle()
    {
        LibrarianSpineIntegration.AssertMainThread();
        var controls = GetNode<Control>("ControlRoot");
        var hitbox = GetNode<Control>("%Hitbox");
        NSelectionReticle? reticle = null;
        Visit(this);
        if (reticle is null)
        {
            // Normally fixed Ritsu CompleteBareRoot already supplied this exact native scene.
            // Direct instantiation still works: borrow the native game's cached PackedScene once.
            if (!ResourceLoader.Exists(NativeReticleScene, "PackedScene"))
                throw new InvalidDataException("Native game's selection reticle scene is absent.");
            var scene = PreloadManager.Cache.GetScene(NativeReticleScene);
            try
            {
                var created = scene.Instantiate();
                if (created is not NSelectionReticle native)
                {
                    created.Free(); // Only the failed newly minted Node, never the borrowed scene.
                    throw new InvalidDataException("Native selection scene did not instantiate NSelectionReticle.");
                }
                reticle = native;
                reticle.Name = "SelectionReticle";
                controls.AddChild(reticle);
            }
            finally { GC.KeepAlive(scene); } // Shared native cache resource is never disposed.
        }
        else if (!ReferenceEquals(reticle.GetParent(), controls))
        {
            // Ritsu generates its reticle at the root. Move only that actual instance;
            // native packed descendants keep their own scene owners and unique-name scope.
            reticle.GetParent()?.RemoveChild(reticle);
            controls.AddChild(reticle);
        }
        reticle.Owner = this;
        reticle.UniqueNameInOwner = true;
        // This owned template has a fixed rectangular Hitbox. Match its local rectangle
        // under the same ControlRoot; retain the native reticle's internal UI/style.
        reticle.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        reticle.Position = hitbox.Position;
        reticle.Size = hitbox.Size;
        reticle.Rotation = hitbox.Rotation;
        reticle.Scale = hitbox.Scale;
        reticle.PivotOffset = hitbox.PivotOffset;
        if (!ReferenceEquals(GetNode<NSelectionReticle>("%SelectionReticle"), reticle))
            throw new InvalidDataException("Native rest reticle unique-name ownership was not restored.");
        GC.KeepAlive(reticle);

        void Visit(Node parent)
        {
            int childCount = parent.GetChildCount();
            for (int i = 0; i < childCount; i++)
            {
                Node child = parent.GetChild(i);
                if (child is NSelectionReticle existing)
                {
                    if (reticle is not null) throw new InvalidDataException("Duplicate native rest selection reticles.");
                    reticle = existing;
                }
                else if (child.Name == "SelectionReticle")
                    throw new InvalidDataException("A rest selection reticle placeholder has the wrong native type.");
                Visit(child);
            }
        }
    }
}
