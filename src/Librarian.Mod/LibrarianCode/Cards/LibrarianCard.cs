using BaseLib.Abstracts;
using BaseLib.Extensions;
using BaseLib.Utils;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Extensions;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace Librarian.LibrarianCode.Cards;

/// <summary>
/// This is the base class for your mod's cards, which is set up to load the card's images from your mod's resources.
/// When creating a card, right click the Cards folder and create a new file with the Custom Card template.
/// This will generate a class that extends this one.
/// You can also just create the class manually; just make sure to inherit from this class.
/// </summary>
[Pool(typeof(LibrarianCardPool))]
public abstract class LibrarianCard(int cost, CardType type, CardRarity rarity, TargetType target) :
    CustomCardModel(cost, type, rarity, target)
{
    // BaseLib uses this texture directly in NCard, including the native inspect screen.
    // Match the native 250x190 atlas footprint instead of bypassing it with source artwork.
    public override string CustomPortraitPath => ExistingOrPlaceholder($"{Id.Entry.RemovePrefix().ToLowerInvariant()}.png".CardImagePath(), "card.png".CardImagePath());
    
    //Smaller variants of card images for efficiency:
    //Smaller variant of fullart: 250x350
    //Smaller variant of normalart: 250x190
    
    //Uses card_portraits/card_name.png as image path. These should be smaller images.
    public override string PortraitPath => ExistingOrPlaceholder($"{Id.Entry.RemovePrefix().ToLowerInvariant()}.png".CardImagePath(), "card.png".CardImagePath());
    public override string BetaPortraitPath => PortraitPath;
    private static string ExistingOrPlaceholder(string preferred, string fallback) => Godot.ResourceLoader.Exists(preferred) ? preferred : fallback;
}
