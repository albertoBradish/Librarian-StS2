using BaseLib.Abstracts;
using BaseLib.Extensions;
using BaseLib.Utils;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Extensions;

namespace Librarian.LibrarianCode.Potions;

[Pool(typeof(LibrarianPotionPool))]
public abstract class LibrarianPotion : CustomPotionModel
{
    private string Artwork => GetType().Name switch
    {
        nameof(KindlingPotion) => "fire_potion",
        nameof(ClarityPotion) => "clear_potion",
        nameof(FluidForbiddenFruit) => "fluid_fruit",
        _ => throw new InvalidOperationException("Missing potion artwork mapping: " + GetType().Name)
    };
    public override string? CustomPackedImagePath => "res://Librarian/images/potions/" + Artwork + ".png";
    public override string? CustomLargeImagePath => "res://Librarian/images/potions/big/" + Artwork + ".png";
    public override string? CustomPackedOutlinePath => "res://Librarian/images/potions/outline/" + Artwork + ".png";
}
