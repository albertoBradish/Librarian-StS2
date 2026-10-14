using Godot;
using Librarian.Mechanics;

using STS2RitsuLib.Scaffolding.Content;

namespace Librarian.LibrarianCode.Character;

public class LibrarianRelicPool : TypeListRelicPoolModel
{
    public override string EnergyColorName => Id.Category + "∴" + Id.Entry;
    public override Color LabOutlineColor => LibrarianCharacter.Color;

    public override string BigEnergyIconPath => LibrarianVisualTheme.BigEnergyIconPath;
    public override string TextEnergyIconPath => LibrarianVisualTheme.TextEnergyIconPath;
}
