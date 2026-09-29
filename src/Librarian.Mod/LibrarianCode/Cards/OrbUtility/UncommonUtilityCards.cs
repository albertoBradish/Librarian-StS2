using Librarian.Core;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.LibrarianCode.Cards.OrbUtility;

/// <summary>Source 36/r37. The delayed amount is captured now; its context is supplied next turn.</summary>
public sealed class SeedburialStrike() : OrbUtilityCard(2, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(15m, ValueProp.Move), new DynamicVar("Growth", 12m)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var session = Session;
        int growth = Amount("Growth");
        await Hit(context, play, DynamicVars.Damage.BaseValue);
        var pending = await PowerCmd.Apply<Librarian.LibrarianCode.Powers.Implemented.SeedburialPendingPower>(context, Owner.Creature, growth, Owner.Creature, this);
        pending?.Schedule(session, growth);
    }
    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(5m);
        DynamicVars["Growth"].UpgradeValueBy(4m);
    }
}

/// <summary>V0.4.1: one attack whose amount is the printed base plus current Fire.</summary>
public sealed class EmberReckoning() : OrbUtilityCard(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new CalculationBaseVar(7m), new ExtraDamageVar(1m),
            new CalculatedDamageVar(ValueProp.Move).WithMultiplier((card, _) => PreviewForeground(card, OrbKind.Fire))];
    protected override Task OnPlay(PlayerChoiceContext context, CardPlay play)
        => Hit(context, play, DynamicVars.CalculationBase.BaseValue + Session.Orbs.Value(OrbKind.Fire));
    protected override void OnUpgrade() => DynamicVars.CalculationBase.UpgradeValueBy(3m);
    protected override void AfterDowngraded()
    {
        DynamicVars.RecalculateForUpgradeOrEnchant();
        DynamicVars.FinalizeUpgrade();
    }
}

/// <summary>v0.3.0 approved revision, catalog 45. Stable model ID retained.</summary>
public sealed class Evaporation() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Fire", 6m), new CardsVar(1)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await Gain(context, Session, OrbKind.Fire, Amount("Fire"));
        await Draw(context, Amount("Cards"));
        await Lock(context, OrbKind.Tide, 1);
    }
    protected override void OnUpgrade()
    {
        DynamicVars["Fire"].UpgradeValueBy(3m);
        DynamicVars.Cards.UpgradeValueBy(1m);
    }
}

/// <summary>v0.3.0 approved revision, catalog 46. Stable model ID retained.</summary>
public sealed class Cooldown() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(7m, ValueProp.Move)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await Block(play, Amount("Block"));
        await PowerCmd.Apply<Librarian.LibrarianCode.Powers.Implemented.CooldownPower>(context, Owner.Creature, 1, Owner.Creature, this);
        LibrarianRuntime.VerifyBlock(Session);
        await Lock(context, OrbKind.Fire, 1);
    }
    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(3m);
    }
}

/// <summary>v0.3.0 approved revision, catalog 47. Stable model ID retained.</summary>
public sealed class SproutingBulwark() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(7m, ValueProp.Move)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await Block(play, Amount("Block"));
        if (Owner.Creature.GetPower<Librarian.LibrarianCode.Powers.Implemented.SproutingBulwarkPower>() is null)
            await PowerCmd.Apply<Librarian.LibrarianCode.Powers.Implemented.SproutingBulwarkPower>(context, Owner.Creature, 1, Owner.Creature, this);
    }
    protected override void OnUpgrade()
    {
        DynamicVars["Block"].UpgradeValueBy(3m);
    }
}

/// <summary>V0.4.1: choose one enemy and branch on that enemy's current intent.</summary>
public sealed class ResidualWarmth() : OrbUtilityCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(8m, ValueProp.Move), new DynamicVar("Fire", 8m)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        ArgumentNullException.ThrowIfNull(play.Target);
        bool attacks = play.Target.Monster?.NextMove.Intents.Any(intent =>
            intent.IntentType == MegaCrit.Sts2.Core.MonsterMoves.Intents.IntentType.Attack) == true;
        if (attacks) await Block(play, DynamicVars.Block.BaseValue);
        else await Gain(context, Session, OrbKind.Fire, Amount("Fire"));
    }
    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(3m);
        DynamicVars["Fire"].UpgradeValueBy(3m);
    }
}

