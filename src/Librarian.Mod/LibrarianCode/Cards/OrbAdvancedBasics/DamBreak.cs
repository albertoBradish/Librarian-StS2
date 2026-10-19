using Librarian.Core;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.LibrarianCode.Cards.OrbAdvancedBasics;

/// <summary>V1.3.11 approved 20-card revision. Published model identity is retained.</summary>
public sealed class DamBreak() : OrbAdvancedCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(6m, ValueProp.Move), new DynamicVar("Dexterity", 1m)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<DexterityPower>()];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await LibrarianRuntime.Dispatch(Session, context, Session.Orbs.Lock(OrbKind.Tide, 1, Origin));
        await PowerCmd.Apply<DexterityPower>(context, Owner.Creature, Amount("Dexterity"), Owner.Creature, this);
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block.BaseValue, ValueProp.Move, play);
    }
    protected override void OnUpgrade() { DynamicVars.Block.UpgradeValueBy(3m); DynamicVars["Dexterity"].UpgradeValueBy(1m); }
}
