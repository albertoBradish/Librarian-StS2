using Librarian.Core;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.LibrarianCode.Cards.NativeBatch;

/// <summary>v0.3.0 approved revision, catalog 7. Stable model ID retained.</summary>
public sealed class NeedleFlurry() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(0, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(3m, ValueProp.Move)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        int hits = 1 + Session.Orbs.Select(OrbScope.All, ActivationFilter.Active).Count;
        await DamageCmd.Attack(Amount("Damage")).FromCard(this, play).Targeting(play.Target!)
            .WithHitCount(hits).WithHitFx("vfx/vfx_attack_slash").Execute(context);
    }
    protected override void OnUpgrade()
    {
        DynamicVars["Damage"].UpgradeValueBy(1m);
    }
}
