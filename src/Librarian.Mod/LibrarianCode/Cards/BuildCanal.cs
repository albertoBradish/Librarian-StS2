using Librarian.Core;
using Librarian.LibrarianCode.Cards.OrbUtility;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace Librarian.LibrarianCode.Cards;

/// <summary>v0.4.0 replacement in new pools; SeverCurrent retains its historical model ID.</summary>
public sealed class BuildCanal() : OrbUtilityCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Tide", 4m), new EnergyVar(2)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await Gain(context, Session, OrbKind.Tide, Amount("Tide"));
        await PlayerCmd.GainEnergy(Amount("Energy"), Owner);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}
