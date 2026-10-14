using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Extensions;

using STS2RitsuLib.Scaffolding.Content;

namespace Librarian.LibrarianCode.Potions;

public abstract class LibrarianPotion : ModPotionTemplate
{
    private string Artwork => GetType().Name switch
    {
        nameof(KindlingPotion) => "fire_potion",
        nameof(ClarityPotion) => "clear_potion",
        nameof(FluidForbiddenFruit) => "fluid_fruit",
        _ => throw new InvalidOperationException("Missing potion artwork mapping: " + GetType().Name)
    };
    public override string? CustomImagePath => "res://Librarian/images/potions/" + Artwork + ".png";
    public string? CustomLargeImagePath => "res://Librarian/images/potions/big/" + Artwork + ".png";
    public override string? CustomOutlinePath => "res://Librarian/images/potions/outline/" + Artwork + ".png";
}
