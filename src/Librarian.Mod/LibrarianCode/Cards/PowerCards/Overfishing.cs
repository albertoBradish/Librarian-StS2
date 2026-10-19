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
public sealed class Overfishing() : ImplementedPowerCard(2, CardRarity.Rare)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("UpgradedNotes", 0m)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [HoverTipFactory.FromCard<StudyNotes>()];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var kind = await LibrarianOrbChoice.Choose(context, this, Session.Orbs.Positions.ToArray());
        if (kind is null) return;
        await LibrarianRuntime.Dispatch(Session, context, Session.Orbs.Lock(kind.Value, origin: new(Id.ToString())));
        var power = await Apply<Librarian.LibrarianCode.Powers.Implemented.OverfishingPower>(context, 1m);
        power?.RegisterNotes(IsUpgraded);
    }
    protected override void OnUpgrade() => DynamicVars["UpgradedNotes"].UpgradeValueBy(1m);
}
