using Librarian.Core;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace Librarian.LibrarianCode.Powers.Implemented;

public sealed class SeedburialPendingPower : ImplementedLibrarianPower
{
    public void Schedule(LibrarianSession session, int value)
    {
        session.QueueNextTurn(async context =>
        {
            if (!Owner.Powers.Contains(this) || Owner.IsDead) return;
            Flash();
            await PowerCmd.ModifyAmount(context, this, -value, null, null);
            await Gain(context, session, OrbKind.Growth, value);
        });
    }
}

public sealed class DeepSeaPendingPower : ImplementedLibrarianPower
{
    public void Schedule(LibrarianSession session, int value)
    {
        session.QueueEndTurn(async context =>
        {
            if (!Owner.Powers.Contains(this) || Owner.IsDead) return;
            Flash();
            await PowerCmd.ModifyAmount(context, this, -value, null, null);
            await Gain(context, session, OrbKind.Tide, value);
        });
    }
}

public sealed class LifeSymphonyPendingPower : ImplementedLibrarianPower, IOrbEndTurnListener
{
    public async Task BeforeOrbSettlements(LibrarianSession session, PlayerChoiceContext context)
    {
        if (!BelongsTo(session) || Owner.IsDead) return;
        session.Orbs.QueueExtraSettlement(OrbSelector.Named(OrbKind.Growth, OrbScope.All), Amount, Id.ToString());
        Flash();
        await PowerCmd.Remove(this);
    }
}

public sealed class PracticeMakesPerfectPower : ImplementedLibrarianPower, IOrbEventListener
{
    private sealed class Cycle(int interval) { internal int Interval = interval; internal int Remaining = interval; }
    private sealed class Data { internal readonly List<Cycle> Cycles = []; }
    protected override object InitInternalData() => new Data();
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Remaining", 4m), new StringVar("Progress", "")];
    public override int DisplayAmount => DynamicVars["Remaining"].IntValue;
    public void AddCycle(int interval)
    {
        GetInternalData<Data>().Cycles.Add(new(interval));
        UpdateProgress();
    }
    private void UpdateProgress()
    {
        var cycles = GetInternalData<Data>().Cycles;
        DynamicVars["Remaining"].BaseValue = cycles.Count == 0 ? 0 : cycles.Min(c => c.Remaining);
        ((StringVar)DynamicVars["Progress"]).StringValue = string.Join(LibrarianLanguage.Format("LIST_SEPARATOR"), cycles.Select(c => LibrarianLanguage.Format("PRACTICE_PROGRESS", ("Remaining", c.Remaining), ("Interval", c.Interval))));
        InvokeDisplayAmountChanged();
    }
    public async Task OnOrbEvent(LibrarianSession session, PlayerChoiceContext context, OrbEvent change)
    {
        if (!OwnEvent(session, change) || !LibrarianChannelEvents.IsSuccessfulChannel(session, change)) return;
        int energy = 0;
        foreach (var cycle in GetInternalData<Data>().Cycles)
            if (--cycle.Remaining == 0) { cycle.Remaining = cycle.Interval; energy++; }
        UpdateProgress();
        if (energy > 0) { Flash(); await PlayerCmd.GainEnergy(energy, session.Player); }
    }
}

public sealed class ToBeContinuedPower : ImplementedLibrarianPower
{
    public override async Task AfterPlayerTurnStart(PlayerChoiceContext context, Player player)
    {
        if (Owner.Player != player || Owner.IsDead) return;
        int repeats = Amount;
        for (int i = 0; i < repeats && !Owner.IsDead; i++)
        {
            Flash();
            await CardPileCmd.Draw(context, 1, player);
            var prefs = new CardSelectorPrefs(new LocString("card_selection", "LIBRARIAN_TO_BE_CONTINUED"), 1);
            var selected = await CardSelectCmd.FromHand(context, player, prefs, null, this);
            foreach (var card in selected)
                if (card.Pile?.Type == PileType.Hand)
                    await LibrarianBottomPlay041.MoveToBottomAsync(context, card);
        }
    }
}

public sealed class EndlessTidePower : ImplementedLibrarianPower
{
    public override PowerStackType StackType => PowerStackType.Single;
    public override async Task BeforeHandDraw(Player player, PlayerChoiceContext context, MegaCrit.Sts2.Core.Combat.ICombatState combatState)
    {
        if (player != Owner.Player || Owner.IsDead) return;
        Flash();
        var session = LibrarianRuntime.Get(player);
        await Gain(context, session, OrbKind.Tide, 1);
    }
}
