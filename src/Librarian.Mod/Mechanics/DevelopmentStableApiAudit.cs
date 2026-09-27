using HarmonyLib;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Unlocks;
namespace Librarian.Mechanics;
internal static class DevelopmentStableApiAudit
{
    private static CardModel? ReturnAfterExhaust, MoveOtherAfterExhaust;
    internal static async Task Run(Player player, Func<Task> fresh)
    {
        var context = new ThrowingPlayerChoiceContext(); int checks=0;
        void Check(bool ok, string text) { if(!ok) throw new InvalidOperationException("STABLE_API: "+text); checks++; MainFile.Logger.Info("STABLE_API_CHECK_PASS "+text); }
        async Task Ready() { await fresh(); while(player.PlayerCombatState!.Phase!=PlayerTurnPhase.Play) await NGame.Instance!.ToSignal(NGame.Instance.GetTree(),Godot.SceneTree.SignalName.ProcessFrame); }
        foreach(bool upgraded in new[]{false,true})
        {
            await Ready();
            foreach(var card in player.PlayerCombatState!.Hand.Cards.ToArray()) await CardPileCmd.Add(card,PileType.Discard);
            var first=player.Creature.CombatState!.CreateCard<LibrarianStrike>(player);
            var second=player.Creature.CombatState.CreateCard<LibrarianStrike>(player);
            await CardPileCmd.AddGeneratedCardToCombat(first,PileType.Hand,player);
            await CardPileCmd.AddGeneratedCardToCombat(second,PileType.Hand,player);
            var barrier=player.Creature.CombatState.CreateCard<DeepSeaBarrier>(player);
            if(upgraded){barrier.UpgradeInternal();barrier.FinalizeUpgradeInternal();}
            ReturnAfterExhaust=first; MoveOtherAfterExhaust=second;
            try { await CardCmd.AutoPlay(context,barrier,null,skipCardPileVisuals:true); }
            finally { ReturnAfterExhaust=null; MoveOtherAfterExhaust=null; }
            Check(first.Pile?.Type==PileType.Hand,"after-exhaust return to hand preserved "+upgraded);
            Check(second.Pile?.Type==PileType.Discard,"earlier hook removes later snapshot member "+upgraded);
            Check(player.Creature.GetPower<DeepSeaPendingPower>()?.Amount==(upgraded?6:5),"counts one actual exhaust despite return and moved second card "+upgraded);
        }
        await Ready();
        var combat=(CombatState)player.Creature.CombatState!;
        var ally=Player.CreateForNewRun<Ironclad>(UnlockState.all,910701);
        ally.RunState=player.RunState; ally.ResetCombatState(); combat.AddPlayer(ally);
        try
        {
            for(int i=0;i<5;i++) await CardPileCmd.AddGeneratedCardToCombat(combat.CreateCard<DefendIronclad>(ally),PileType.Draw,ally);
            int before=ally.PlayerCombatState!.Hand.Cards.Count;
            await CardCmd.AutoPlay(context,combat.CreateCard<CirculationNotes>(player),ally.Creature,skipCardPileVisuals:true);
            Check(ally.PlayerCombatState.Hand.Cards.Count==before+3,"native ally draw through stable choice context; one-process fixture");
        }
        finally { combat.RemoveCreature(ally.Creature); }
        MainFile.Logger.Info($"STABLE_API_AUDIT_PASS checks={checks} liveMulticlient=False");
    }
    [HarmonyPatch(typeof(Hook),nameof(Hook.AfterCardExhausted))]
    private static class AfterExhaustFixture
    {
        private static void Postfix(CardModel card, ref Task __result)
        {
            if(Environment.GetEnvironmentVariable("LIBRARIAN_RUNTIME_AUDIT")!="1" || !ReferenceEquals(card,ReturnAfterExhaust))return;
            var other=MoveOtherAfterExhaust; ReturnAfterExhaust=null;
            __result=Move(__result,card,other);
        }
        private static async Task Move(Task task,CardModel card,CardModel? other)
        { await task; await CardPileCmd.Add(card,PileType.Hand); if(other is not null) await CardPileCmd.Add(other,PileType.Discard); }
    }
}
