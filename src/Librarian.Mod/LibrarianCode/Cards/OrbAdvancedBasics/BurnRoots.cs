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

/// <summary>v0.3.0 approved revision, catalog 32. Stable model ID retained.</summary>
[BaseLib.Utils.Attributes.CustomID("LIBRARIAN-BURN_ROOTS")]
public sealed class BurnRoots() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(9m, ValueProp.Move), new DynamicVar("Fire", 6m)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await Hit(context, play, Amount("Damage"));
        await Gain(context, Session, OrbKind.Fire, Amount("Fire"));
        await Lock(context, OrbKind.Growth, 1);
    }
    protected override void OnUpgrade()
    {
        DynamicVars["Damage"].UpgradeValueBy(3m);
        DynamicVars["Fire"].UpgradeValueBy(2m);
    }
}
