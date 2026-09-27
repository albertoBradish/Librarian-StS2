using BaseLib.Abstracts;
using Godot;
using Librarian.Mechanics;

namespace Librarian.LibrarianCode.Character;

public class LibrarianRelicPool : CustomRelicPoolModel
{
    public override Color LabOutlineColor => LibrarianCharacter.Color;

    public override string BigEnergyIconPath => LibrarianVisualTheme.BigEnergyIconPath;
    public override string TextEnergyIconPath => LibrarianVisualTheme.TextEnergyIconPath;
}
