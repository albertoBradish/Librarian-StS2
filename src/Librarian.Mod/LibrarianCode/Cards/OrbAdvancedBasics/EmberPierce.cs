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
public sealed class EmberPierce() : OrbAdvancedCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    public override bool GainsBlock => true;
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(6m, ValueProp.Move)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<BufferPower>()];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var session = Session;
        bool eligible = session.Orbs.OwnerTurn > 1 && !session.TideSettledPreviousOwnTurn;
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block.BaseValue, ValueProp.Move, play);
        if (eligible) await PowerCmd.Apply<BufferPower>(context, Owner.Creature, 1, Owner.Creature, this);
    }
    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3m);
}
