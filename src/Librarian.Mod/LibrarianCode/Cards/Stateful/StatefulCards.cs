using Librarian.Core;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.LibrarianCode.Cards.Stateful;

/// <summary>v0.3.0 approved revision, catalog 48. Stable model ID retained.</summary>
public sealed class Calibrate() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(2)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        bool retain = Session.ResolvingEndTurn;
        await LibrarianBottomPlay041.DrawTopThenTakeBottomAsync(context, Owner, Amount("Cards"), retain);
        // The card is in the Play pile while its effect resolves; moving this instance
        // to the bottom is the final atomic step of the exchange.
        await LibrarianBottomPlay041.MoveToBottomAsync(context, this);
    }
    protected override void OnUpgrade() => DynamicVars["Cards"].UpgradeValueBy(1m);
}

// Catalog 44. The existing starter-card visuals serve as the two choice previews only.
public sealed class DrawBranch() : LibrarianCard(1, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var session = LibrarianRuntime.Get(Owner);
        var loss = session.Orbs.LoseAll(OrbKind.Growth, OrbScope.All, new(Id.ToString()));
        await LibrarianRuntime.Dispatch(session, context, loss);
        int value = checked(loss.ActualAmount * 2);
        var combat = CombatState ?? throw new InvalidOperationException("Choice requires combat.");
        var fire = combat.CreateCard<Spark>(Owner);
        var tide = combat.CreateCard<Trickle>(Owner);
        fire.DynamicVars["Fire"].BaseValue = value;
        tide.DynamicVars["Water"].BaseValue = value;
        fire.SetToFreeThisTurn();
        tide.SetToFreeThisTurn();
        var selected = await CardSelectCmd.FromChooseACardScreen(context, [fire, tide], Owner);
        if (selected is null) return;
        await LibrarianRuntime.Dispatch(session, context,
            session.Orbs.Gain(ReferenceEquals(selected, fire) ? OrbKind.Fire : OrbKind.Tide, value, new(Id.ToString())));
    }
}

// v0.5.2: snapshot Growth once, use it for base damage and temporary Strength loss.
public sealed class Bookworm() : Librarian.LibrarianCode.Cards.OrbAdvancedBasics.OrbAdvancedCard(1, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        CalculatedDamage((card, _) => PreviewSession(card)?.Orbs.Value(OrbKind.Growth) ?? 0);
    protected override IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip> ExtraHoverTips =>
        [MegaCrit.Sts2.Core.HoverTips.HoverTipFactory.FromPower<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>()];
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        ArgumentNullException.ThrowIfNull(play.Target);
        int growth = Session.Orbs.Value(OrbKind.Growth);
        await DamageCmd.Attack(growth).FromCard(this, play).Targeting(play.Target)
            .WithHitFx("vfx/vfx_bite").Execute(context);
        if (growth > 0 && play.Target.IsAlive)
            await PowerCmd.Apply<Librarian.LibrarianCode.Powers.Implemented.BookwormPower>(
                context, play.Target, growth, Owner.Creature, this);
    }
}

// Catalog 66 / D12: mutable-instance field is copied by cloning, never written to DeckVersion or a saved property.
public sealed class NourishingLife() : LibrarianCard(1, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
{
    private int _absorbed;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(5m, ValueProp.Move)];
    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(3m);
    protected override void AfterDowngraded() => DynamicVars.Damage.BaseValue = checked(5 + _absorbed);
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        ArgumentNullException.ThrowIfNull(play.Target);
        var session = LibrarianRuntime.Get(Owner);
        var candidates = session.Orbs.Positions.Where(k => session.Orbs.Value(k) > 0).ToArray();
        OrbKind? kind = candidates.Length switch
        {
            0 => null,
            1 => candidates[0],
            _ => candidates[new LibrarianRuntime.GameOrbRandom(Owner).NextInt(candidates.Length)]
        };
        if (kind is { } selected)
        {
            await session.Orbs.SettleImmediatelyAsync(OrbSelector.Named(selected, OrbScope.All), 1,
                request => LibrarianRuntime.Settle(session, context, request), Id.ToString(),
                canSettle: () => !MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsOverOrEnding && !Owner.Creature.IsDead);
            // The chosen identity stays fixed. Locked orbs skip settlement but still lose their values.
            // Absorb the actual post-settlement loss, including settlement-triggered strengthening.
            var loss = session.Orbs.LoseAll(selected, OrbScope.All, new(Id.ToString()));
            await LibrarianRuntime.Dispatch(session, context, loss);
            _absorbed = checked(_absorbed + loss.ActualAmount);
            DynamicVars.Damage.BaseValue = checked((IsUpgraded ? 8 : 5) + _absorbed);
        }
        Owner.PlayerCombatState?.RecalculateCardValues();
        if (play.Target.IsAlive && !MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsOverOrEnding)
            await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, play).Targeting(play.Target)
                .WithHitFx("vfx/vfx_attack_slash").Execute(context);
    }
}

