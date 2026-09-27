using BaseLib.Abstracts;
using BaseLib.Utils.NodeFactories;
using Godot;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Extensions;
using Librarian.LibrarianCode.Relics;
using MegaCrit.Sts2.Core.Entities.Characters;
using MegaCrit.Sts2.Core.Models;
using Librarian.Mechanics;

namespace Librarian.LibrarianCode.Character;

public sealed class LibrarianCharacter : PlaceholderCharacterModel
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

    public override IEnumerable<CardModel> StartingDeck =>
    [
        ModelDb.Card<LibrarianStrike>(), ModelDb.Card<LibrarianStrike>(),
        ModelDb.Card<LibrarianStrike>(), ModelDb.Card<LibrarianStrike>(),
        ModelDb.Card<LibrarianDefend>(), ModelDb.Card<LibrarianDefend>(),
        ModelDb.Card<LibrarianDefend>(), ModelDb.Card<LibrarianDefend>(),
        ModelDb.Card<Spark>(), ModelDb.Card<Trickle>()
    ];

    public override IReadOnlyList<RelicModel> StartingRelics => [ModelDb.Relic<TatteredSpellScroll>()];
    public override CardPoolModel CardPool => ModelDb.CardPool<LibrarianCardPool>();
    public override RelicPoolModel RelicPool => ModelDb.RelicPool<LibrarianRelicPool>();
    public override PotionPoolModel PotionPool => ModelDb.PotionPool<LibrarianPotionPool>();

    // BaseLib resolves the other placeholder scenes from the game's resources at runtime.
    // No extracted game scene or texture is redistributed by this project.
    public override Control CustomIcon
    {
        get
        {
            var icon = NodeFactory<Control>.CreateFromResource(CustomIconTexturePath);
            icon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            return icon;
        }
    }
    public override string CustomCharacterSelectBg => LibrarianCharacterSelect040.ScenePath;
    public override string CustomIconTexturePath => "res://Librarian/images/charui/v0.3.9/character_icon.png";
    public override string CustomCharacterSelectIconPath => "res://Librarian/images/character_select/floating_archive/portrait.png";
    public override string CustomCharacterSelectLockedIconPath => "res://Librarian/images/character_select/floating_archive/portrait_locked.png";
    public override string CustomMapMarkerPath => "res://Librarian/images/charui/map_marker_librarian.png";
    public override string CustomVisualPath => "res://Librarian/scenes/character/v0.3.7/combat.tscn";
    public override string CustomRestSiteAnimPath => "res://Librarian/scenes/character/v0.4.0/rest.tscn";
    public override string CustomMerchantAnimPath => "res://Librarian/scenes/character/v0.3.7/shop.tscn";
    public override string CustomIconOutlineTexturePath => "res://Librarian/images/charui/v0.3.9/character_icon_outline.png";
    public override string CustomArmPointingTexturePath => "res://Librarian/images/character/v0.3.7/hand_point.png";
    public override string CustomArmRockTexturePath => "res://Librarian/images/character/v0.3.7/hand_rock.png";
    public override string CustomArmPaperTexturePath => "res://Librarian/images/character/v0.3.7/hand_paper.png";
    public override string CustomArmScissorsTexturePath => "res://Librarian/images/character/v0.3.7/hand_scissors.png";
    public override string CustomTrailPath => "res://Librarian/scenes/character/v0.3.7/card_trail.tscn";
    public override string CustomCharacterSelectTransitionPath => "res://Librarian/scenes/character/v0.3.7/transition.tres";
    public override float DeathAnimTime => 1.65f;
}
