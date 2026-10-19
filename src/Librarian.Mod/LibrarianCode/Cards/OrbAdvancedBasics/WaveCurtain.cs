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
public sealed class WaveCurtain() : OrbAdvancedCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        Session.Waves.Add(Session.Orbs.Value(OrbKind.Tide));
        LibrarianRuntime.VerifyBlock(Session);
        await Task.CompletedTask;
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}
