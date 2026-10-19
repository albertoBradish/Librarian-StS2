using Librarian.LibrarianCode.Extensions;
using Godot;

using STS2RitsuLib.Scaffolding.Content;

namespace Librarian.LibrarianCode.Character;

public class LibrarianPotionPool : TypeListPotionPoolModel
{
    public override string EnergyColorName => Id.Category + "∴" + Id.Entry;
    public override Color LabOutlineColor => LibrarianCharacter.Color;
    

    public override string BigEnergyIconPath => Librarian.Mechanics.LibrarianVisualTheme.BigEnergyIconPath;
    public override string TextEnergyIconPath => Librarian.Mechanics.LibrarianVisualTheme.TextEnergyIconPath;
}
