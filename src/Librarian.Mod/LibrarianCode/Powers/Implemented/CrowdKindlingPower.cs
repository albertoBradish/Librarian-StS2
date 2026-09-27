using Librarian.Core;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;

namespace Librarian.LibrarianCode.Powers.Implemented;

/// <summary>One owner's counter listens to each actual allied card play, including replayed attacks.</summary>
public sealed class CrowdKindlingPower : ImplementedLibrarianPower
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [LibrarianHoverTips.Tip("STRENGTHEN")];

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // CardPlay.Player is the actual player, even if the card transferred ownership during play.
        // The native combat hook dispatch is shared by all players; never filter to local ownership.
        if (Amount <= 0 || Owner.IsDead || Owner.Player is not { } owner || Owner.CombatState is not { } combat
            || cardPlay.Card.Type != CardType.Attack || cardPlay.Card.Owner.Creature.CombatState != combat)
            return;

        var session = LibrarianRuntime.Get(owner);
        Flash();
        // Strengthen only: no activation, position change, target randomness or extra settlement.
        await LibrarianRuntime.Dispatch(session, choiceContext,
            session.Orbs.Strengthen(OrbKind.Fire, Amount, OrbScope.All, Origin));
    }
}
