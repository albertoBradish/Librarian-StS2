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

/// <summary>Source 5/r6. D24: Rare; gain Fire before reading damage.</summary>
public sealed class FlameBurst() : OrbAdvancedCard(1, CardType.Attack, CardRarity.Rare, TargetType.AllEnemies)
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DynamicVar("Fire", 4m), .. CalculatedDamage((card, _) =>
        {
            // Gain explicitly reaches background Fire; preview projects that gain, without firing its callbacks.
            var session = PreviewSession(card);
            return (session?.Orbs.Value(OrbKind.Fire) ?? 0) + card.DynamicVars["Fire"].BaseValue;
        })];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var session = Session;
        await Gain(choiceContext, session, OrbKind.Fire, Amount("Fire"));
        await HitAll(choiceContext, cardPlay, session.Orbs.Value(OrbKind.Fire));
    }
    protected override void OnUpgrade() => DynamicVars["Fire"].UpgradeValueBy(2m);
}
