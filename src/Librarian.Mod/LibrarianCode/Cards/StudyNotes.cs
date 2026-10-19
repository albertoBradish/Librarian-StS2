using Librarian.Core;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.LibrarianCode.Cards;

/// <summary>V1.3.11 approved 20-card revision. Published model identity is retained.</summary>
public sealed class StudyNotes() : LibrarianCard(0, CardType.Skill, CardRarity.Token, TargetType.Self)
{
    public override string PortraitPath => "res://Librarian/images/card_portraits/study_notes.png";
    public override string CustomPortraitPath => PortraitPath;
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Thorns", 3m), new DynamicVar("Strength", 2m), new DynamicVar("Dexterity", 1m)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<ThornsPower>(), HoverTipFactory.FromPower<StrengthPower>(), HoverTipFactory.FromPower<DexterityPower>()];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var s = LibrarianRuntime.Get(Owner);
        if (s.Orbs.IsLocked(OrbKind.Growth)) await PowerCmd.Apply<ThornsPower>(context, Owner.Creature, DynamicVars["Thorns"].BaseValue, Owner.Creature, this);
        if (s.Orbs.IsLocked(OrbKind.Fire)) await PowerCmd.Apply<StrengthPower>(context, Owner.Creature, DynamicVars["Strength"].BaseValue, Owner.Creature, this);
        if (s.Orbs.IsLocked(OrbKind.Tide)) await PowerCmd.Apply<DexterityPower>(context, Owner.Creature, DynamicVars["Dexterity"].BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade() { foreach (string key in new[] { "Thorns", "Strength", "Dexterity" }) DynamicVars[key].UpgradeValueBy(1m); }
}
