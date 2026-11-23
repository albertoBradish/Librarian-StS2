using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.LibrarianCode.Powers.Implemented;

/// <summary>Independent owner-turn windows. Native damage totals include damage absorbed by Block.</summary>
public sealed class ChargedStrikePower : ImplementedLibrarianPower, IOrbAfterEndTurnListener
{
    private sealed class Window(long dueTurn, CardModel source)
    {
        internal long DueTurn = dueTurn;
        internal CardModel Source = source;
        internal decimal Damage;
    }
    private sealed class Data { internal readonly List<Window> Windows = []; }
    protected override object InitInternalData() => new Data();
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Damage", 0m)];

    public void Schedule(LibrarianSession session, CardModel source)
    {
        GetInternalData<Data>().Windows.Add(new(session.Orbs.OwnerTurn + 1, source));
        RefreshDamage();
    }

    private void RefreshDamage()
    {
        DynamicVars["Damage"].BaseValue = GetInternalData<Data>().Windows.Sum(window => window.Damage);
    }

    public override Task AfterDamageGiven(PlayerChoiceContext context, Creature? dealer,
        DamageResult result, ValueProp props, Creature target, CardModel? source)
    {
        if ((dealer == Owner || dealer?.PetOwner?.Creature == Owner) && target.Side != Owner.Side)
        {
            foreach (var window in GetInternalData<Data>().Windows)
                window.Damage += result.TotalDamage;
            RefreshDamage();
        }
        return Task.CompletedTask;
    }

    public async Task AfterOrbSettlements(LibrarianSession session, PlayerChoiceContext context)
    {
        if (!BelongsTo(session) || Owner.IsDead) return;
        var windows = GetInternalData<Data>().Windows;
        // Freeze and remove all windows due now before any payout can record itself or another due payout.
        var due = windows.Where(window => window.DueTurn <= session.Orbs.OwnerTurn)
            .Select(window => (window.Source, window.Damage)).ToArray();
        windows.RemoveAll(window => window.DueTurn <= session.Orbs.OwnerTurn);
        foreach (var (source, damage) in due)
        {
            if (Owner.IsDead || MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsOverOrEnding) break;
            var targets = Owner.CombatState!.GetOpponentsOf(Owner)
                .Where(target => target.IsHittable && MegaCrit.Sts2.Core.Hooks.Hook.ShouldAllowTargeting(Owner.CombatState, target, out _)).ToList();
            var target = session.LastAttackedEnemy;
            if (target is null || !targets.Contains(target))
                target = targets.Count == 0 ? null : session.Player.RunState.Rng.CombatTargets.NextItem(targets);
            if (target is not null && damage > 0)
            {
                Flash();
                await LibrarianCardRemasterVfx.Delayed(source, target);
#if LIBRARIAN_API_0111
                await CreatureCmd.Damage(context, target, damage, ValueProp.Unpowered, Owner, source, null);
#else
                await CreatureCmd.Damage(context, target, damage, ValueProp.Unpowered, Owner, source);
#endif
            }
        }
        RefreshDamage();
        if (due.Length > 0 && Owner.Powers.Contains(this))
            await PowerCmd.ModifyAmount(context, this, -due.Length, null, null);
    }
}
