using Godot;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Unlocks;

using STS2RitsuLib.Scaffolding.Content;

namespace Librarian.LibrarianCode.Character;

public class LibrarianCardPool : TypeListCardPoolModel
{
    // Retain registered legacy types for old save deserialization, but remove retired
    // models from current card libraries, rewards, random generation and this catalog.
    public override IEnumerable<CardModel> AllCards => base.AllCards.Where(card =>
        card.Id.Entry is not "LIBRARIAN-FLAME_BURST" and not "LIBRARIAN-SEAL_AWAY"
            and not "LIBRARIAN-SEVER_CURRENT" and not "LIBRARIAN-COLD_FLAME"
            and not "LIBRARIAN-FOREST_WALL"
            and not "LIBRARIAN-LONELY_SCROLL" and not "LIBRARIAN-SEA_BURIAL"
            and not "LIBRARIAN-DROUGHT_EDICT" and not "LIBRARIAN-TIDAL_STRIKE");
    protected override IEnumerable<CardModel> FilterThroughEpochs(UnlockState unlockState, IEnumerable<CardModel> cards)
        => LibrarianUnlocks040.FilterCards(unlockState, cards);
    public override string Title => LibrarianCharacter.CharacterId; //This is not a display name.
    
    public override string BigEnergyIconPath => LibrarianVisualTheme.BigEnergyIconPath;
    public override string TextEnergyIconPath => LibrarianVisualTheme.TextEnergyIconPath;
    // The dedicated theme material preserves native frame shading and sets the
    // requested orange explicitly; the native HSV shader only rotates source colors.
    public Color ShaderColor => LibrarianCharacter.Color;
    public override string EnergyColorName => Id.Category + "∴" + Id.Entry;
    //Color of small card icons
    public override Color DeckEntryCardColor => LibrarianCharacter.Color;
    
    public override bool IsColorless => false;
}
