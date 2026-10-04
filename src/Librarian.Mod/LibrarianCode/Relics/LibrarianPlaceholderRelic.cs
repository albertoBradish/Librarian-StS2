using HarmonyLib;
using System.Runtime.CompilerServices;
using Librarian.Core;
using Librarian.Mechanics;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.LibrarianCode.Relics;

/// <summary>Legacy class names retain save compatibility; each relic has original artwork.</summary>
public abstract class LibrarianPlaceholderRelic : LibrarianRelic
{
    private string Artwork => GetType().Name switch
    {
        nameof(RestoredSpellScroll) => "restored_scroll",
        nameof(LibrarianCommonPlaceholder) => "fire_starter",
        nameof(LibrarianUncommonPlaceholderOne) => "undying_ember",
        nameof(LibrarianUncommonPlaceholderTwo) => "mite_remover",
        nameof(LibrarianRarePlaceholderOne) => "unstable_spell",
        nameof(LibrarianRarePlaceholderTwo) => "annoying_vines",
        nameof(LibrarianRarePlaceholderThree) => "memory_scroll",
        nameof(LibrarianShopPlaceholder) => "perfect_badge",
        _ => throw new InvalidOperationException("Missing relic artwork mapping: " + GetType().Name)
    };
    public override string PackedIconPath => "res://Librarian/images/relics/v0.4.0/" + Artwork + ".png";
    protected override string PackedIconOutlinePath => "res://Librarian/images/relics/v0.4.0/" + Artwork + "_outline.png";
    protected override string BigIconPath => "res://Librarian/images/relics/v0.4.0/big/" + Artwork + ".png";
    public override bool IsStackable => false;
}

public sealed class LibrarianCommonPlaceholder : LibrarianPlaceholderRelic
{
    private bool _triggered;
    public override RelicRarity Rarity => RelicRarity.Common;
    public override Task BeforeCombatStart() { _triggered = false; return Task.CompletedTask; }
    public override async Task BeforeSideTurnStart(PlayerChoiceContext context, CombatSide side,
        IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (_triggered || !participants.Contains(Owner.Creature)) return;
        _triggered = true;
        Flash();
        var session = LibrarianRuntime.Get(Owner);
        await LibrarianRuntime.Dispatch(session, context, session.Orbs.Gain(OrbKind.Fire, 7, new(Id.ToString())));
    }
}

public sealed class LibrarianUncommonPlaceholderOne : LibrarianPlaceholderRelic, IOrbSettlementListener
{
    public override RelicRarity Rarity => RelicRarity.Uncommon;
    public async Task AfterOrbSettlement(LibrarianSession session, PlayerChoiceContext context,
        SettlementRequest request, OrbKind? growthHigherTarget)
    {
        if (request.Orb != OrbKind.Fire) return;
        Flash();
        await LibrarianRuntime.Dispatch(session, context, session.Orbs.Strengthen(OrbKind.Fire, 2, OrbScope.All, new(Id.ToString())));
    }
}

public sealed class LibrarianUncommonPlaceholderTwo : LibrarianPlaceholderRelic
{
    private bool _used;
    public override RelicRarity Rarity => RelicRarity.Uncommon;
    protected override IEnumerable<IHoverTip> ExtraHoverTips => base.ExtraHoverTips.Append(HoverTipFactory.FromKeyword(CardKeyword.Exhaust));
    public override Task BeforeCombatStart() { _used = false; Status = RelicStatus.Normal; return Task.CompletedTask; }
    internal bool Qualifies(CardModel card) => !IsMelted && !_used && card.Owner == Owner && card.Keywords.Contains(CardKeyword.Exhaust);
    internal void Consume() { _used = true; Status = RelicStatus.Disabled; Flash(); Owner.PlayerCombatState?.RecalculateCardValues(); }
    public override bool TryModifyEnergyCostInCombatLate(CardModel card, decimal originalCost, out decimal modifiedCost)
    {
        modifiedCost = Qualifies(card) ? 0m : originalCost;
        return Qualifies(card);
    }
    public override bool TryModifyStarCost(CardModel card, decimal originalCost, out decimal modifiedCost)
    {
        modifiedCost = Qualifies(card) ? 0m : originalCost;
        return Qualifies(card);
    }
    public override Task BeforeCardPlayed(CardPlay play)
    {
        // Automatic plays also count as the first Exhaust card, even when already free.
        if (Qualifies(play.Card)) Consume();
        return Task.CompletedTask;
    }
}

