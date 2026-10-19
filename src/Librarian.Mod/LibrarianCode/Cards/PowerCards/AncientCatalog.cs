using Librarian.Core;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.LibrarianCode.Cards.PowerCards;

/// <summary>V1.3.11 approved 20-card revision. Published model identity is retained.</summary>
public sealed class AncientCatalog() : ImplementedPowerCard(1, CardRarity.Rare)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Block", 4m), new DynamicVar("Vigor", 5m)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<VigorPower>()];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var p = await Apply<Librarian.LibrarianCode.Powers.Implemented.AncientCatalogPower>(context, DynamicVars["Block"].BaseValue);
        if (p is not null) p.VigorAmount += DynamicVars["Vigor"].IntValue;
    }
    protected override void OnUpgrade() { DynamicVars["Block"].UpgradeValueBy(1m); DynamicVars["Vigor"].UpgradeValueBy(2m); }
}
