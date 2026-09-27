using Librarian.Core;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.LibrarianCode.Cards.OrbAdvancedBasics;

/// <summary>Shared operations for this batch. All mutations dispatch before the caller's next effect.</summary>
public abstract class OrbAdvancedCard(int cost, CardType type, CardRarity rarity, TargetType target)
    : LibrarianCard(cost, type, rarity, target)
{
    protected LibrarianSession Session => LibrarianRuntime.Get(Owner);
    protected OrbOrigin Origin => new(Id.ToString());
    protected int Amount(string name) => checked((int)DynamicVars[name].BaseValue);

    protected Task Gain(PlayerChoiceContext context, LibrarianSession session, OrbKind kind, int amount)
        => LibrarianRuntime.Dispatch(session, context, session.Orbs.Gain(kind, amount, Origin));

    protected async Task<int> Lose(PlayerChoiceContext context, LibrarianSession session, OrbKind kind, int amount)
    {
        var result = session.Orbs.Lose(kind, amount, OrbScope.All, Origin);
        await LibrarianRuntime.Dispatch(session, context, result);
        return result.ActualAmount;
    }

    protected async Task<int> LoseAll(PlayerChoiceContext context, LibrarianSession session, OrbKind kind)
    {
        var result = session.Orbs.LoseAll(kind, OrbScope.All, Origin);
        await LibrarianRuntime.Dispatch(session, context, result);
        return result.ActualAmount;
    }

    protected Task Hit(PlayerChoiceContext context, CardPlay play, decimal damage)
    {
        ArgumentNullException.ThrowIfNull(play.Target);
        return DamageCmd.Attack(damage).FromCard(this, play).Targeting(play.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(context);
    }

    protected Task HitAll(PlayerChoiceContext context, CardPlay play, decimal damage)
        => DamageCmd.Attack(damage).FromCard(this, play).TargetingAllOpponents(CombatState ?? throw new InvalidOperationException("Attack requires combat."))
            .WithHitFx("vfx/vfx_attack_slash").Execute(context);

    // User override: loss previews read the named orb regardless of position.
    protected static int ForegroundValue(LibrarianSession session, OrbKind kind)
        => session.Orbs.Value(kind);

    // D29: do not call Get() from preview; it creates state and synchronizes the owner turn.
    protected static LibrarianSession? PreviewSession(CardModel card)
        => card.IsMutable && card.Owner is { } owner && LibrarianRuntime.TryGet(owner, out var session) ? session : null;
    protected static int PreviewForeground(CardModel card, OrbKind kind)
        => PreviewSession(card) is { } session ? ForegroundValue(session, kind) : 0;

    protected static IEnumerable<DynamicVar> CalculatedDamage(
        Func<CardModel, Creature?, decimal> calculation, int multiplier = 1)
        => [new CalculationBaseVar(0m), new ExtraDamageVar(multiplier),
            new CalculatedDamageVar(ValueProp.Move).WithMultiplier(calculation)];
}
