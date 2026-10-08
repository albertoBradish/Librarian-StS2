using Librarian.Core;
using Librarian.Mechanics;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.LibrarianCode.Cards.OrbUtility;

/// <summary>Source 63/r64. All three immediate settlements precede the three actual losses.</summary>
[BaseLib.Utils.Attributes.CustomID("LIBRARIAN-THREEFOLD_UNITY")]
public sealed class ThreefoldUnity() : OrbUtilityCard(3, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var session = Session;
        var chosen = await LibrarianOrbChoice.Choose(context, this, session.Orbs.Positions.ToArray());
        if (chosen is not { } target) return;
        // Preserve identities. Loss callbacks resolve normally, then the actual lost amount is transferred.
        int total = 0;
        foreach (var kind in session.Orbs.Positions.Where(k => k != target).ToArray())
            total = checked(total + await Lose(context, session, kind, scope: OrbScope.All));
        await LibrarianRuntime.Dispatch(session, context, session.Orbs.Strengthen(target, total, OrbScope.All, Origin));
        await Settle(context, session, target, 3);
    }
    protected override void OnUpgrade() => AddKeyword(CardKeyword.Retain);
}

/// <summary>Source 65/r66. Capture cumulative actual-loss events before the attack.</summary>
[BaseLib.Utils.Attributes.CustomID("LIBRARIAN-ZERO_SEARCH")]
public sealed class ZeroSearch() : OrbUtilityCard(1, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new CalculationBaseVar(3m), new CalculationExtraVar(1m),
            new CalculatedVar("Cards").WithMultiplier((card, _) => PreviewSession(card)?.Orbs.LockedKindsThisCombatCount ?? 0)];
    protected override Task OnPlay(PlayerChoiceContext context, CardPlay play)
        => Draw(context, (int)DynamicVars.CalculationBase.BaseValue + Session.Orbs.LockedKindsThisCombatCount);
    protected override void OnUpgrade() => DynamicVars.CalculationBase.UpgradeValueBy(1m);
    protected override void AfterDowngraded()
    {
        DynamicVars.RecalculateForUpgradeOrEnchant();
        DynamicVars.FinalizeUpgrade();
    }
}

/// <summary>V1.2.0-beta1: three/four hits; both stages exhaust.</summary>
[BaseLib.Utils.Attributes.CustomID("LIBRARIAN-BOOK_BURNING")]
public sealed class BookBurning() : OrbUtilityCard(2, CardType.Attack, CardRarity.Rare, TargetType.AllEnemies)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new CalculationBaseVar(0m), new ExtraDamageVar(1m), new RepeatVar(3),
            new CalculatedDamageVar(ValueProp.Move).WithMultiplier((card, _) => PreviewForeground(card, OrbKind.Fire))];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        int lost = await Lose(context, Session, OrbKind.Fire);
        await DamageCmd.Attack(lost).FromCard(this, play).TargetingAllOpponents(CombatState!)
            .WithHitCount(Amount("Repeat")).WithHitFx("vfx/vfx_attack_slash").Execute(context);
    }
    protected override void OnUpgrade() => DynamicVars.Repeat.UpgradeValueBy(1m);
}

/// <summary>V1.2.0-beta1: energy1, Growth2, Tide3, Fire4; Fire ends foreground.</summary>
[BaseLib.Utils.Attributes.CustomID("LIBRARIAN-READ_WIDELY")]
public sealed class ReadWidely() : OrbUtilityCard(0, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new EnergyVar(1), new DynamicVar("Growth", 2m), new DynamicVar("Tide", 3m), new DynamicVar("Fire", 4m)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var session = Session;
        await PlayerCmd.GainEnergy(Amount("Energy"), Owner);
        await Gain(context, session, OrbKind.Growth, Amount("Growth"));
        await Gain(context, session, OrbKind.Tide, Amount("Tide"));
        await Gain(context, session, OrbKind.Fire, Amount("Fire"));
    }
    protected override void OnUpgrade() => AddKeyword(CardKeyword.Innate);
}

