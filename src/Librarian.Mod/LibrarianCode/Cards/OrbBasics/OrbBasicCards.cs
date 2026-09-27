using MegaCrit.Sts2.Core.Commands;
using Librarian.Mechanics;
using Librarian.Core;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.LibrarianCode.Cards.OrbBasics;

/// <summary>v0.3.0 approved revision, catalog 8. Stable model ID retained.</summary>
public sealed class Reignite() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(1, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await Settle(context, Session, OrbKind.Fire);
    }
    protected override void OnUpgrade()
    {
        AddKeyword(CardKeyword.Retain);
    }
}

/// <summary>v0.3.0 approved revision, catalog 9. Stable model ID retained.</summary>
public sealed class FlameStrike() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(7m, ValueProp.Move), new DynamicVar("Fire", 3m)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await Hit(context, play, Amount("Damage"));
        await Gain(context, Session, OrbKind.Fire, Amount("Fire"));
    }
    protected override void OnUpgrade()
    {
        DynamicVars["Damage"].UpgradeValueBy(3m);
    }
}

/// <summary>v0.3.0 approved revision, catalog 10. Stable model ID retained.</summary>
public sealed class WaveStrike() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(6m, ValueProp.Move), new DynamicVar("Tide", 2m)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await Hit(context, play, Amount("Damage"));
        await Gain(context, Session, OrbKind.Tide, Amount("Tide"));
    }
    protected override void OnUpgrade()
    {
        DynamicVars["Damage"].UpgradeValueBy(3m);
    }
}

/// <summary>v0.3.0 approved revision, catalog 11. Stable model ID retained.</summary>
public sealed class ScatteredFlames() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CalculationBaseVar(8m), new ExtraDamageVar(5m), new CalculatedDamageVar(ValueProp.Move).WithMultiplier((card, _) => PreviewLockedCount(card))];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await Hit(context, play, Amount("CalculationBase") + LockedCount * Amount("ExtraDamage"));
    }
    protected override void OnUpgrade()
    {
        DynamicVars.ExtraDamage.UpgradeValueBy(2m);
    }
    protected override void AfterDowngraded() { DynamicVars.RecalculateForUpgradeOrEnchant(); DynamicVars.FinalizeUpgrade(); }
}

/// <summary>Catalog 15. The damage is fixed, not conditional on how much Fire was available.</summary>
public sealed class AshenBlow() : OrbBasicsCard(3, CardType.Attack, TargetType.AnyEnemy)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(31m, ValueProp.Move)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await LoseAsync(context, OrbKind.Fire);
        await HitAsync(context, play, DynamicVars.Damage.BaseValue);
    }
    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(6m);
}

/// <summary>v0.3.0 approved revision, catalog 16. Stable model ID retained.</summary>
public sealed class Springwater() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(1, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await Settle(context, Session, OrbKind.Tide);
    }
    protected override void OnUpgrade()
    {
        AddKeyword(CardKeyword.Retain);
    }
}

/// <summary>v0.3.0 approved revision, catalog 17. Stable model ID retained.</summary>
public sealed class BurningPages() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(1, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Fire", 8m)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await Gain(context, Session, OrbKind.Fire, Amount("Fire"));
        await Lock(context, OrbKind.Tide, 1);
    }
    protected override void OnUpgrade()
    {
        DynamicVars["Fire"].UpgradeValueBy(3m);
    }
}

/// <summary>v0.3.0 approved revision, catalog 18. Stable model ID retained.</summary>
public sealed class QuietEmbers() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(1, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(10m, ValueProp.Move)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await Block(play, Amount("Block"));
        await Extinguish(context, Session, OrbKind.Fire, OrbScope.All);
    }
    protected override void OnUpgrade()
    {
        DynamicVars["Block"].UpgradeValueBy(3m);
    }
}

/// <summary>v0.3.0 approved revision, catalog 19. Stable model ID retained.</summary>
public sealed class ColdFlame() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(1, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Tide", 6m)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await Gain(context, Session, OrbKind.Tide, Amount("Tide"));
        await Lock(context, OrbKind.Fire, 1);
    }
    protected override void OnUpgrade()
    {
        DynamicVars["Tide"].UpgradeValueBy(2m);
    }
}

