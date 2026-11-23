using System.Reflection;
using HarmonyLib;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.OrbBasics;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.Mechanics;

/// <summary>
/// The independent printed effects of imported cards. A foreign owner has no Librarian
/// orbs: skip only the missing-orb operations, without constructing invisible orb state.
/// The normal native card-play pipeline still handles cost, powers, exhaust, and history.
/// </summary>
internal static class LibrarianForeignCardEffects040
{
    internal static async Task Play(CardModel card, PlayerChoiceContext context, CardPlay play)
    {
        string id = card.Id.Entry;
        decimal Value(string name) => card.DynamicVars[name].BaseValue;
        Task Block(decimal amount) => CreatureCmd.GainBlock(card.Owner.Creature, amount, ValueProp.Move, play);
        Task Draw(int count) => LibrarianRuntime.For(card.Owner.Creature) is { } session
            ? LibrarianRuntime.DrawOrDeferAsync(session, context, count)
            : CardPileCmd.Draw(context, count, card.Owner);
        async Task Hit(decimal damage, bool all = false, int count = 1)
        {
            var attack = DamageCmd.Attack(damage).FromLibrarianCard(card, play);
            if (all) attack.TargetingAllOpponents(card.CombatState!);
            else { ArgumentNullException.ThrowIfNull(play.Target); attack.Targeting(play.Target); }
            await attack.WithHitCount(count).WithHitFx("vfx/vfx_attack_slash").Execute(context);
        }
        switch (id)
        {
            case "LIBRARIAN-NEEDLE_FLURRY":
            case "LIBRARIAN-FLAME_STRIKE":
            case "LIBRARIAN-ASHEN_BLOW":
            case "LIBRARIAN-ROOTBIND":
            case "LIBRARIAN-BURN_ROOTS":
            case "LIBRARIAN-SEEDBURIAL_STRIKE":
            case "LIBRARIAN-OVERLOAD_BURN":
                await Hit(Value("Damage"));
                break;
            case "LIBRARIAN-RESIDUAL_WARMTH":
            {
                // A foreign owner has no Fire orb. Preserve only the printed
                // intent branch that grants Block; the non-attack branch is
                // intentionally a no-op rather than hidden orb state.
                bool attacks = play.Target?.Monster?.NextMove.Intents.Any(intent =>
                    intent.IntentType == MegaCrit.Sts2.Core.MonsterMoves.Intents.IntentType.Attack) == true;
                if (attacks) await Block(Value("Block"));
                break;
            }
            case "LIBRARIAN-SCATTERED_FLAMES":
                await Hit(Value("Damage"), count: 2);
                break;
            case "LIBRARIAN-GAP_NEEDLE":
                await Hit(Value("CalculationBase"));
                break;
            case "LIBRARIAN-EMBER_RECKONING":
                await Hit(Value("Damage"));
                await PowerCmd.Apply<StrengthPower>(context, card.Owner.Creature, Value("Strength"), card.Owner.Creature, card);
                break;
            case "LIBRARIAN-DAM_BREAK":
                await Block(Value("Block"));
                await PowerCmd.Apply<DexterityPower>(context, card.Owner.Creature, Value("Dexterity"), card.Owner.Creature, card);
                break;
            case "LIBRARIAN-FIRE_INSCRIPTION":
                // Shared settlements can record real history for a foreign recipient.
                await Hit(Value("CalculationBase") + (LibrarianRuntime.TryGet(card.Owner, out var history)
                    ? history!.Orbs.SettlementsThisCombat * Value("ExtraDamage") : 0));
                break;
            case "LIBRARIAN-TIDAL_STRIKE":
                await Hit(Value("CalculationBase") + (LibrarianRuntime.TryGet(card.Owner, out var tidal) ? tidal!.Waves.Amount : 0));
                break;
            case "LIBRARIAN-ZERO_SEARCH":
                await Draw(card.DynamicVars.CalculationBase.IntValue);
                break;
            case "LIBRARIAN-STEAM_BLAST":
                await Hit(Value("Damage"), all: true);
                break;
            case "LIBRARIAN-TIDAL_EROSION":
                await Hit(Value("Damage"), all: true);
                foreach (var enemy in card.CombatState!.GetOpponentsOf(card.Owner.Creature).Where(c => c.IsHittable).ToArray())
                    await PowerCmd.Apply<WeakPower>(context, enemy, Value("WeakPower"), card.Owner.Creature, card);
                break;
            case "LIBRARIAN-QUIET_EMBERS":
            case "LIBRARIAN-SPROUTING_SEED":
            case "LIBRARIAN-SPROUTING_BULWARK":
            case "LIBRARIAN-VINE_SHIELD":
            case "LIBRARIAN-SONG_OF_ICE_AND_FIRE":
                await Block(Value("Block"));
                break;
            case "LIBRARIAN-IGNITE":
                await Block(Value("CalculationBase"));
                break;
            case "LIBRARIAN-COOLDOWN":
                await Block(Value("Block"));
                await PowerCmd.Apply<CooldownPower>(context, card.Owner.Creature, 1, card.Owner.Creature, card);
                LibrarianRuntime.VerifyBlock(LibrarianRuntime.Get(card.Owner));
                break;
            case "LIBRARIAN-CHANNEL_FLOW":
                await CardPileCmd.Draw(context, Value("Cards"), card.Owner);
                break;
            case "LIBRARIAN-DRY_BRANCH_SEARCH":
            case "LIBRARIAN-OUT_OF_CONTEXT":
                await Draw(card.DynamicVars.Cards.IntValue);
                break;
            case "LIBRARIAN-READ_WIDELY":
                await PlayerCmd.GainEnergy(card.DynamicVars.Energy.IntValue, card.Owner);
                if (card.IsUpgraded) await Draw(1);
                break;
            case "LIBRARIAN-TIDAL_GRAVITY":
                await PlayerCmd.GainEnergy(checked((int)Value("Energy")), card.Owner);
                break;
            case "LIBRARIAN-RIDGE_WARD":
                await PowerCmd.Apply<RidgeWardPower>(context, card.Owner.Creature, Value("RidgeWardPower"), card.Owner.Creature, card);
                break;
            case "LIBRARIAN-BOOK_BURNING":
                await Hit(0, all: true, count: card.DynamicVars.Repeat.IntValue);
                break;
            case "LIBRARIAN-DROUGHT_EDICT":
            case "LIBRARIAN-FLAME_BURST":
                await Hit(0, all: true);
                break;
            case "LIBRARIAN-SEVER_CURRENT":
                await Hit(0, count: card.DynamicVars["Repeat"].IntValue);
                break;
            case "LIBRARIAN-SEAL_AWAY":
                await Block(Value("Block"));
                PlayerCmd.EndTurn(card.Owner, false);
                break;
            default:
                if (!LibrarianCrossCharacter040.IsForeignBlank(card))
                    throw new InvalidOperationException("Missing imported-card implementation: " + id);
                // Orb-only blank: the native pipeline still pays its cost and exhausts it.
                break;
        }
    }
}

