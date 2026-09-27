using System.Reflection;
using HarmonyLib;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
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
        Task Draw() => CardPileCmd.Draw(context, Value("Cards"), card.Owner);
        async Task Hit(decimal damage, bool all = false, int count = 1, bool random = false)
        {
            var attack = DamageCmd.Attack(damage).FromCard(card, play);
            if (random) attack.TargetingRandomOpponents(card.CombatState!);
            else if (all) attack.TargetingAllOpponents(card.CombatState!);
            else { ArgumentNullException.ThrowIfNull(play.Target); attack.Targeting(play.Target); }
            await attack.WithHitCount(count).WithHitFx("vfx/vfx_attack_slash").Execute(context);
        }
        switch (id)
        {
            case "LIBRARIAN-NEEDLE_FLURRY":
            case "LIBRARIAN-FLAME_STRIKE":
            case "LIBRARIAN-WAVE_STRIKE":
            case "LIBRARIAN-ASHEN_BLOW":
            case "LIBRARIAN-EMBER_PIERCE":
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
            case "LIBRARIAN-GAP_NEEDLE":
            case "LIBRARIAN-EMBER_RECKONING":
                await Hit(Value("CalculationBase"));
                break;
            case "LIBRARIAN-FIRE_INSCRIPTION":
                // Binding can copy actual settlements to a foreign recipient without
                // granting that recipient character orbs. Preserve that real history.
                await Hit(Value("CalculationBase") + (LibrarianRuntime.TryGet(card.Owner, out var history)
                    ? history!.Orbs.SettlementsThisCombat * Value("ExtraDamage") : 0));
                break;
            case "LIBRARIAN-TIDAL_STRIKE":
                await Hit(Value("CalculationBase") + (LibrarianRuntime.TryGet(card.Owner, out var tidal) ? tidal!.Waves.Amount : 0));
                break;
            case "LIBRARIAN-ZERO_SEARCH":
                await Draw();
                break;
            case "LIBRARIAN-ROOTBIND":
            case "LIBRARIAN-STEAM_BLAST":
                await Hit(Value("Damage"), all: true);
                break;
            case "LIBRARIAN-TIDAL_EROSION":
                await Hit(Value("Damage"), all: true);
                foreach (var enemy in card.CombatState!.GetOpponentsOf(card.Owner.Creature).Where(c => c.IsHittable).ToArray())
                    await PowerCmd.Apply<WeakPower>(context, enemy, Value("WeakPower"), card.Owner.Creature, card);
                break;
            case "LIBRARIAN-EARTH_COLLAPSE":
                // Separate attacks reselect a living enemy each time, as on the Librarian path.
                for (int i = 0; i < card.ResolveEnergyXValue(); i++)
                {
                    if (MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsOverOrEnding || card.Owner.Creature.IsDead) break;
                    await Hit(Value("Damage"), random: true);
                }
                break;
            case "LIBRARIAN-NOURISHING_LIFE":
                await Hit(Value("Damage"), count: 2);
                break;
            case "LIBRARIAN-QUIET_EMBERS":
            case "LIBRARIAN-SPROUTING_SEED":
            case "LIBRARIAN-VINE_SHIELD":
            case "LIBRARIAN-SONG_OF_ICE_AND_FIRE":
                await Block(Value("Block"));
                break;
            case "LIBRARIAN-IGNITE":
                await Block(Value("CalculationBase"));
                break;
            case "LIBRARIAN-COOLDOWN":
                await Block(Value("Block"));
                // Waves are an explicitly supported cross-character status. Its existing ledger
                // may allocate a session, but this path never activates or grants an orb value.
                var waves = LibrarianRuntime.Get(card.Owner);
                waves.Waves.Add(checked((int)Value("Waves")));
                LibrarianRuntime.VerifyBlock(waves);
                break;
            case "LIBRARIAN-SPROUTING_BULWARK":
                await Block(Value("Block"));
                await Draw();
                break;
            case "LIBRARIAN-CHANNEL_FLOW":
            case "LIBRARIAN-DRY_BRANCH_SEARCH":
            case "LIBRARIAN-EVAPORATION":
            case "LIBRARIAN-OUT_OF_CONTEXT":
                if (Value("Cards") > 0) await Draw();
                break;
            case "LIBRARIAN-REKINDLE":
                await Draw();
                await CardPileCmd.AddGeneratedCardToCombat(card.CreateClone(), PileType.Draw, card.Owner, CardPilePosition.Bottom);
                break;
            case "LIBRARIAN-CALIBRATE":
            {
                var calibrateSession = LibrarianRuntime.Get(card.Owner);
                await LibrarianBottomPlay041.DrawTopThenTakeBottomAsync(context, card.Owner,
                    checked((int)Value("Cards")), calibrateSession.ResolvingEndTurn);
                await LibrarianBottomPlay041.MoveToBottomAsync(context, card);
                break;
            }
            case "LIBRARIAN-READ_BACKWARD":
                await LibrarianBottomPlay041.ReadBackwardAsync(context, card.Owner);
                break;
            case "LIBRARIAN-FUEL_THE_FIRE":
            {
                // Allocate the existing per-combat session so the shared end-turn
                // hook sees this power, while HasCharacterOrbs remains false and
                // no hidden orb state is presented to the foreign character.
                _ = LibrarianRuntime.Get(card.Owner);
                await PowerCmd.Apply<FuelTheFirePower>(context, card.Owner.Creature,
                    Value("FuelTheFirePower"), card.Owner.Creature, card);
                break;
            }
            case "LIBRARIAN-BUILD_CANAL":
            case "LIBRARIAN-TIDAL_GRAVITY":
            case "LIBRARIAN-READ_WIDELY":
                await PlayerCmd.GainEnergy(checked((int)Value("Energy")), card.Owner);
                break;
            case "LIBRARIAN-DEEP_SEA_BARRIER":
                foreach (var target in PileType.Hand.GetPile(card.Owner).Cards.Where(c => !ReferenceEquals(c, card)).ToArray())
                    if (target.Pile?.Type == PileType.Hand) await CardCmd.Exhaust(context, target);
                break;
            case "LIBRARIAN-RIDGE_WARD":
                await PowerCmd.Apply<RidgeWardPower>(context, card.Owner.Creature, Value("RidgeWardPower"), card.Owner.Creature, card);
                break;
            case "LIBRARIAN-UNRETREATING_TIDE":
                int floor = checked((int)Value("UnretreatingTidePower"));
                int existing = card.Owner.Creature.GetPower<UnretreatingTidePower>()?.Amount ?? 0;
                if (floor > existing)
                    await PowerCmd.Apply<UnretreatingTidePower>(context, card.Owner.Creature, floor - existing, card.Owner.Creature, card);
                break;
            default:
                if (!LibrarianCrossCharacter040.IsSeaGlassBlank(card))
                    throw new InvalidOperationException("Missing imported-card implementation: " + id);
                // Orb-only blank: the native pipeline still pays its cost and exhausts it.
                break;
        }
    }
}

[HarmonyPatch]
internal static class LibrarianForeignCardPlay040
{
    private static IEnumerable<MethodBase> TargetMethods() => typeof(LibrarianCard).Assembly.GetTypes()
        .Where(t => !t.IsAbstract && typeof(LibrarianCard).IsAssignableFrom(t))
        .Select(t => AccessTools.DeclaredMethod(t, "OnPlay")).Where(m => m is not null).Cast<MethodBase>();

    [HarmonyPrefix]
    private static bool Prefix(CardModel __instance, PlayerChoiceContext __0, CardPlay __1, ref Task __result)
    {
        if (__instance.Owner.Character is LibrarianCharacter || !LibrarianCrossCharacter040.NeedsForeignAdaptation(__instance)) return true;
        __result = LibrarianForeignCardEffects040.Play(__instance, __0, __1);
        return false;
    }
}
