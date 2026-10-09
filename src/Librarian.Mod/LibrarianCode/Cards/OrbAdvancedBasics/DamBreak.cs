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

/// <summary>Source 35/r36; author-supplied D08 upgrade reduces cost 2 to 1.</summary>
public sealed class DamBreak() : OrbAdvancedCard(2, CardType.Attack, CardRarity.Uncommon, TargetType.AllEnemies)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => CalculatedDamage((card, _) => PreviewForeground(card, OrbKind.Tide));
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        int actual = await LoseAll(choiceContext, Session, OrbKind.Tide);
        await HitAll(choiceContext, cardPlay, actual);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}
