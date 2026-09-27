using Librarian.Core;
using Librarian.LibrarianCode.Integration;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace Librarian.LibrarianCode.Cards.OrbBasics;

/// <summary>Small action helpers; all concrete cards remain explicit about effect order.</summary>
public abstract class OrbBasicsCard(int cost, CardType type, TargetType target)
    : LibrarianCard(cost, type, CardRarity.Common, target)
{
    protected decimal Amount(string name) => DynamicVars[name].BaseValue;

    protected Task GainAsync(PlayerChoiceContext context, CardPlay play, OrbKind orb, string variable)
        => LibrarianMechanicsBridge.Current.GainAsync(context, Owner, orb switch
        {
            OrbKind.Fire => LibrarianElement.Fire,
            OrbKind.Tide => LibrarianElement.Water,
            OrbKind.Growth => LibrarianElement.Earth,
            _ => throw new ArgumentOutOfRangeException(nameof(orb))
        }, Amount(variable), this, play);

    protected async Task<int> LoseAsync(PlayerChoiceContext context, OrbKind orb, string? variable = null)
    {
        var session = LibrarianRuntime.Get(Owner);
        var origin = new OrbOrigin(Id.ToString());
        // User override: losses affect the named orb in either position; loss still clamps at zero.
        var operation = variable is null
            ? session.Orbs.LoseAll(orb, OrbScope.All, origin)
            : session.Orbs.Lose(orb, checked((int)decimal.Floor(Amount(variable))), OrbScope.All, origin);
        await LibrarianRuntime.Dispatch(session, context, operation);
        // Keep the original actual loss even if a callback gains values or changes foreground.
        return operation.ActualAmount;
    }

    protected Task HitAsync(PlayerChoiceContext context, CardPlay play, decimal damage, bool allEnemies = false)
    {
        var attack = DamageCmd.Attack(damage).FromCard(this, play);
        if (allEnemies) attack.TargetingAllOpponents(CombatState ?? throw new InvalidOperationException("Attack requires combat."));
        else
        {
            ArgumentNullException.ThrowIfNull(play.Target);
            attack.Targeting(play.Target);
        }
        return attack.WithHitFx("vfx/vfx_attack_slash").Execute(context);
    }

    protected Task BlockAsync(CardPlay play) => CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, play);
    protected Task DrawAsync(PlayerChoiceContext context) => CardPileCmd.Draw(context, DynamicVars.Cards.BaseValue, Owner);
}
