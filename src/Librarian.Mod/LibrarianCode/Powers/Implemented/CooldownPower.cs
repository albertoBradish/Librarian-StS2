using MegaCrit.Sts2.Core.Entities.Powers;

namespace Librarian.LibrarianCode.Powers.Implemented;

/// <summary>Each stack preserves one nonempty end-turn Waves resolution.</summary>
public sealed class CooldownPower : ImplementedLibrarianPower
{
    public override PowerStackType StackType => PowerStackType.Counter;
    public override string CustomIconPath => "res://Librarian/images/powers/v0.6.1/cooldown.png";
    public override string CustomBigIconPath => "res://Librarian/images/powers/v0.6.1/big/cooldown.png";
}
