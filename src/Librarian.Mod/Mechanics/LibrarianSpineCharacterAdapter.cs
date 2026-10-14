using Godot;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;

namespace Librarian.LibrarianCode.Character;

// The accompanying patch makes the existing sealed character declaration partial.
public partial class LibrarianCharacter
{
    protected override CreatureAnimator? SetupCustomCreatureAnimator(MegaSprite controller)
        => LibrarianSpineIntegration.CreateCombatAnimator(controller);
}
