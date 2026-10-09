using Librarian.LibrarianCode.Extensions;
using MegaCrit.Sts2.Core.Entities.Powers;

using STS2RitsuLib.Scaffolding.Content;

namespace Librarian.LibrarianCode.Powers;

/// <summary>
/// This is the base class for your mod's powers, which is set up to load the power's images from your mod's resources.
/// When creating a power, right click the Powers folder and create a new file with the Custom Power template.
/// This will generate a class that extends this one.
/// You can also just create the class manually; just make sure to inherit from this class.
/// </summary>
public abstract class LibrarianPower : ModPowerTemplate
{
    protected new void Flash()
    {
        base.Flash();
        Librarian.Mechanics.LibrarianCardVfx050.PowerTriggered(this);
    }

    private string? Revision060Icon => GetType().Name switch
    {
        "ShiftingPagesPower" => "shifting_pages",
        "TidalBlockStatusPower" => "tidal_block_status",
        "RidgeWardPower" => "ridge_ward",
        "UnretreatingTidePower" => "unretreating_tide",
        "EndlessTidePower" => "endless_tide",
        "TidalMarkPower" => "tidal_mark",
        "WaterSpiritPower" => "water_spirit",
        "DeepSeaPendingPower" => "deep_sea_pending",
        "CropRotationPower" => "crop_rotation",
        "RingCurriculumPower" => "ring_curriculum",
        "ThornBurstPower" => "thorn_burst",
        "SeedburialPendingPower" => "seedburial_pending",
        "LifeSymphonyPendingPower" => "life_symphony_pending",
        "AncientCatalogPower" => "ancient_catalog",
        "LifelinePower" => "lifeline",
        "EmberBookmarkPower" => "ember_bookmark",
        "CrowdKindlingPower" => "crowd_kindling",
        "BlazingChapterPower" => "blazing_chapter",
        "FuelTheFirePower" => "fuel_the_fire",
        "ToBeContinuedPower" => "to_be_continued",
        "PracticeMakesPerfectPower" => "practice_makes_perfect",
        "OverlimitFormPower" => "overlimit_form",
        "OverfishingPower" => "overfishing",
        "EternalGrimoirePower" => "eternal_grimoire",
        "SedimentationPower" => "sedimentation",
        "SpacetimeTwistPower" => "spacetime_twist",
        _ => null
    };

    private string IconName => GetType().Name switch
    {
        "TidalBlockStatusPower" => "tidal_block_status",
        "TidalMarkPower" => "tidal_mark",
        "UnretreatingTidePower" => "unretreating_tide",
        "SedimentationPower" => "sedimentation",
        "EternalGrimoirePower" => "eternal_grimoire",
        "OverlimitFormPower" => "overlimit_form",
        "OverfishingPower" => "overfishing",
        "SelectiveOverfishingPower" => "selective_overfishing",
        "LifelinePower" => "lifeline",
        "RidgeWardPower" => "ridge_ward",
        "ShiftingPagesPower" => "shifting_pages",
        "AncientCatalogPower" => "ancient_catalog",
        "RingCurriculumPower" => "ring_curriculum",
        "EmberBookmarkPower" => "ember_bookmark",
        "BlazingChapterPower" => "blazing_chapter",
        "SpacetimeTwistPower" => "spacetime_twist",
        "WaterSpiritPower" => "water_spirit",
        "CrowdKindlingPower" => "crowd_kindling",
        "ThornBurstPower" => "thorn_burst",
        "FuelTheFirePower" => "fuel_the_fire",
        _ => "power"
    };
    // Reuse native presentation only; each custom Power retains its own rules.
    internal string? NativeIconName => GetType().Name switch
    {
        "ShiftingPagesPower" => "afterimage",
        "RidgeWardPower" => "barricade",
        "WaterSpiritPower" => "beacon_of_hope",
        "FuelTheFirePower" => "mayhem",
        "CropRotationPower" => "lightning_rod",
        "LifeSymphonyPendingPower" => "loop",
        "PracticeMakesPerfectPower" => "automation",
        "ToBeContinuedPower" => "tools_of_the_trade",
        "EndlessTidePower" => "plating",
        "OverlimitFormPower" => "spinner",
        "ThornBurstPower" => "toric_toughness",
        _ => null
    };
    public override string CustomIconPath => Revision060Icon is { } icon
        ? $"res://Librarian/images/powers/v0.6.0/{icon}.png"
        : NativeIconName is { } native
        ? $"res://images/atlases/power_atlas.sprites/{native}_power.tres"
        : $"res://Librarian/images/powers/v0.4.1/{IconName}.png";
    public override string CustomBigIconPath => Revision060Icon is { } icon
        ? $"res://Librarian/images/powers/v0.6.0/big/{icon}.png"
        : NativeIconName is { } native
        ? $"res://images/powers/{native}_power.png"
        : $"res://Librarian/images/powers/v0.4.1/big/{IconName}.png";

    /// <summary>
    /// Whether this power is a buff or debuff.
    /// </summary>
    public abstract override PowerType Type { get; }
    
    /// <summary>
    /// How this power stacks if reapplied. Counter is the most common type, where applying the power again just
    /// adds to the amount. Single means the power does not stack, like Barricade. None functions identically to
    /// Single, but you're suggested to use Single as it is more explicit about how it will work.
    /// </summary>
    public abstract override PowerStackType StackType { get; }
}
