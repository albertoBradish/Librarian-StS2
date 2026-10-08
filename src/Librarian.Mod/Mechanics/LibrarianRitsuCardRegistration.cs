using HarmonyLib;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib;

namespace Librarian.Mechanics;

internal static class LibrarianRitsuCardRegistration
{
    // Includes nine retired models for old saves; LibrarianCardPool still excludes them from the active catalog.
    internal static readonly IReadOnlyDictionary<Type, string> LegacyEntries = new Dictionary<Type, string>
    {
        [typeof(global::Librarian.LibrarianCode.Cards.OrbUtility.Afforestation)] = "LIBRARIAN-AFFORESTATION",
        [typeof(global::Librarian.LibrarianCode.Cards.PowerCards.AncientCatalog)] = "LIBRARIAN-ANCIENT_CATALOG",
        [typeof(global::Librarian.LibrarianCode.Cards.AncientSpark)] = "LIBRARIAN-ANCIENT_SPARK",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbUtility.ArchiveBulwark)] = "LIBRARIAN-ARCHIVE_BULWARK",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbBasics.AshenBlow)] = "LIBRARIAN-ASHEN_BLOW",
        [typeof(global::Librarian.LibrarianCode.Cards.PowerCards.BlazingChapter)] = "LIBRARIAN-BLAZING_CHAPTER",
        [typeof(global::Librarian.LibrarianCode.Cards.Stateful.Bookworm)] = "LIBRARIAN-BOOKWORM",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbUtility.BookBurning)] = "LIBRARIAN-BOOK_BURNING",
        [typeof(global::Librarian.LibrarianCode.Cards.BuildCanal)] = "LIBRARIAN-BUILD_CANAL",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbBasics.BurningPages)] = "LIBRARIAN-BURNING_PAGES",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.BurnRoots)] = "LIBRARIAN-BURN_ROOTS",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbBasics.BurnTheRiver)] = "LIBRARIAN-BURN_THE_RIVER",
        [typeof(global::Librarian.LibrarianCode.Cards.Stateful.Calibrate)] = "LIBRARIAN-CALIBRATE",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbBasics.ChannelFlow)] = "LIBRARIAN-CHANNEL_FLOW",
        [typeof(global::Librarian.LibrarianCode.Cards.CirculationNotes)] = "LIBRARIAN-CIRCULATION_NOTES",
        [typeof(global::Librarian.LibrarianCode.Cards.NativeBatch.CombatNotes)] = "LIBRARIAN-COMBAT_NOTES",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbUtility.Cooldown)] = "LIBRARIAN-COOLDOWN",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.CropRotation)] = "LIBRARIAN-CROP_ROTATION",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.DamBreak)] = "LIBRARIAN-DAM_BREAK",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbUtility.DeepSeaBarrier)] = "LIBRARIAN-DEEP_SEA_BARRIER",
        [typeof(global::Librarian.LibrarianCode.Cards.Stateful.DrawBranch)] = "LIBRARIAN-DRAW_BRANCH",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbBasics.DryBranchSearch)] = "LIBRARIAN-DRY_BRANCH_SEARCH",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.EarthCollapse)] = "LIBRARIAN-EARTH_COLLAPSE",
        [typeof(global::Librarian.LibrarianCode.Cards.PowerCards.EmberBookmark)] = "LIBRARIAN-EMBER_BOOKMARK",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.EmberPierce)] = "LIBRARIAN-EMBER_PIERCE",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbUtility.EmberReckoning)] = "LIBRARIAN-EMBER_RECKONING",
        [typeof(global::Librarian.LibrarianCode.Cards.EndlessTide)] = "LIBRARIAN-ENDLESS_TIDE",
        [typeof(global::Librarian.LibrarianCode.Cards.PowerCards.EternalGrimoire)] = "LIBRARIAN-ETERNAL_GRIMOIRE",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbUtility.Evaporation)] = "LIBRARIAN-EVAPORATION",
        [typeof(global::Librarian.LibrarianCode.Cards.Stateful.FireInscription)] = "LIBRARIAN-FIRE_INSCRIPTION",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbBasics.FlameStrike)] = "LIBRARIAN-FLAME_STRIKE",
        [typeof(global::Librarian.LibrarianCode.Cards.NativeBatch.FlyingPages)] = "LIBRARIAN-FLYING_PAGES",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbUtility.FuelTheFire)] = "LIBRARIAN-FUEL_THE_FIRE",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.GapNeedle)] = "LIBRARIAN-GAP_NEEDLE",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.Ignite)] = "LIBRARIAN-IGNITE",
        [typeof(global::Librarian.LibrarianCode.Cards.Stateful.ImmortalSpark)] = "LIBRARIAN-IMMORTAL_SPARK",
        [typeof(global::Librarian.LibrarianCode.Cards.LibrarianDefend)] = "LIBRARIAN-LIBRARIAN_DEFEND",
        [typeof(global::Librarian.LibrarianCode.Cards.LibrarianStrike)] = "LIBRARIAN-LIBRARIAN_STRIKE",
        [typeof(global::Librarian.LibrarianCode.Cards.PowerCards.Lifeline)] = "LIBRARIAN-LIFELINE",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbUtility.LifeSymphony)] = "LIBRARIAN-LIFE_SYMPHONY",
        [typeof(global::Librarian.LibrarianCode.Cards.MultiplayerPlaceholderA)] = "LIBRARIAN-MULTIPLAYER_PLACEHOLDER_A",
        [typeof(global::Librarian.LibrarianCode.Cards.MultiplayerPlaceholderB)] = "LIBRARIAN-MULTIPLAYER_PLACEHOLDER_B",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbUtility.MultipleEruption)] = "LIBRARIAN-MULTIPLE_ERUPTION",
        [typeof(global::Librarian.LibrarianCode.Cards.NativeBatch.NeedleFlurry)] = "LIBRARIAN-NEEDLE_FLURRY",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbBasics.Nourish)] = "LIBRARIAN-NOURISH",
        [typeof(global::Librarian.LibrarianCode.Cards.Stateful.NourishingLife)] = "LIBRARIAN-NOURISHING_LIFE",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.OpeningTide)] = "LIBRARIAN-OPENING_TIDE",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbUtility.OutOfContext)] = "LIBRARIAN-OUT_OF_CONTEXT",
        [typeof(global::Librarian.LibrarianCode.Cards.PowerCards.Overfishing)] = "LIBRARIAN-OVERFISHING",
        [typeof(global::Librarian.LibrarianCode.Cards.PowerCards.OverlimitForm)] = "LIBRARIAN-OVERLIMIT_FORM",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbUtility.OverloadBurn)] = "LIBRARIAN-OVERLOAD_BURN",
        [typeof(global::Librarian.LibrarianCode.Cards.PracticeMakesPerfect)] = "LIBRARIAN-PRACTICE_MAKES_PERFECT",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbBasics.QuietEmbers)] = "LIBRARIAN-QUIET_EMBERS",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbBasics.ReadBackward)] = "LIBRARIAN-READ_BACKWARD",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbUtility.ReadWidely)] = "LIBRARIAN-READ_WIDELY",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbBasics.Reignite)] = "LIBRARIAN-REIGNITE",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbUtility.Rekindle)] = "LIBRARIAN-REKINDLE",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbBasics.Renewal)] = "LIBRARIAN-RENEWAL",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbUtility.ResidualWarmth)] = "LIBRARIAN-RESIDUAL_WARMTH",
        [typeof(global::Librarian.LibrarianCode.Cards.ReRead)] = "LIBRARIAN-RE_READ",
        [typeof(global::Librarian.LibrarianCode.Cards.PowerCards.RidgeWard)] = "LIBRARIAN-RIDGE_WARD",
        [typeof(global::Librarian.LibrarianCode.Cards.PowerCards.RingCurriculum)] = "LIBRARIAN-RING_CURRICULUM",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.Rootbind)] = "LIBRARIAN-ROOTBIND",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbBasics.ScatteredFlames)] = "LIBRARIAN-SCATTERED_FLAMES",
        [typeof(global::Librarian.LibrarianCode.Cards.PowerCards.Sedimentation)] = "LIBRARIAN-SEDIMENTATION",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbUtility.SeedburialStrike)] = "LIBRARIAN-SEEDBURIAL_STRIKE",
        [typeof(global::Librarian.LibrarianCode.Cards.SharedShelter)] = "LIBRARIAN-SHARED_SHELTER",
        [typeof(global::Librarian.LibrarianCode.Cards.PowerCards.ShiftingPages)] = "LIBRARIAN-SHIFTING_PAGES",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbUtility.SongOfIceAndFire)] = "LIBRARIAN-SONG_OF_ICE_AND_FIRE",
        [typeof(global::Librarian.LibrarianCode.Cards.PowerCards.SpacetimeTwist)] = "LIBRARIAN-SPACETIME_TWIST",
        [typeof(global::Librarian.LibrarianCode.Cards.Spark)] = "LIBRARIAN-SPARK",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbBasics.Springwater)] = "LIBRARIAN-SPRINGWATER",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbUtility.SproutingBulwark)] = "LIBRARIAN-SPROUTING_BULWARK",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbBasics.SproutingSeed)] = "LIBRARIAN-SPROUTING_SEED",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.SteamBlast)] = "LIBRARIAN-STEAM_BLAST",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.ThornBurst)] = "LIBRARIAN-THORN_BURST",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbUtility.ThreefoldUnity)] = "LIBRARIAN-THREEFOLD_UNITY",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.TidalErosion)] = "LIBRARIAN-TIDAL_EROSION",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.TidalGravity)] = "LIBRARIAN-TIDAL_GRAVITY",
        [typeof(global::Librarian.LibrarianCode.Cards.PowerCards.TidalMark)] = "LIBRARIAN-TIDAL_MARK",
        [typeof(global::Librarian.LibrarianCode.Cards.ToBeContinued)] = "LIBRARIAN-TO_BE_CONTINUED",
        [typeof(global::Librarian.LibrarianCode.Cards.Stateful.Transcribe)] = "LIBRARIAN-TRANSCRIBE",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbUtility.TreeRingBurst)] = "LIBRARIAN-TREE_RING_BURST",
        [typeof(global::Librarian.LibrarianCode.Cards.Trickle)] = "LIBRARIAN-TRICKLE",
        [typeof(global::Librarian.LibrarianCode.Cards.PowerCards.UnretreatingTide)] = "LIBRARIAN-UNRETREATING_TIDE",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbBasics.VineShield)] = "LIBRARIAN-VINE_SHIELD",
        [typeof(global::Librarian.LibrarianCode.Cards.PowerCards.WaterSpirit)] = "LIBRARIAN-WATER_SPIRIT",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.WaveCurtain)] = "LIBRARIAN-WAVE_CURTAIN",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbBasics.WaveStrike)] = "LIBRARIAN-WAVE_STRIKE",
        [typeof(global::Librarian.LibrarianCode.Cards.NativeBatch.WearyIncantation)] = "LIBRARIAN-WEARY_INCANTATION",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbUtility.ZeroSearch)] = "LIBRARIAN-ZERO_SEARCH",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbBasics.ColdFlame)] = "LIBRARIAN-COLD_FLAME",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbUtility.DroughtEdict)] = "LIBRARIAN-DROUGHT_EDICT",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.FlameBurst)] = "LIBRARIAN-FLAME_BURST",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbUtility.ForestWall)] = "LIBRARIAN-FOREST_WALL",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbUtility.LonelyScroll)] = "LIBRARIAN-LONELY_SCROLL",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbUtility.SealAway)] = "LIBRARIAN-SEAL_AWAY",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.SeaBurial)] = "LIBRARIAN-SEA_BURIAL",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.SeverCurrent)] = "LIBRARIAN-SEVER_CURRENT",
        [typeof(global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.TidalStrike)] = "LIBRARIAN-TIDAL_STRIKE",
    };

    internal static void Initialize()
    {
        RitsuLibFramework.CreateContentPack(MainFile.ModId)
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbUtility.Afforestation>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.PowerCards.AncientCatalog>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.AncientSpark>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbUtility.ArchiveBulwark>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbBasics.AshenBlow>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.PowerCards.BlazingChapter>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.Stateful.Bookworm>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbUtility.BookBurning>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.BuildCanal>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbBasics.BurningPages>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.BurnRoots>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbBasics.BurnTheRiver>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.Stateful.Calibrate>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbBasics.ChannelFlow>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.CirculationNotes>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.NativeBatch.CombatNotes>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbUtility.Cooldown>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.CropRotation>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.DamBreak>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbUtility.DeepSeaBarrier>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.Stateful.DrawBranch>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbBasics.DryBranchSearch>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.EarthCollapse>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.PowerCards.EmberBookmark>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.EmberPierce>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbUtility.EmberReckoning>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.EndlessTide>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.PowerCards.EternalGrimoire>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbUtility.Evaporation>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.Stateful.FireInscription>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbBasics.FlameStrike>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.NativeBatch.FlyingPages>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbUtility.FuelTheFire>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.GapNeedle>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.Ignite>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.Stateful.ImmortalSpark>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.LibrarianDefend>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.LibrarianStrike>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.PowerCards.Lifeline>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbUtility.LifeSymphony>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.MultiplayerPlaceholderA>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.MultiplayerPlaceholderB>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbUtility.MultipleEruption>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.NativeBatch.NeedleFlurry>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbBasics.Nourish>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.Stateful.NourishingLife>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.OpeningTide>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbUtility.OutOfContext>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.PowerCards.Overfishing>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.PowerCards.OverlimitForm>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbUtility.OverloadBurn>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.PracticeMakesPerfect>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbBasics.QuietEmbers>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbBasics.ReadBackward>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbUtility.ReadWidely>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbBasics.Reignite>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbUtility.Rekindle>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbBasics.Renewal>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbUtility.ResidualWarmth>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.ReRead>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.PowerCards.RidgeWard>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.PowerCards.RingCurriculum>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.Rootbind>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbBasics.ScatteredFlames>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.PowerCards.Sedimentation>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbUtility.SeedburialStrike>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.SharedShelter>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.PowerCards.ShiftingPages>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbUtility.SongOfIceAndFire>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.PowerCards.SpacetimeTwist>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.Spark>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbBasics.Springwater>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbUtility.SproutingBulwark>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbBasics.SproutingSeed>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.SteamBlast>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.ThornBurst>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbUtility.ThreefoldUnity>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.TidalErosion>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.TidalGravity>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.PowerCards.TidalMark>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.ToBeContinued>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.Stateful.Transcribe>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbUtility.TreeRingBurst>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.Trickle>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.PowerCards.UnretreatingTide>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbBasics.VineShield>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.PowerCards.WaterSpirit>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.WaveCurtain>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbBasics.WaveStrike>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.NativeBatch.WearyIncantation>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbUtility.ZeroSearch>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbBasics.ColdFlame>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbUtility.DroughtEdict>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.FlameBurst>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbUtility.ForestWall>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbUtility.LonelyScroll>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbUtility.SealAway>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.SeaBurial>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.SeverCurrent>()
            .Card<LibrarianCardPool, global::Librarian.LibrarianCode.Cards.OrbAdvancedBasics.TidalStrike>()
            .Apply();
        MainFile.Logger.Info("RITSU_CARD_REGISTRATION registeredModels=100 expectedActive=91 compatibilityModels=9 legacyEntries=True");
    }
}

// Preserve published entries after both libraries' identity hooks, before ModelDb initializes.
// Scope remains explicit: only the one hundred owned Ritsu card models above.
[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.GetEntry))]
internal static class LibrarianRitsuLegacyEntries
{
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    [HarmonyAfter("BaseLib", Const.FrameworkContentRegistryHarmonyId)]
    private static void Postfix(Type type, ref string __result)
    {
        if (typeof(LibrarianRitsuCard).IsAssignableFrom(type)
            && LibrarianRitsuCardRegistration.LegacyEntries.TryGetValue(type, out var entry))
            __result = entry;
    }
}
