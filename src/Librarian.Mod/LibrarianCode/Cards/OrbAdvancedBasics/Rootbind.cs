using Librarian.Core;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.LibrarianCode.Cards.OrbAdvancedBasics;

/// <summary>v0.5.2: single target, Growth activation gates temporary Strength loss.</summary>
public sealed class Rootbind() : OrbAdvancedCard(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(8m, ValueProp.Move), new DynamicVar("StrengthLoss", 6m)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<StrengthPower>()];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target);
        bool activated = Session.Orbs.IsActivated(OrbKind.Growth);
        await Hit(choiceContext, cardPlay, DynamicVars.Damage.BaseValue);
        if (activated && cardPlay.Target.IsAlive)
            await PowerCmd.Apply<Librarian.LibrarianCode.Powers.Implemented.RootbindPower>(
                choiceContext, cardPlay.Target, DynamicVars["StrengthLoss"].BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(3m);
}
