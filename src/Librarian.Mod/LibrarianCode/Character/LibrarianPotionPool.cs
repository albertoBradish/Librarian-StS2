using BaseLib.Abstracts;
using Librarian.LibrarianCode.Extensions;
using Godot;

namespace Librarian.LibrarianCode.Character;

public class LibrarianPotionPool : CustomPotionPoolModel
{
    public override Color LabOutlineColor => LibrarianCharacter.Color;
    

    public override string BigEnergyIconPath => Librarian.Mechanics.LibrarianVisualTheme.BigEnergyIconPath;
    public override string TextEnergyIconPath => Librarian.Mechanics.LibrarianVisualTheme.TextEnergyIconPath;
}
