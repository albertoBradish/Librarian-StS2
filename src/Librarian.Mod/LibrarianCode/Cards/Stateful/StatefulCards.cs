using Librarian.Core;
using Librarian.Mechanics;
using Librarian.LibrarianCode.Character;
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
public sealed class Calibrate() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(0, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    protected override bool HasEnergyCostX => true;
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Extra", 0m)];
    protected override Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        int x = ResolveEnergyXValue();
        int count = checked(x + Amount("Extra"));
        var session = LibrarianRuntime.For(Owner.Creature);
        return session is null ? DrawAndDiscount(context) : session.AfterCleanupOrNow(context, DrawAndDiscount);

        async Task DrawAndDiscount(PlayerChoiceContext ctx)
        {
            var drawn = (await CardPileCmd.Draw(ctx, count, Owner)).ToArray();
            foreach (var card in drawn) card.EnergyCost.AddThisTurn(-x);
        }
    }
    protected override void OnUpgrade() => DynamicVars["Extra"].UpgradeValueBy(1m);
}

// Catalog 44. The existing starter-card visuals serve as the two choice previews only.
public sealed class DrawBranch() : LibrarianCard(1, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override void OnUpgrade() => RemoveKeyword(CardKeyword.Exhaust);
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var session = LibrarianRuntime.Get(Owner);
        await session.Orbs.SettleImmediatelyAsync(OrbSelector.Named(OrbKind.Growth, OrbScope.All), 1,
            request => LibrarianRuntime.Settle(session, context, request), Id.ToString(),
            canSettle: () => !MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsOverOrEnding && !Owner.Creature.IsDead);
        await LibrarianRuntime.Dispatch(session, context, session.Orbs.RedistributeValues(new(Id.ToString())));
    }
}

// v0.5.2: snapshot Growth once, use it for base damage and temporary Strength loss.
public sealed class Bookworm() : Librarian.LibrarianCode.Cards.OrbAdvancedBasics.OrbAdvancedCard(1, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        CalculatedDamage((card, _) => PreviewSession(card)?.Orbs.Value(OrbKind.Growth) ?? 0);
    protected override IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip> AdditionalHoverTips =>
        [MegaCrit.Sts2.Core.HoverTips.HoverTipFactory.FromPower<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>()];
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        ArgumentNullException.ThrowIfNull(play.Target);
        int growth = Session.Orbs.Value(OrbKind.Growth);
        await DamageCmd.Attack(growth).FromLibrarianCard(this, play).Targeting(play.Target)
            .WithHitFx("vfx/vfx_bite").Execute(context);
        if (growth > 0 && play.Target.IsAlive)
            await PowerCmd.Apply<Librarian.LibrarianCode.Powers.Implemented.BookwormPower>(
                context, play.Target, growth, Owner.Creature, this);
    }
}

// V1.4.2: consume the actual Growth value and convert whole groups into native Regeneration.
public sealed class NourishingLife() : LibrarianCard(1, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Threshold", 8m)];
    protected override IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip> AdditionalHoverTips =>
        [MegaCrit.Sts2.Core.HoverTips.HoverTipFactory.FromPower<MegaCrit.Sts2.Core.Models.Powers.RegenPower>()];
    protected override void OnUpgrade() => DynamicVars["Threshold"].UpgradeValueBy(-2m);
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var session = LibrarianRuntime.Get(Owner);
        var loss = session.Orbs.LoseAll(OrbKind.Growth, OrbScope.All, new(Id.ToString()));
        await LibrarianRuntime.Dispatch(session, context, loss);
        int regen = loss.ActualAmount / DynamicVars["Threshold"].IntValue;
        if (regen > 0)
            await PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.RegenPower>(context, Owner.Creature, regen, Owner.Creature, this);
    }
}

// Catalog 67: runtime records the last successfully completed play from a pre-play clone.
public sealed class Transcribe() : LibrarianCard(1, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip> AdditionalHoverTips =>
        [MegaCrit.Sts2.Core.HoverTips.HoverTipFactory.FromKeyword(CardKeyword.Ethereal),
         MegaCrit.Sts2.Core.HoverTips.HoverTipFactory.FromKeyword(CardKeyword.Exhaust)];
    protected override bool IsPlayable => IsMutable && Owner is { } player
        && LibrarianCrossCharacter040.LastPlayed(player) is not null;
    protected override void AddExtraArgsToDescription(LocString description)
    {
        base.AddExtraArgsToDescription(description);
        description.Add("MissingCombatHistory", IsMutable && CombatState is not null && !IsPlayable);
    }
    protected override void OnUpgrade() => RemoveKeyword(CardKeyword.Exhaust);
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var snapshot = LibrarianCrossCharacter040.LastPlayed(Owner);
        if (snapshot is null) return;
        var handCopy = CopyUnenchantedBody(snapshot);
        handCopy.AddKeyword(CardKeyword.Ethereal);
        handCopy.AddKeyword(CardKeyword.Exhaust);
        var bottomCopy = CopyUnenchantedBody(snapshot);
        bottomCopy.AddKeyword(CardKeyword.Ethereal);
        bottomCopy.AddKeyword(CardKeyword.Exhaust);
        await CardPileCmd.AddGeneratedCardToCombat(handCopy, PileType.Hand, Owner);
        CardCmd.PreviewCardPileAdd(await CardPileCmd.AddGeneratedCardToCombat(bottomCopy, PileType.Draw, Owner, CardPilePosition.Bottom), 2.2f);
    }

    private CardModel CopyUnenchantedBody(CardModel snapshot)
    {
        // Reconstruct saved card properties and upgrades without ever applying the
        // enchantment. Clearing a cloned enchantment leaves its stat mutations behind.
        var saved = snapshot.ToSerializable();
        saved.Enchantment = null;
        var copy = CardModel.FromSerializable(saved);
        CombatState!.AddCard(copy, Owner);
        return copy;
    }
}

/// <summary>v0.3.0 approved revision, catalog 69. Stable model ID retained.</summary>
public sealed class FireInscription() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CalculationBaseVar(8m), new ExtraDamageVar(4m), new CalculatedDamageVar(ValueProp.Move).WithMultiplier((card, _) => PreviewSession(card)?.Orbs.SettlementsThisCombat ?? 0)];
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
    protected override IEnumerable<DynamicVar> CanonicalVars => [new IntVar("Fire", 1m), new IntVar("Increase", 2m)];
    protected override void OnUpgrade() => DynamicVars["Increase"].UpgradeValueBy(2m);
    protected override void AfterDowngraded() => DynamicVars["Fire"].BaseValue = checked(1 + PermanentIncrease);
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        if (Owner.Character is LibrarianCharacter)
        {
            var session = LibrarianRuntime.Get(Owner);
            await LibrarianRuntime.Dispatch(session, context, session.Orbs.Gain(OrbKind.Fire, DynamicVars["Fire"].IntValue, new(Id.ToString())));
        }
        int increase = DynamicVars["Increase"].IntValue;
        PermanentIncrease = checked(PermanentIncrease + increase);
        if (CloneOf is null && DeckVersion is ImmortalSpark original && !ReferenceEquals(original, this))
            original.PermanentIncrease = checked(original.PermanentIncrease + increase);
        Owner.PlayerCombatState?.RecalculateCardValues();
    }
}