/// <summary>Source 71/r72. D11 freezes orb identities, not mutable values, before any loss callbacks.</summary>
[BaseLib.Utils.Attributes.CustomID("LIBRARIAN-LONELY_SCROLL")]
public sealed class LonelyScroll() : OrbUtilityCard(0, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Retain, CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new RepeatVar(1)];
    protected override bool IsPlayable => PreviewSession(this) is { } session && session.Orbs.Select(OrbScope.All, ActivationFilter.Active).Count == 3;
    protected override bool ShouldGlowGoldInternal => IsPlayable;
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var session = Session;
        // Native playability governs manual and autoplay requests. Keep this guard for an invalid forced play.
        if (session.Orbs.Select(OrbScope.All, ActivationFilter.Active).Count != 3) return;
        var foreground = session.Orbs.Foreground;
        var background = session.Orbs.Select(OrbScope.Background).ToArray();
        int total = 0;
        foreach (var kind in background)
            total = checked(total + await Lose(context, session, kind, scope: OrbScope.All));
        await Gain(context, session, foreground, total);
        await Settle(context, session, foreground, Amount("Repeat"));
    }
    protected override void OnUpgrade() => DynamicVars.Repeat.UpgradeValueBy(1m);
}

/// <summary>v0.3.0 approved revision, catalog 72. Stable model ID retained.</summary>
[BaseLib.Utils.Attributes.CustomID("LIBRARIAN-DROUGHT_EDICT")]
public sealed class DroughtEdict() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(2, CardType.Skill, CardRarity.Rare, TargetType.AllEnemies)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        int lost = await Lose(context, Session, OrbKind.Tide);
        await Hit(context, play, lost, all: true);
        await Lock(context, OrbKind.Tide);
    }
    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
    }
}

/// <summary>Legacy model retained for old saves; the active pool uses FuelTheFire.</summary>
[BaseLib.Utils.Attributes.CustomID("LIBRARIAN-FOREST_WALL")]
public sealed class ForestWall() : OrbUtilityCard(1, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    public override bool GainsBlock => true;
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new CalculationBaseVar(0m), new CalculationExtraVar(2m),
            new CalculatedBlockVar(ValueProp.Move).WithMultiplier((card, _) => PreviewForeground(card, OrbKind.Growth)), new DynamicVar("Threshold", 8m)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        int lost = await Lose(context, Session, OrbKind.Growth);
        await Block(play, lost * DynamicVars.CalculationExtra.BaseValue);
        if (lost >= Amount("Threshold"))
            foreach (var power in Owner.Creature.Powers.Where(power => power.TypeForCurrentAmount == PowerType.Debuff).ToArray())
                if (Owner.Creature.Powers.Contains(power)) await PowerCmd.Remove(power);
    }
    protected override void OnUpgrade() => DynamicVars.CalculationExtra.UpgradeValueBy(1m);
}

/// <summary>V0.4.1: each layer plays the draw-pile bottom once at the end of the owner's turn.</summary>
[BaseLib.Utils.Attributes.CustomID("LIBRARIAN-FUEL_THE_FIRE")]
public sealed class FuelTheFire() : LibrarianCard(2, CardType.Power, CardRarity.Uncommon, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new PowerVar<FuelTheFirePower>(1m)];

    protected override Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        // Foreign owners need the existing per-combat session for the shared
        // end-turn hook, while HasCharacterOrbs stays false and no orb UI/state
        // is created for that character.
        _ = LibrarianRuntime.Get(Owner);
        return PowerCmd.Apply<FuelTheFirePower>(context, Owner.Creature,
            DynamicVars["FuelTheFirePower"].BaseValue, Owner.Creature, this);
    }

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}

