using System.Reflection;
using Librarian.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Cards;

namespace Librarian.Mechanics;

/// <summary>Read-only adapters for native turn-end Block. Never execute live hooks or orb getters.</summary>
internal static class LibrarianNativeBlockPreview
{
    private static readonly string[] EndHooks = ["BeforeSideTurnEndVeryEarly", "BeforeSideTurnEndEarly",
        "BeforeSideTurnEnd", "AfterSideTurnEnd", "AfterSideTurnEndLate", "BeforeFlush", "BeforeFlushLate", "AfterFlush"];
    private static readonly HashSet<Type> PassiveModifiers = [typeof(DexterityPower), typeof(FrailPower),
        typeof(NoBlockPower), typeof(FastenPower), typeof(UnmovablePower), typeof(ShadowmeldPower)];
    private static readonly HashSet<Type> SupportedEnd = [typeof(PlatingPower), typeof(Orichalcum),
        typeof(FakeOrichalcum), typeof(CloakClasp), typeof(RippleBasin), typeof(FrailPower),
        typeof(NoBlockPower), typeof(ShadowmeldPower), typeof(WeakPower), typeof(VulnerablePower),
        typeof(TemporaryStrengthPower), typeof(TemporaryDexterityPower), typeof(PaelsTears), typeof(ConstrictPower), typeof(IntangiblePower)];

    private static readonly Dictionary<(Type Type, string Method), bool> OverrideCache = [];
    internal static bool Overrides(AbstractModel model, string name)
    {
        var key = (model.GetType(), name);
        if (!OverrideCache.TryGetValue(key, out bool value))
            OverrideCache[key] = value = key.Item1.GetMethods(BindingFlags.Instance | BindingFlags.Public).Any(m => m.Name == name
                && m.GetBaseDefinition().DeclaringType == typeof(AbstractModel) && m.DeclaringType != typeof(AbstractModel));
        return value;
    }

    internal static string? Uncertainty(LibrarianSession session, bool settlementDamage = false)
    {
        var player = session.Player;
        var combat = player.Creature.CombatState!;
        // Another owner's queued effects can change this player's Block before the enemy side.
        if (combat.Players.Count > 1) return "multiplayer";
        var hand = player.PlayerCombatState!.Hand.Cards;
        bool exhausts = hand.Any(c => c.Keywords.Contains(CardKeyword.Ethereal) && !c.HasTurnEndInHandEffect);
        if (hand.Any(c => c.HasTurnEndInHandEffect && c is not Burn and not Decay and not Doubt and not Shame)) return "hand";
        bool takesDamage = settlementDamage || hand.Any(c => c is Burn or Decay) || player.Creature.GetPower<ConstrictPower>() is not null;
        foreach (var model in combat.IterateHookListeners())
        {
            var type = model.GetType();
            if (model is RelicModel { IsMelted: true }) continue;
            if (model is not LibrarianSpellVisualHooks040 && !PassiveModifiers.Contains(type)
                && type.FullName != "MegaCrit.Sts2.Core.Models.Singleton.MultiplayerScalingModel"
                && new[] { "ModifyBlockAdditive", "ModifyBlockMultiplicative", "BeforeBlockGained", "AfterBlockGained", "AfterModifyingBlockAmount" }.Any(n => Overrides(model, n)))
                return "modifiers";
            if (Overrides(model, "AfterAutoPostPlayPhaseEntered") && (model is not IAmInvincible invincible
                || invincible.Owner == player && player.PlayerCombatState.DrawPile.Cards.FirstOrDefault() == invincible)) return "bottom";
            if (takesDamage && model is not LibrarianSpellVisualHooks040 and not IntangiblePower
                && new[] { "ModifyDamageAdditive", "ModifyDamageMultiplicative", "ModifyDamageCap",
                    "ModifyHpLostBeforeOsty", "ModifyHpLostBeforeOstyLate", "ModifyHpLostAfterOsty", "ModifyHpLostAfterOstyLate",
                    "ModifyUnblockedDamageTarget", "BeforeDamageReceived", "AfterDamageGiven", "AfterDamageReceived", "AfterDamageReceivedLate",
                    "AfterCurrentHpChanged", "AfterBlockBroken", "AfterModifyingDamageAmount", "AfterModifyingHpLostBeforeOsty", "AfterModifyingHpLostAfterOsty" }.Any(n => Overrides(model, n))) return "modifiers";
            if (exhausts && (Overrides(model, "ShouldEtherealTrigger") && model is not LibrarianCombatHooks
                || Overrides(model, "AfterCardExhausted") && model is not FeelNoPainPower and not LibrarianSpellVisualHooks040)) return "hand";
            if (type.Assembly == typeof(LibrarianRuntime).Assembly) continue;
            if (!SupportedEnd.Contains(type) && EndHooks.Any(n => Overrides(model, n))) return "effects";
            if (player.PlayerCombatState.OrbQueue.Orbs.Count > 0 && type != typeof(GoldPlatedCables)
                && type != typeof(FocusPower) && new[] { "ModifyOrbValue", "ModifyOrbPassiveTriggerCounts", "AfterModifyingOrbPassiveTriggerCount" }.Any(n => Overrides(model, n)))
                return "effects";
        }
        if (player.PlayerCombatState.OrbQueue.Orbs.Any(o => o.GetType() != typeof(FrostOrb))) return "effects";
        return null;
    }

