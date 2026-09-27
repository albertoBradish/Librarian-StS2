using Librarian.Core;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.LibrarianCode.Cards.OrbUtility;

/// <summary>Ordered native and orb operations for catalog 36-79. No state is created by previews.</summary>
public abstract class OrbUtilityCard(int cost, CardType type, CardRarity rarity, TargetType target)
    : LibrarianCard(cost, type, rarity, target)
{
    protected LibrarianSession Session => LibrarianRuntime.Get(Owner);
    protected OrbOrigin Origin => new(Id.ToString());
    protected int Amount(string name) => checked((int)decimal.Floor(DynamicVars[name].BaseValue));
    protected static LibrarianSession? PreviewSession(CardModel card)
        => card.IsMutable && card.Owner is { } owner && LibrarianRuntime.TryGet(owner, out var session) ? session : null;
    protected static int PreviewForeground(CardModel card, OrbKind kind)
        => PreviewSession(card) is { } session ? session.Orbs.Value(kind) : 0;

    protected Task Gain(PlayerChoiceContext context, LibrarianSession session, OrbKind kind, int amount)
        => LibrarianRuntime.Dispatch(session, context, session.Orbs.Gain(kind, amount, Origin));

    protected async Task<int> Lose(PlayerChoiceContext context, LibrarianSession session, OrbKind kind,
        int? amount = null, OrbScope scope = OrbScope.All)
    {
        var operation = amount is { } requested
            ? session.Orbs.Lose(kind, requested, scope, Origin)
            : session.Orbs.LoseAll(kind, scope, Origin);
        await LibrarianRuntime.Dispatch(session, context, operation);
        // Callback effects cannot overwrite this operation's actual loss.
        return operation.ActualAmount;
    }

    protected Task Extinguish(PlayerChoiceContext context, LibrarianSession session, OrbKind kind,
        OrbScope scope = OrbScope.Foreground)
        => LibrarianRuntime.Dispatch(session, context, session.Orbs.Extinguish(kind, scope, Origin));

    protected Task Block(CardPlay play, decimal amount)
        => CreatureCmd.GainBlock(Owner.Creature, amount, ValueProp.Move, play);
    protected Task Draw(PlayerChoiceContext context, int amount)
        => LibrarianRuntime.DrawOrDeferAsync(Session, context, amount);
    protected Task Hit(PlayerChoiceContext context, CardPlay play, decimal amount, bool all = false)
    {
        var attack = DamageCmd.Attack(amount).FromCard(this, play);
        if (all) attack.TargetingAllOpponents(CombatState ?? throw new InvalidOperationException("Attack requires combat."));
        else { ArgumentNullException.ThrowIfNull(play.Target); attack.Targeting(play.Target); }
        return attack.WithHitFx("vfx/vfx_attack_slash").Execute(context);
    }
    protected ValueTask<int> Settle(PlayerChoiceContext context, LibrarianSession session, OrbKind kind, int count = 1)
        => session.Orbs.SettleImmediatelyAsync(OrbSelector.Named(kind, OrbScope.All), count,
            request => LibrarianRuntime.Settle(session, context, request), Id.ToString(),
            canSettle: () => !MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsOverOrEnding && !Owner.Creature.IsDead);

    protected Task Lock(PlayerChoiceContext context, OrbKind kind, int? turns = null)
        => LibrarianRuntime.Dispatch(Session, context, Session.Orbs.Lock(kind, turns, Origin));

    protected int LockedCount => Session.Orbs.Positions.Count(Session.Orbs.IsLocked);
    protected static int PreviewLockedCount(CardModel card)
        => PreviewSession(card) is { } s ? s.Orbs.Positions.Count(s.Orbs.IsLocked) : 0;

    protected async Task SettleActiveInOrder(PlayerChoiceContext context)
    {
        var visited = new HashSet<OrbKind>();
        while (Session.Orbs.Positions.FirstOrDefault(k => !visited.Contains(k) && Session.Orbs.IsActivated(k)
            && !Session.Orbs.IsLocked(k), (OrbKind)(-1)) is var kind && (int)kind >= 0)
        {
            visited.Add(kind);
            if (await Settle(context, Session, kind) == 0) break;
            await Extinguish(context, Session, kind, OrbScope.All);
        }
    }
}
