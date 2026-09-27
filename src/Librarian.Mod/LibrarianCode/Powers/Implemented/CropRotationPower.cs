using Librarian.Core;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace Librarian.LibrarianCode.Powers.Implemented;

/// <summary>Amount displays total remaining activations across independent cast durations.</summary>
public sealed class CropRotationPower : ImplementedLibrarianPower
{
    private sealed class Cast(int remaining) { internal int Remaining = remaining; }
    private sealed class Data { internal readonly List<Cast> Casts = []; }
    protected override object InitInternalData() => new Data();
    protected override IEnumerable<DynamicVar> CanonicalVars => [new StringVar("Durations", "")];
    private void UpdateDurations()
    {
        ((StringVar)DynamicVars["Durations"]).StringValue = string.Join(
            LibrarianLanguage.Format("LIST_SEPARATOR"), GetInternalData<Data>().Casts.Select(c => c.Remaining));
        InvokeDisplayAmountChanged();
    }
    public void Schedule(LibrarianSession session, int remaining)
    {
        var cast = new Cast(remaining);
        GetInternalData<Data>().Casts.Add(cast);
        UpdateDurations();
        async Task Tick(PlayerChoiceContext context)
        {
            // Removed/replaced powers must not leave invisible scheduled effects behind.
            if (!Owner.Powers.Contains(this) || Owner.IsDead) return;
            var candidates = session.Orbs.Positions.Where(k => !session.Orbs.IsLocked(k) && !session.Orbs.IsActivated(k)).ToArray();
            Flash();
            if (candidates.Length > 0)
            {
                var chosen = candidates[new LibrarianRuntime.GameOrbRandom(session.Player).NextInt(candidates.Length)];
                await LibrarianRuntime.Dispatch(session, context, session.Orbs.Activate(chosen, OrbScope.All, Origin));
            }
            // Like native LightningRodPower, even an empty candidate set consumes duration.
            if (--cast.Remaining == 0) GetInternalData<Data>().Casts.Remove(cast);
            UpdateDurations();
            await PowerCmd.Decrement(this);
            if (cast.Remaining > 0) session.QueueNextTurn(Tick);
        }
        session.QueueNextTurn(Tick);
    }
}
