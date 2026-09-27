using Librarian.Core;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.LibrarianCode.Powers.Implemented;

/// <summary>Source 81. Like native Afterimage: snapshot pre-play layers, so applying this Power cannot trigger its new layers.</summary>
public sealed class LifelinePower : ImplementedLibrarianPower, IOrbEventListener
{
    public async Task OnOrbEvent(LibrarianSession session, PlayerChoiceContext context, OrbEvent change)
    {
        if (!OwnEvent(session, change) || !LibrarianChannelEvents.IsSuccessfulChannel(session, change)) return;
        Flash();
        await LibrarianRuntime.Dispatch(session, context, session.Orbs.Strengthen(session.Orbs.Foreground, Amount, OrbScope.All, Origin));
    }

    private sealed class Data { public readonly Dictionary<CardPlay, int> BeforePlayAmounts = new(); }
    protected override object InitInternalData() => new Data();

    public override Task BeforeCardPlayed(CardPlay cardPlay)
    {
        if (cardPlay.Player == Owner.Player)
            GetInternalData<Data>().BeforePlayAmounts[cardPlay] = Amount;
        return Task.CompletedTask;
    }

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Player != Owner.Player || !GetInternalData<Data>().BeforePlayAmounts.Remove(cardPlay, out int amount) || amount <= 0) return;
        var session = LibrarianRuntime.Get(cardPlay.Player);
        Flash();
        await LibrarianRuntime.Dispatch(session, choiceContext,
            session.Orbs.Strengthen(session.Orbs.Foreground, amount, OrbScope.Foreground, Origin));
    }
}
