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

/// <summary>v0.3.0 approved revision, catalog 42. Stable model ID retained.</summary>
public sealed class TidalGravity() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(0, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new EnergyVar(2), new DynamicVar("LockTurns", 2m)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await PlayerCmd.GainEnergy(Amount("Energy"), Owner);
        await Lock(context, OrbKind.Tide, Amount("LockTurns"));
    }
    protected override void OnUpgrade()
    {
        DynamicVars["LockTurns"].UpgradeValueBy(-1m);
        RemoveKeyword(CardKeyword.Exhaust);
    }
}