/// <summary>Source 50/r51. Explicit ALL overrides the default foreground scope.</summary>
public sealed class SealAway() : OrbUtilityCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(11m, ValueProp.Move)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var session = Session;
        foreach (var kind in session.Orbs.Select(OrbScope.All)) await Extinguish(context, session, kind, OrbScope.All);
        await Block(play, DynamicVars.Block.BaseValue);
        PlayerCmd.EndTurn(Owner, false);
    }
    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(4m);
}

/// <summary>V0.4.1: Fire, draw one, and put an upgraded-state-preserving copy at draw-pile bottom.</summary>
public sealed class Rekindle() : OrbUtilityCard(1, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Fire", 3m), new CardsVar(1)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var session = Session;
        await Gain(context, session, OrbKind.Fire, Amount("Fire"));
        await Draw(context, Amount("Cards"));
        // A fresh card body preserves the upgrade, but never copies replay enchantments.
        var copy = CombatState!.CreateCard<Rekindle>(Owner);
        if (IsUpgraded) { copy.UpgradeInternal(); copy.FinalizeUpgradeInternal(); }
        CardCmd.PreviewCardPileAdd(await CardPileCmd.AddGeneratedCardToCombat(copy, PileType.Draw, Owner, CardPilePosition.Bottom), 2.2f);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}

/// <summary>V0.4.1: base is the background total; upgrade doubles that total without changing cost.</summary>
public sealed class ArchiveBulwark() : OrbUtilityCard(0, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    public override bool GainsBlock => true;
    private static decimal BackgroundTotal(LibrarianSession session) => session.Orbs.Select(OrbScope.Background).Sum(kind => (decimal)session.Orbs.Value(kind));
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new CalculationBaseVar(0m), new CalculationExtraVar(1m),
            new CalculatedBlockVar(ValueProp.Move).WithMultiplier((card, _) => PreviewSession(card) is { } session
                ? BackgroundTotal(session) : 0)];
    protected override Task OnPlay(PlayerChoiceContext context, CardPlay play)
        => Block(play, BackgroundTotal(Session) * DynamicVars.CalculationExtra.BaseValue);
    protected override void OnUpgrade() => DynamicVars.CalculationExtra.UpgradeValueBy(1m);
}

/// <summary>Source 54/r55. The active candidate set is resolved after drawing, and consumes RNG only for an actual tie.</summary>
public sealed class OutOfContext() : OrbUtilityCard(0, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(2)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await Draw(context, Amount("Cards"));
        var session = Session;
        var target = session.Orbs.ResolveSelector(OrbSelector.Random(OrbScope.All, ActivationFilter.Active), new LibrarianRuntime.GameOrbRandom(Owner));
        if (target is { } kind) await Extinguish(context, session, kind, OrbScope.All);
    }
    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(1m);
}

/// <summary>Source 55/r56. User override: extinguish Fire in either position.</summary>
public sealed class Afforestation() : OrbUtilityCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Tide", 5m), new DynamicVar("Growth", 4m)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var session = Session;
        await Gain(context, session, OrbKind.Tide, Amount("Tide"));
        await Gain(context, session, OrbKind.Growth, Amount("Growth"));
        await Extinguish(context, session, OrbKind.Fire, OrbScope.All);
    }
    protected override void OnUpgrade() { DynamicVars["Tide"].UpgradeValueBy(2m); DynamicVars["Growth"].UpgradeValueBy(2m); }
}

/// <summary>v0.3.0 approved revision, catalog 56. Stable model ID retained.</summary>
public sealed class OverloadBurn() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(3m, ValueProp.Move)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await Hit(context, play, Amount("Damage"));
        if (play.Target is { IsAlive: true } && Session.Orbs.IsActivated(OrbKind.Fire))
            await Session.Orbs.SettleImmediatelyAsync(
            OrbSelector.Named(OrbKind.Fire, OrbScope.All), 1,
            request => LibrarianRuntime.Settle(Session, context, request, play.Target), Id.ToString(),
            canSettle: () => !MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsOverOrEnding && !Owner.Creature.IsDead && play.Target.IsAlive);
    }
    protected override void OnUpgrade()
    {
        DynamicVars["Damage"].UpgradeValueBy(2m);
    }
}
