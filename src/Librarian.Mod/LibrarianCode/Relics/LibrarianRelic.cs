using Librarian.Mechanics;
using MegaCrit.Sts2.Core.HoverTips;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Extensions;

using STS2RitsuLib.Scaffolding.Content;

namespace Librarian.LibrarianCode.Relics;

/// <summary>
/// This is the base class for your mod's relics, which is set up to load the relic's images from your mod's resources.
/// When creating a relic, right click the Relics folder and create a new file with the Custom Relic template.
/// This will generate a class that extends this one.
/// You can also just create the class manually; just make sure to inherit from this class.
///
/// The [Pool] annotation marks this relic as being tied to your specific character. Inheriting from this class means
/// that your relics don't need to invidually say which pool they should be in.
/// </summary>
public abstract class LibrarianRelic : ModRelicTemplate
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => LibrarianHoverTips.ForText(DynamicDescription.GetFormattedText());
    public override string PackedIconPath => "relic.png".RelicImagePath();
    protected override string PackedIconOutlinePath => "relic_outline.png".RelicImagePath();
    protected override string BigIconPath => "relic.png".BigRelicImagePath();
}
