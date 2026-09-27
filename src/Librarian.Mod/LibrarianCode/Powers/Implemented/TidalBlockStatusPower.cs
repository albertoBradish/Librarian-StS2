using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Models;

namespace Librarian.LibrarianCode.Powers.Implemented;

/// <summary>Presentation of renewable Wave stacks; never grants Block or schedules a settlement.</summary>
public sealed class TidalBlockStatusPower : ImplementedLibrarianPower
{
    public static void Synchronize(LibrarianSession session)
    {
        int remaining = session.Waves.Amount;
        var owner = session.Player.Creature;
        var status = owner.GetPower<TidalBlockStatusPower>();
        if (remaining == 0)
        {
            status?.RemoveInternal();
            return;
        }
        // Bypass gameplay Apply hooks deliberately: this is a mirror of an already committed gain.
        if (status is null)
            ModelDb.Power<TidalBlockStatusPower>().ToMutable().ApplyInternal(owner, remaining, silent: true);
        else if (status.Amount != remaining)
            status.SetAmount(remaining, silent: true);
    }
}
