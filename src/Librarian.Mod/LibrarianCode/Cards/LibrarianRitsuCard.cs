using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using STS2RitsuLib.Scaffolding.Content;

namespace Librarian.LibrarianCode.Cards;

/// <summary>Ritsu card template sharing the existing Librarian assets and mechanics.</summary>
public abstract class LibrarianRitsuCard(int cost, CardType type, CardRarity rarity, TargetType target)
    : ModCardTemplate(cost, type, rarity, target), ILibrarianCard
{
    // Pool identity also applies to retired models excluded from rewards and the catalog.
    public override CardPoolModel Pool => ModelDb.CardPool<LibrarianCardPool>();

    // Preserve the previous BaseLib dynamic-block capability used by native descriptions and previews.
    public override bool GainsBlock => DynamicVars.Any(pair => pair.Value is BlockVar or CalculatedBlockVar);

    public override string PortraitPath => ExistingOrFallback(
        "res://Librarian/images/card_portraits/" + Id.Entry["LIBRARIAN-".Length..].ToLowerInvariant() + ".png");
    public override string CustomPortraitPath => PortraitPath;
    public override string BetaPortraitPath => PortraitPath;
    public override string CustomBetaPortraitPath => PortraitPath;
    private static string ExistingOrFallback(string path) => ResourceLoader.Exists(path)
        ? path : "res://Librarian/images/card_portraits/card.png";
}
