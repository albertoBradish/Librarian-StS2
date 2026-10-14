using MegaCrit.Sts2.Core.Entities.Cards;

namespace Librarian.LibrarianCode.Cards;

/// <summary>Shared rule helpers retain this name while all cards use the Ritsu template.</summary>
public abstract class LibrarianCard(int cost, CardType type, CardRarity rarity, TargetType target)
    : LibrarianRitsuCard(cost, type, rarity, target)
{
}
