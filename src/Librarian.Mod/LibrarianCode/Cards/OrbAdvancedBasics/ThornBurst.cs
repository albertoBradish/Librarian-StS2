using Librarian.Core;
using Librarian.Mechanics;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.LibrarianCode.Cards.OrbAdvancedBasics;

/// <summary>V0.4.1: lock Growth and schedule fixed six-point Growth at future owner turns.</summary>
public sealed class ThornBurst() : OrbAdvancedCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DynamicVar("LockTurns", 1m), new DynamicVar("Growth", 6m), new PowerVar<ThornBurstPower>(3m)];
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await LibrarianRuntime.Dispatch(Session, choiceContext,
            Session.Orbs.Lock(OrbKind.Growth, Amount("LockTurns"), Origin));
        await PowerCmd.Apply<ThornBurstPower>(choiceContext, Owner.Creature,
            DynamicVars["ThornBurstPower"].BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade()
    {
        DynamicVars["ThornBurstPower"].UpgradeValueBy(1m);
    }
}
