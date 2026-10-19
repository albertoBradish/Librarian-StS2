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
public sealed class BlazingChapter() : ImplementedPowerCard(2, CardRarity.Rare)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<Librarian.LibrarianCode.Powers.Implemented.BlazingChapterPower>(2m)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromPower<StrengthPower>()];
    protected override Task OnPlay(PlayerChoiceContext context, CardPlay play) => Apply<Librarian.LibrarianCode.Powers.Implemented.BlazingChapterPower>(context, DynamicVars["BlazingChapterPower"].BaseValue);
    protected override void OnUpgrade() => DynamicVars["BlazingChapterPower"].UpgradeValueBy(1m);
}