/// <summary>v0.3.0 approved revision, catalog 75. Stable model ID retained.</summary>
[BaseLib.Utils.Attributes.CustomID("LIBRARIAN-TREE_RING_BURST")]
public sealed class TreeRingBurst() : OrbUtilityCard(2, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new RepeatVar(1)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        for (int round = 0; round < Amount("Repeat"); round++)
            foreach (var kind in Session.Orbs.Positions.ToArray())
                if (!Session.Orbs.IsLocked(kind))
                    await Settle(context, Session, kind);
    }
    protected override void OnUpgrade() => DynamicVars.Repeat.UpgradeValueBy(1m);
}

/// <summary>v0.3.0 approved revision, catalog 76. Stable model ID retained.</summary>
[BaseLib.Utils.Attributes.CustomID("LIBRARIAN-SONG_OF_ICE_AND_FIRE")]
public sealed class SongOfIceAndFire() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(1, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(5m, ValueProp.Move)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var chosen = await LibrarianOrbChoice.Choose(context, this, Session.Orbs.Positions.Where(k => !Session.Orbs.IsLocked(k)).ToArray());
        if (chosen is { } kind) await LibrarianRuntime.Dispatch(Session, context, Session.Orbs.Activate(kind, OrbScope.All, Origin));
        await Block(play, Amount("Block"));
    }
    protected override void OnUpgrade()
    {
        DynamicVars["Block"].UpgradeValueBy(3m);
    }
}

/// <summary>Source 77/r78. The extra Growth task retains the ordinary foreground settlement scope.</summary>
[BaseLib.Utils.Attributes.CustomID("LIBRARIAN-LIFE_SYMPHONY")]
public sealed class LifeSymphony() : OrbUtilityCard(1, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Growth", 8m)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var session = Session;
        await Gain(context, session, OrbKind.Growth, Amount("Growth"));
        await PowerCmd.Apply<LifeSymphonyPendingPower>(context, Owner.Creature, 1, Owner.Creature, this);
    }
    protected override void OnUpgrade() => DynamicVars["Growth"].UpgradeValueBy(4m);
}

/// <summary>V1.2.0-beta1: successful Exhaust moves queue one aggregate Waves gain.</summary>
[BaseLib.Utils.Attributes.CustomID("LIBRARIAN-DEEP_SEA_BARRIER")]
public sealed class DeepSeaBarrier() : OrbUtilityCard(2, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Waves", 5m)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var session = Session;
        int perCard = Amount("Waves");
        var cards = PileType.Hand.GetPile(Owner).Cards.Where(card => !ReferenceEquals(card, this)).ToArray();
        int exhausted = 0;
        foreach (var card in cards)
        {
            // Earlier exhaust hooks may have moved another snapshot member out of hand already.
            if (card.Pile?.Type != PileType.Hand) continue;
            var result = await CardCmd.Exhaust(context, card);
            // The move result survives after-exhaust hooks that immediately move the card again.
            if (result is { success: true, targetPile: PileType.Exhaust }) exhausted++;
        }
        int tide = checked(exhausted * perCard);
        if (tide > 0)
        {
            var pending = await PowerCmd.Apply<DeepSeaPendingPower>(context, Owner.Creature, tide, Owner.Creature, this);
            pending?.Schedule(session, tide);
        }
    }
    protected override void OnUpgrade() => DynamicVars["Waves"].UpgradeValueBy(1m);
}

/// <summary>Source 79/r80. v0.3.4: settle the foreground X times, retain its value, and exhaust.</summary>
[BaseLib.Utils.Attributes.CustomID("LIBRARIAN-MULTIPLE_ERUPTION")]
public sealed class MultipleEruption() : OrbUtilityCard(0, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    protected override bool HasEnergyCostX => true;
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Extra", 0m)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        int count = checked(ResolveEnergyXValue() + Amount("Extra"));
        var session = Session;
        await Settle(context, session, session.Orbs.Foreground, count);
    }
    protected override void OnUpgrade() => DynamicVars["Extra"].UpgradeValueBy(1m);
}