// Catalog 67: runtime records the last successfully completed play from a pre-play clone.
public sealed class Transcribe() : LibrarianCard(1, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    protected override bool IsPlayable => IsMutable && Owner is { } player
        && LibrarianCrossCharacter040.LastPlayed(player) is not null;
    protected override void AddExtraArgsToDescription(LocString description)
    {
        base.AddExtraArgsToDescription(description);
        description.Add("MissingCombatHistory", IsMutable && CombatState is not null && !IsPlayable);
    }
    protected override void OnUpgrade() => AddKeyword(CardKeyword.Retain);
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var snapshot = LibrarianCrossCharacter040.LastPlayed(Owner);
        if (snapshot is null) return;
        var handCopy = snapshot.CreateClone();
        handCopy.AddKeyword(CardKeyword.Ethereal);
        handCopy.AddKeyword(CardKeyword.Exhaust);
        var bottomCopy = snapshot.CreateClone();
        bottomCopy.AddKeyword(CardKeyword.Ethereal);
        bottomCopy.AddKeyword(CardKeyword.Exhaust);
        await CardPileCmd.AddGeneratedCardToCombat(handCopy, PileType.Hand, Owner);
        CardCmd.PreviewCardPileAdd(await CardPileCmd.AddGeneratedCardToCombat(bottomCopy, PileType.Draw, Owner, CardPilePosition.Bottom), 2.2f);
    }
}

/// <summary>v0.3.0 approved revision, catalog 69. Stable model ID retained.</summary>
public sealed class FireInscription() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CalculationBaseVar(10m), new ExtraDamageVar(4m), new CalculatedDamageVar(ValueProp.Move).WithMultiplier((card, _) => PreviewSession(card)?.Orbs.SettlementsThisCombat ?? 0)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await Hit(context, play, Amount("CalculationBase") + Session.Orbs.SettlementsThisCombat * Amount("ExtraDamage"));
    }
    protected override void OnUpgrade()
    {
        DynamicVars.ExtraDamage.UpgradeValueBy(2m);
    }
    protected override void AfterDowngraded() { DynamicVars.RecalculateForUpgradeOrEnchant(); DynamicVars.FinalizeUpgrade(); }
}

// Catalog 74 / D20: saved permanent increment; upgrade affects future increments only.
public sealed class ImmortalSpark() : LibrarianCard(1, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    private int _permanentIncrease;
    [SavedProperty]
    public int PermanentIncrease
    {
        get => _permanentIncrease;
        set { AssertMutable(); _permanentIncrease = value; DynamicVars["Fire"].BaseValue = checked(1 + value); }
    }
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new IntVar("Fire", 1m), new IntVar("Increase", 4m)];
    protected override void OnUpgrade() => DynamicVars["Increase"].UpgradeValueBy(2m);
    protected override void AfterDowngraded() => DynamicVars["Fire"].BaseValue = checked(1 + PermanentIncrease);
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var session = LibrarianRuntime.Get(Owner);
        await LibrarianRuntime.Dispatch(session, context, session.Orbs.Gain(OrbKind.Fire, DynamicVars["Fire"].IntValue, new(Id.ToString())));
        int increase = DynamicVars["Increase"].IntValue;
        PermanentIncrease = checked(PermanentIncrease + increase);
        if (CloneOf is null && DeckVersion is ImmortalSpark original && !ReferenceEquals(original, this))
            original.PermanentIncrease = checked(original.PermanentIncrease + increase);
        Owner.PlayerCombatState?.RecalculateCardValues();
    }
}
