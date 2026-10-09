using Librarian.Core;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace Librarian.LibrarianCode.Potions;

public sealed class KindlingPotion : LibrarianPotion
{
    public override PotionRarity Rarity => PotionRarity.Common;
    public override PotionUsage Usage => PotionUsage.CombatOnly;
    public override TargetType TargetType => TargetType.Self;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Fire", 6m)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [LibrarianHoverTips.Tip("FIRE")];
    protected override Task OnUse(PlayerChoiceContext context, Creature? target)
    {
        var session = LibrarianRuntime.Get(Owner);
        return LibrarianRuntime.Dispatch(session, context, session.Orbs.Gain(OrbKind.Fire,
            checked((int)DynamicVars["Fire"].BaseValue), new(Id.ToString())));
    }
}

public sealed class ClarityPotion : LibrarianPotion
{
    public override PotionRarity Rarity => PotionRarity.Uncommon;
    public override PotionUsage Usage => PotionUsage.CombatOnly;
    public override TargetType TargetType => TargetType.Self;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Waves", 9m)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [LibrarianHoverTips.Tip("WAVES")];
    protected override Task OnUse(PlayerChoiceContext context, Creature? target)
    {
        var session = LibrarianRuntime.Get(Owner);
        session.Waves.Add(checked((int)DynamicVars["Waves"].BaseValue));
        LibrarianRuntime.VerifyBlock(session);
        return Task.CompletedTask;
    }
}

public sealed class FluidForbiddenFruit : LibrarianPotion
{
    public override PotionRarity Rarity => PotionRarity.Rare;
    public override PotionUsage Usage => PotionUsage.CombatOnly;
    public override TargetType TargetType => TargetType.Self;
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [LibrarianHoverTips.Tip("SETTLE")];
    protected override async Task OnUse(PlayerChoiceContext context, Creature? target)
    {
        var session = LibrarianRuntime.Get(Owner);
        var ordered = session.Orbs.Positions.ToArray();
        // Clear every lock before the first settlement, including permanent locks.
        foreach (var kind in ordered)
            await LibrarianRuntime.Dispatch(session, context, session.Orbs.Unlock(kind, new(Id.ToString())));
        foreach (var kind in ordered)
            await session.Orbs.SettleImmediatelyAsync(OrbSelector.Named(kind, OrbScope.All), 1,
                request => LibrarianRuntime.Settle(session, context, request), Id.ToString(),
                canSettle: () => !CombatManager.Instance.IsOverOrEnding && !Owner.Creature.IsDead);
    }
}
