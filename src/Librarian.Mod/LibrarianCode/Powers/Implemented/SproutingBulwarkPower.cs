using Librarian.Core;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.LibrarianCode.Powers.Implemented;

/// <summary>Spend Growth on post-block damage until the owner's next turn. Reapplication does not multiply absorption.</summary>
public sealed class SproutingBulwarkPower : ImplementedLibrarianPower
{
    public override PowerStackType StackType => PowerStackType.Single;
    private sealed class Data { internal int PendingLoss; }
    protected override object InitInternalData() => new Data();

    public override decimal ModifyHpLostAfterOstyLate(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target != Owner || props.HasFlag(ValueProp.Unblockable) || LibrarianRuntime.For(Owner) is not { } session)
            return amount;
        int absorbed = (int)Math.Min(Math.Max(decimal.Truncate(amount), 0m), session.Orbs.Value(OrbKind.Growth));
        // Native previews run this hook too. Only the subsequent native commit callback may spend Growth.
        GetInternalData<Data>().PendingLoss = absorbed;
        return amount - absorbed;
    }

    public override async Task AfterModifyingHpLostAfterOsty()
    {
        int absorbed = GetInternalData<Data>().PendingLoss;
        GetInternalData<Data>().PendingLoss = 0;
        if (absorbed <= 0 || LibrarianRuntime.For(Owner) is not { } session) return;
        Flash();
        await LibrarianRuntime.Dispatch(session, new ThrowingPlayerChoiceContext(),
            session.Orbs.Lose(OrbKind.Growth, absorbed, OrbScope.All, Origin));
    }

    public override Task BeforeSideTurnStart(PlayerChoiceContext context, CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
        => side == CombatSide.Player && participants.Contains(Owner) ? PowerCmd.Remove(this) : Task.CompletedTask;
}