/// <summary>V0.4.1 replacement for ColdFlame. ColdFlame remains loadable for old saves.</summary>
public sealed class ReadBackward() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(1, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    protected override Task OnPlay(PlayerChoiceContext context, CardPlay play)
        => LibrarianBottomPlay041.ReadBackwardAsync(context, Owner);

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}

/// <summary>v0.3.0 approved revision, catalog 20. Stable model ID retained.</summary>
public sealed class Renewal() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(1, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Growth", 8m)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await Gain(context, Session, OrbKind.Growth, Amount("Growth"));
        await Lock(context, OrbKind.Fire, 1);
    }
    protected override void OnUpgrade()
    {
        DynamicVars["Growth"].UpgradeValueBy(4m);
    }
}

/// <summary>v0.3.0 approved revision, catalog 21. Stable model ID retained.</summary>
public sealed class SproutingSeed() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(7m, ValueProp.Move), new DynamicVar("Growth", 8m), new DynamicVar("LockTurns", 2m)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        int lockTurns = Amount("LockTurns");
        await Block(play, Amount("Block"));
        await Gain(context, Session, OrbKind.Growth, Amount("Growth"));
        await Lock(context, OrbKind.Fire, lockTurns);
    }
    protected override void OnUpgrade()
    {
        DynamicVars["Growth"].UpgradeValueBy(2m);
        DynamicVars["LockTurns"].UpgradeValueBy(-1m);
    }
}

/// <summary>v0.3.0 approved revision, catalog 22. Stable model ID retained.</summary>
public sealed class Nourish() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(3)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        if (Owner.Character is Librarian.LibrarianCode.Character.LibrarianCharacter)
            await Draw(context, Amount("Cards"));
        else
            await CardPileCmd.Draw(context, Amount("Cards"), Owner);
        var discarded = (await CardSelectCmd.FromHand(context, Owner,
            new MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs(new MegaCrit.Sts2.Core.Localization.LocString("card_selection", "LIBRARIAN-TO_DRAW_BOTTOM"), 1), null, this)).FirstOrDefault();
        if (discarded is not null) CardCmd.PreviewCardPileAdd(await CardPileCmd.Add(discarded, PileType.Draw, CardPilePosition.Bottom), 2.2f);
    }
    protected override void OnUpgrade()
    {
        DynamicVars["Cards"].UpgradeValueBy(1m);
    }
}

/// <summary>Catalog 23. Gain callbacks complete before drawing.</summary>
public sealed class ChannelFlow() : OrbBasicsCard(1, CardType.Skill, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Tide", 1m), new CardsVar(1)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await GainAsync(context, play, OrbKind.Tide, "Tide");
        await DrawAsync(context);
    }
    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(1m);
}

/// <summary>v0.3.0 approved revision, catalog 24. Stable model ID retained.</summary>
public sealed class DryBranchSearch() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(1, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(2)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await Draw(context, Amount("Cards"));
        await Lock(context, OrbKind.Growth, 1);
    }
    protected override void OnUpgrade()
    {
        DynamicVars["Cards"].UpgradeValueBy(1m);
    }
}

/// <summary>Catalog 25.</summary>
public sealed class VineShield() : OrbBasicsCard(2, CardType.Skill, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(11m, ValueProp.Move), new DynamicVar("Growth", 4m)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await BlockAsync(play);
        await GainAsync(context, play, OrbKind.Growth, "Growth");
    }
    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(3m);
        DynamicVars["Growth"].UpgradeValueBy(2m);
    }
}

/// <summary>v0.3.0 approved revision, catalog 26. Stable model ID retained.</summary>
public sealed class BurnTheRiver() : Librarian.LibrarianCode.Cards.OrbUtility.OrbUtilityCard(2, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Fire", 6m)];
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await Gain(context, Session, OrbKind.Fire, Amount("Fire"));
        await Settle(context, Session, OrbKind.Fire);
    }
    protected override void OnUpgrade() => DynamicVars["Fire"].UpgradeValueBy(3m);
}