    internal static EndTurnBlockTotals Resolve(LibrarianSession session, EndTurnBlockProjection projection, Action resolveOrbs)
    {
        var player = session.Player;
        var creature = player.Creature;
        var models = creature.CombatState!.IterateHookListeners().ToArray();
        var total = new EndTurnBlockTotals(creature.Block);
        decimal multiplier = creature.GetPower<ShadowmeldPower>() is { } shadow ? (decimal)Math.Pow(2, shadow.Amount) : 1;
        void Gain(decimal amount) => total.Gain(Math.Max(0, amount * multiplier));
        int hp = creature.CurrentHp;
        void Damage(int amount)
        {
            if (creature.GetPower<IntangiblePower>() is not null) amount = Math.Min(amount, 1);
            hp -= Math.Max(0, amount - total.FinalBlock);
            total.Lose(amount);
            if (hp <= 0) throw new BlockPreviewUncertainException("combat");
        }
        projection.TidalBlockGained = amount => Gain(amount);
        projection.OrdinaryBlockGained = amount => Gain(amount);
        projection.BlockExpired = total.Lose;
        bool emptyAtVeryEarly = creature.Block == 0;
        // Native very-early snapshots precede all early gains, particularly Orichalcum vs Plating.
        foreach (var model in models)
            if (model is PlatingPower plating && plating.Owner == creature) Gain(plating.Amount);
        bool coreResolved = false;
        foreach (var model in models)
        {
            if (model is LibrarianCombatHooks)
            {
                resolveOrbs(); coreResolved = true;
            }
            if (model is not RelicModel relic || relic.IsMelted || relic.Owner != player) continue;
            if (relic is Orichalcum or FakeOrichalcum && emptyAtVeryEarly) Gain(relic.DynamicVars.Block.BaseValue);
            if (relic is CloakClasp) Gain((int)(player.PlayerCombatState!.Hand.Cards.Count * relic.DynamicVars.Block.BaseValue));
            if (relic is RippleBasin && !CombatManager.Instance.History.CardPlaysFinished.Any(e =>
                e.HappenedThisTurn(creature.CombatState) && e.CardPlay.Card.Owner == player && e.CardPlay.Card.Type == CardType.Attack))
                Gain(relic.DynamicVars.Block.BaseValue);
        }
        if (!coreResolved) throw new InvalidOperationException("Native turn-end projection requires the registered Librarian hook.");
        var nativeOrbs = player.PlayerCombatState!.OrbQueue.Orbs;
        for (int i = 0; i < nativeOrbs.Count; i++)
        {
            int count = i == 0 && player.Relics.Any(r => r is GoldPlatedCables && !r.IsMelted) ? 2 : 1;
            for (int j = 0; j < count; j++) Gain(Math.Max(0, 2 + (creature.GetPower<FocusPower>()?.Amount ?? 0)));
        }
        foreach (var card in player.PlayerCombatState.Hand.Cards)
            if (!card.HasTurnEndInHandEffect && card.Keywords.Contains(CardKeyword.Ethereal))
                Gain(creature.GetPower<FeelNoPainPower>()?.Amount ?? 0);
        foreach (var card in player.PlayerCombatState.Hand.Cards)
            if (card is Burn or Decay) Damage((int)card.DynamicVars.Damage.BaseValue);
        foreach (var model in models)
            if (model is ConstrictPower constrict && constrict.Owner == creature) Damage(constrict.Amount);
        return total;
    }
}

internal sealed class BlockPreviewUncertainException(string reason) : Exception
{
    internal string Reason { get; } = reason;
}
