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

/// <summary>Each cast owns an independent chain, represented by a native visible power.</summary>
public sealed class CropRotation() : OrbAdvancedCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Turns", 2m)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var power = await PowerCmd.Apply<Librarian.LibrarianCode.Powers.Implemented.CropRotationPower>(
            context, Owner.Creature, Amount("Turns"), Owner.Creature, this);
        power?.Schedule(Session, Amount("Turns"));
    }
    protected override void OnUpgrade() => DynamicVars["Turns"].UpgradeValueBy(1m);
}
