using Godot;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Extensions;
using Librarian.LibrarianCode.Relics;
using MegaCrit.Sts2.Core.Entities.Characters;
using MegaCrit.Sts2.Core.Models;
using Librarian.Mechanics;

using STS2RitsuLib.Scaffolding.Characters;

namespace Librarian.LibrarianCode.Character;

public sealed class LibrarianCharacter : ModCharacterTemplate<LibrarianCardPool, LibrarianRelicPool, LibrarianPotionPool>
{
    public const string CharacterId = "LibrarianCharacter";
    public static readonly Color Color = LibrarianVisualTheme.ThemeColor;
    public override Color NameColor => Color;
    public override Color MapDrawingColor => Color;
    public override Color EnergyLabelOutlineColor => new("59351f");
    public override string CustomEnergyCounterPath => LibrarianVisualTheme.EnergyCounterPath;
    public override CharacterGender Gender => CharacterGender.Neutral;
    public override int StartingHp => 72;
    public override int StartingGold => 99;
    public override int MaxEnergy => 3;
    public override float AttackAnimDelay => 0.15f;
    public override float CastAnimDelay => 0.25f;
    public override List<string> GetArchitectAttackVfx() =>
        ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Ironclad>().GetArchitectAttackVfx();
    public override string CustomIconPath => CustomIconTexturePath;

    protected override IEnumerable<CardModel> LocalStartingDeck =>
    [
        ModelDb.Card<LibrarianStrike>(), ModelDb.Card<LibrarianStrike>(),
        ModelDb.Card<LibrarianStrike>(), ModelDb.Card<LibrarianStrike>(),
        ModelDb.Card<LibrarianDefend>(), ModelDb.Card<LibrarianDefend>(),
        ModelDb.Card<LibrarianDefend>(), ModelDb.Card<LibrarianDefend>(),
        ModelDb.Card<Spark>(), ModelDb.Card<Trickle>()
    ];

    protected override IEnumerable<RelicModel> LocalStartingRelics => [ModelDb.Relic<TatteredSpellScroll>()];

    // Existing owned scenes and runtime references are registered through RitsuLib.
    public override string CustomCharacterSelectBgPath => LibrarianCharacterSelect040.ScenePath;
    public override string CustomIconTexturePath => "res://Librarian/images/charui/v0.3.9/character_icon.png";
    public override string CustomCharacterSelectIconPath => "res://Librarian/images/character_select/floating_archive/portrait.png";
    public override string CustomCharacterSelectLockedIconPath => "res://Librarian/images/character_select/floating_archive/portrait_locked.png";
    public override string CustomMapMarkerPath => "res://Librarian/images/charui/map_marker_librarian.png";
    public override string CustomVisualsPath => "res://Librarian/scenes/character/v0.3.7/combat.tscn";
    public override string CustomRestSiteAnimPath => "res://Librarian/scenes/character/v0.4.0/rest.tscn";
    public override string CustomMerchantAnimPath => "res://Librarian/scenes/character/v0.3.7/shop.tscn";
    public override string CustomIconOutlineTexturePath => "res://Librarian/images/charui/v0.3.9/character_icon_outline.png";
    public override string CustomArmPointingTexturePath => "res://Librarian/images/character/v0.3.7/hand_point.png";
    public override string CustomArmRockTexturePath => "res://Librarian/images/character/v0.3.7/hand_rock.png";
    public override string CustomArmPaperTexturePath => "res://Librarian/images/character/v0.3.7/hand_paper.png";
    public override string CustomArmScissorsTexturePath => "res://Librarian/images/character/v0.3.7/hand_scissors.png";
    public override string CustomTrailPath => "res://Librarian/scenes/character/v0.3.7/card_trail.tscn";
    public override string CustomCharacterSelectTransitionPath => "res://Librarian/scenes/character/v0.3.7/transition.tres";
    public float DeathAnimTime => 1.65f;
}
