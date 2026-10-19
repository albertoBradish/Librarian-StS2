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
public sealed class Rootbind() : OrbAdvancedCard(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(8m, ValueProp.Move), new DynamicVar("StrengthLoss", 3m)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<StrengthPower>()];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await Hit(context, play, DynamicVars.Damage.BaseValue);
        int loss = Session.Orbs.Positions.Count(Session.Orbs.IsLocked) * Amount("StrengthLoss");
        if (loss > 0 && play.Target is { IsAlive: true })
            await PowerCmd.Apply<Librarian.LibrarianCode.Powers.Implemented.RootbindPower>(context, play.Target, loss, Owner.Creature, this);
    }
    protected override void OnUpgrade() { DynamicVars.Damage.UpgradeValueBy(3m); DynamicVars["StrengthLoss"].UpgradeValueBy(1m); }
}