// The native end-turn hook only schedules bottom plays for players with a session.
// A foreign owner can reach this boundary without ever having used a Librarian card.
[HarmonyPatch(typeof(LibrarianCombatHooks), nameof(LibrarianCombatHooks.BeforeSideTurnEnd))]
internal static class LibrarianForeignMainstem040
{
    [HarmonyPrefix]
    private static void Prefix(CombatSide side, IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Player) return;
        foreach (var creature in participants)
            if (creature.Player is { PlayerCombatState: not null } player && player.Character is not LibrarianCharacter
                && !LibrarianRuntime.TryGet(player, out _) && LibrarianBottomPlay041.PeekBottom(player) is BurnTheRiver)
                _ = LibrarianRuntime.Get(player);
    }
}

[HarmonyPatch]
internal static class LibrarianForeignCardPlay040
{
    private static IEnumerable<MethodBase> TargetMethods() => typeof(LibrarianCard).Assembly.GetTypes()
        .Where(t => !t.IsAbstract && typeof(ILibrarianCard).IsAssignableFrom(t))
        .Select(t => AccessTools.DeclaredMethod(t, "OnPlay")).Where(m => m is not null).Cast<MethodBase>();

    [HarmonyPrefix]
    private static bool Prefix(CardModel __instance, PlayerChoiceContext __0, CardPlay __1, ref Task __result)
    {
        if (__instance.Owner.Character is LibrarianCharacter || !LibrarianCrossCharacter040.NeedsForeignAdaptation(__instance)) return true;
        __result = LibrarianForeignCardEffects040.Play(__instance, __0, __1);
        return false;
    }
}