// Native X costs bypass global modifiers. Intercept the whole resource transaction:
// otherwise consuming the relic during SpendEnergy lets SpendStars charge the same card.
[HarmonyPatch(typeof(CardModel), nameof(CardModel.SpendResources))]
internal static class LibrarianExhaustRelicEnergy
{
    internal sealed record FreePlay(int EnergyValue, int StarValue);
    internal static readonly ConditionalWeakTable<CardModel, FreePlay> Pending = new();
    internal static LibrarianUncommonPlaceholderTwo? Find(CardModel card)
        => card.Owner.Relics.OfType<LibrarianUncommonPlaceholderTwo>().FirstOrDefault(r => r.Qualifies(card));
    [HarmonyPrefix]
    private static bool Prefix(CardModel __instance, ref Task<(int, int)> __result)
    {
        if (Find(__instance) is null) return true;
        int energyValue = __instance.EnergyCost.CostsX ? __instance.EnergyCost.GetAmountToSpend() : 0;
        int starValue = __instance.HasStarCostX ? __instance.Owner.PlayerCombatState!.Stars : 0;
        if (__instance.EnergyCost.CostsX) __instance.EnergyCost.CapturedXValue = energyValue;
        __instance.LastStarsSpent = starValue;
        Pending.Remove(__instance);
        Pending.Add(__instance, new(energyValue, starValue));
        __result = FreeResources(__instance);
        return false;
    }
    private static async Task<(int, int)> FreeResources(CardModel card)
    {
        await Hook.AfterEnergySpent(card.CombatState!, card, 0);
        return (0, 0);
    }
}

public sealed class LibrarianRarePlaceholderOne : LibrarianPlaceholderRelic, IOrbEndTurnListener
{
    public override RelicRarity Rarity => RelicRarity.Rare;
    public async Task BeforeOrbSettlements(LibrarianSession session, PlayerChoiceContext context)
    {
        var candidates = session.Orbs.Snapshot().Orbs.Where(o => !o.IsLocked && !o.IsActivated).ToArray();
        if (candidates.Length == 0) return;
        var kind = candidates.Length == 1 ? candidates[0].Kind : candidates[new LibrarianRuntime.GameOrbRandom(Owner).NextInt(candidates.Length)].Kind;
        Flash();
        await LibrarianRuntime.Dispatch(session, context, session.Orbs.Activate(kind, OrbScope.All, new(Id.ToString())));
    }
}

public sealed class LibrarianRarePlaceholderTwo : LibrarianPlaceholderRelic, IOrbSettlementListener
{
    public override RelicRarity Rarity => RelicRarity.Rare;
    public async Task AfterOrbSettlement(LibrarianSession session, PlayerChoiceContext context,
        SettlementRequest request, OrbKind? growthHigherTarget)
    {
        if (request.Orb != OrbKind.Growth || CombatManager.Instance.IsOverOrEnding || Owner.Creature.IsDead) return;
        var enemies = Owner.Creature.CombatState?.GetOpponentsOf(Owner.Creature).Where(c => c.IsHittable).ToArray();
        if (enemies is not { Length: > 0 }) return;
        var target = Owner.RunState.Rng.CombatTargets.NextItem(enemies);
        if (target is null) return;
        Flash();
        await CreatureCmd.Damage(context, target, 7m, ValueProp.Unpowered, Owner.Creature);
    }
}

public sealed class LibrarianRarePlaceholderThree : LibrarianPlaceholderRelic
{
    public override RelicRarity Rarity => RelicRarity.Rare;
    public override async Task BeforeSideTurnStart(PlayerChoiceContext context, CombatSide side,
        IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (!participants.Contains(Owner.Creature)) return;
        var session = LibrarianRuntime.Get(Owner);
        if (session.PreviousTurnsLastSettledOrb is not { } kind || session.Orbs.Snapshot()[kind].IsLocked) return;
        Flash();
        await LibrarianRuntime.Dispatch(session, context, session.Orbs.Activate(kind, OrbScope.All, new(Id.ToString())));
    }
}

public sealed class LibrarianShopPlaceholder : LibrarianPlaceholderRelic, IOrbEndTurnListener
{
    public override RelicRarity Rarity => RelicRarity.Shop;
    public async Task BeforeOrbSettlements(LibrarianSession session, PlayerChoiceContext context)
    {
        var snapshot = session.Orbs.Snapshot();
        if (snapshot.Orbs.Any(o => !o.IsActivated)) return;
        Flash();
        await session.Orbs.SettleImmediatelyAsync(OrbSelector.Named(snapshot.Foreground, OrbScope.All), 1,
                request => LibrarianRuntime.Settle(session, context, request), Id.ToString(),
                canSettle: () => !CombatManager.Instance.IsOverOrEnding && !Owner.Creature.IsDead);
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.OnPlayWrapper))]
internal static class LibrarianExhaustRelicResourceValue
{
    [HarmonyPrefix]
    private static void Prefix(CardModel __instance, ref ResourceInfo resources)
    {
        if (!LibrarianExhaustRelicEnergy.Pending.TryGetValue(__instance, out var pending))
        {
            if (LibrarianExhaustRelicEnergy.Find(__instance) is null) return;
            // AutoPlay has already captured X. Retain its effect value while fixed costs are free.
            pending = new(__instance.EnergyCost.CostsX ? resources.EnergyValue : 0,
                __instance.HasStarCostX ? resources.StarValue : 0);
        }
        LibrarianExhaustRelicEnergy.Pending.Remove(__instance);
        resources = resources with { EnergySpent = 0, EnergyValue = pending.EnergyValue,
            StarsSpent = 0, StarValue = pending.StarValue };
    }
}

