using Librarian.Core;
using Librarian.Mechanics;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace Librarian.LibrarianCode.Cards;

public sealed class PracticeMakesPerfect() : LibrarianCard(1, CardType.Power, CardRarity.Uncommon, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Interval", 4m), new EnergyVar(1)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var power = await PowerCmd.Apply<PracticeMakesPerfectPower>(context, Owner.Creature, 1, Owner.Creature, this);
        power?.AddCycle(DynamicVars["Interval"].IntValue);
    }
    protected override void OnUpgrade() => DynamicVars["Interval"].UpgradeValueBy(-1m);
}

public sealed class ToBeContinued() : LibrarianCard(1, CardType.Power, CardRarity.Uncommon, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<ToBeContinuedPower>(1m)];
    protected override Task OnPlay(PlayerChoiceContext context, CardPlay play)
        => PowerCmd.Apply<ToBeContinuedPower>(context, Owner.Creature, 1, Owner.Creature, this);
    protected override void OnUpgrade() => AddKeyword(CardKeyword.Innate);
}

public sealed class ReRead() : OrbUtilityCard(1, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override void OnUpgrade() => AddKeyword(CardKeyword.Retain);
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var session = Session;
        var chosen = await LibrarianOrbChoice.Choose(context, this, session.Orbs.Positions.ToArray());
        if (chosen is not { } kind) return;
        int delta = Math.Max(0, session.Orbs.HighestValueThisCombat(kind) - session.Orbs.Value(kind));
        await LibrarianRuntime.Dispatch(session, context, session.Orbs.Strengthen(kind, delta, OrbScope.All, Origin));
    }
}

public sealed class EndlessTide() : LibrarianCard(2, CardType.Power, CardRarity.Rare, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<EndlessTidePower>(1m)];
    protected override Task OnPlay(PlayerChoiceContext context, CardPlay play)
        => Owner.Creature.GetPower<EndlessTidePower>() is null
            ? PowerCmd.Apply<EndlessTidePower>(context, Owner.Creature, 1, Owner.Creature, this)
            : Task.CompletedTask;
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}
