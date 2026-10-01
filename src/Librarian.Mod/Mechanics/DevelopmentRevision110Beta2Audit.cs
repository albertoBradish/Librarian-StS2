using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;

namespace Librarian.Mechanics;

internal static class DevelopmentRevision110Beta2Audit
{
    internal static async Task Run(Player player, Func<Task> freshFight)
    {
        var ctx = new ThrowingPlayerChoiceContext();
        int checks = 0;
        LibrarianSession S() => LibrarianRuntime.Get(player);
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("110 beta2: " + label);
            checks++; MainFile.Logger.Info("V110_BETA2_CHECK_PASS " + label);
        }
        async Task Reset()
        {
            await freshFight();
            for (int i = 0; player.PlayerCombatState?.Phase != PlayerTurnPhase.Play; i++)
            {
                if (i > 1200) throw new TimeoutException("110 beta2 Play phase");
                await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            await CreatureCmd.SetMaxAndCurrentHp(player.Creature, 10000);
            foreach (var enemy in player.Creature.CombatState!.HittableEnemies)
                await CreatureCmd.SetMaxAndCurrentHp(enemy, 10000);
        }
        EndlessTide Card(bool up)
        {
            var c = player.Creature.CombatState!.CreateCard<EndlessTide>(player);
            if (up) { c.UpgradeInternal(); c.FinalizeUpgradeInternal(); }
            return c;
        }
        Task Dispatch(OrbOperationResult op) => LibrarianRuntime.Dispatch(S(), ctx, op);
        async Task Turn()
        {
            int round = player.Creature.CombatState!.RoundNumber;
            CombatManager.Instance.SetReadyToEndTurn(player, false);
            for (int i = 0; player.Creature.CombatState!.RoundNumber == round || player.PlayerCombatState!.Phase != PlayerTurnPhase.Play; i++)
            {
                if (i > 3600) throw new TimeoutException("110 beta2 native turn");
                await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
        foreach (var relic in player.Relics.ToArray()) await RelicCmd.Remove(relic);
        foreach (bool up in new[] { false, true })
        foreach (bool ringFirst in new[] { false, true })
        {
            await Reset();
            S().Waves.Add(16);
            var card = Card(up);
            Check(card.EnergyCost.GetWithModifiers(CostModifiers.None) == (up ? 1 : 2), "unchanged cost " + up);
            if (ringFirst) await PowerCmd.Apply<RingCurriculumPower>(ctx, player.Creature, 2, player.Creature, null);
            await CardCmd.AutoPlay(ctx, card, null, skipCardPileVisuals: true);
            if (!ringFirst) await PowerCmd.Apply<RingCurriculumPower>(ctx, player.Creature, 2, player.Creature, null);
            await CardCmd.AutoPlay(ctx, Card(!up), null, skipCardPileVisuals: true);
            Check(player.Creature.GetPower<EndlessTidePower>()!.Amount == 1, "nonstacking " + up + " " + ringFirst);
            Check(S().Waves.Amount == 16 && S().Orbs.Value(OrbKind.Tide) == 0, "play preserves existing Waves and has no immediate gain");
            await PowerCmd.Apply<TidalMarkPower>(ctx, player.Creature, 2, player.Creature, null);
            S().Waves.Add(14); // Same shared addition used by Clarity Potion and foreign effects.
            Check(S().Waves.Amount == 16, "direct/potion/foreign shared gain blocked");
            await Dispatch(S().Orbs.Gain(OrbKind.Tide, 5));
            await Dispatch(S().Orbs.Lose(OrbKind.Tide, 2, OrbScope.All));
            Check(S().Waves.Amount == 16, "Tidal Mark gain and loss blocked");
            await Turn();
            Check(S().Orbs.Value(OrbKind.Tide) == 4, "real turn adds exactly one Tide with both Power orders");
            Check(S().Orbs.Value(OrbKind.Growth) == 2, "Ring Curriculum still triggers");
            Check(S().Waves.Amount == 8, "Tide settlement grants no Waves; existing Waves decay normally");
            await Turn();
            Check(S().Orbs.Value(OrbKind.Tide) == 5, "second real turn adds one Tide");
            await Dispatch(S().Orbs.Lock(OrbKind.Tide));
            await Turn();
            Check(S().Orbs.Value(OrbKind.Tide) == 6 && !S().Orbs.IsActivated(OrbKind.Tide), "locked Tide still gains value without activation, as normal");
            await PowerCmd.Remove(player.Creature.GetPower<EndlessTidePower>()!);
            int waves = S().Waves.Amount;
            S().Waves.Add(14);
            Check(S().Waves.Amount == waves + 14, "gain policy follows Power removal");
        }
        await Reset();
        S().Waves.Add(14);
        Check(S().Waves.Amount == 14 && player.Creature.GetPower<EndlessTidePower>() is null, "fresh combat clears prevention");
        MainFile.Logger.Info($"V110_BETA2_AUDIT_PASS checks={checks} realTurns=12 liveMulticlient=False");
    }
}
