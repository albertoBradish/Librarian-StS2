using Godot;
using System.Text.Json;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.OrbAdvancedBasics;
using Librarian.LibrarianCode.Cards.OrbBasics;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Cards.PowerCards;
using Librarian.LibrarianCode.Cards.Stateful;
using Librarian.LibrarianCode.Cards.NativeBatch;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.Mechanics;

internal static partial class DevelopmentRuntimeAudit
{
    private static async Task Check140Contracts(string output)
    {
        using var expected = JsonDocument.Parse(System.IO.File.ReadAllText(System.Environment.GetEnvironmentVariable("LIBRARIAN_140_CONTRACT")!));
        foreach (var row in expected.RootElement.EnumerateArray())
        {
            var model = ModelDb.GetById<CardModel>(new ModelId("CARD", row.GetProperty("id").GetString()!));
            foreach (bool up in new[] { false, true })
            {
                var c=model.ToMutable(); c.Owner=_player;
                if(up) { c.UpgradeInternal(); c.FinalizeUpgradeInternal(); }
                var t=row.GetProperty("target"); int phase=up?1:0;
                var cost=t.GetProperty("cost")[phase];
                Equal(cost.ValueKind==JsonValueKind.String && cost.GetString()=="X",c.EnergyCost.CostsX,"140 X cost "+c.Id+up);
                if(cost.ValueKind==JsonValueKind.Number) Equal(cost.GetInt32(),c.EnergyCost.GetWithModifiers(CostModifiers.None),"140 cost "+c.Id+up);
                string type=t.GetProperty("type").GetString()!;
                Equal(type=="能力"?CardType.Power:type=="攻击"?CardType.Attack:CardType.Skill,c.Type,"140 type "+c.Id+up);
                foreach(var v in t.GetProperty("vars").EnumerateObject())
                    Equal(v.Value[phase].GetDecimal(),c.DynamicVars[v.Name].BaseValue,"140 variable "+c.Id+"/"+v.Name+up);
                var keywords=t.GetProperty("keywords")[phase].EnumerateArray().Select(k=>k.GetString()).ToArray();
                Equal(keywords.Contains("消耗"),c.Keywords.Contains(CardKeyword.Exhaust),"140 Exhaust "+c.Id+up);
                Equal(keywords.Contains("固有"),c.Keywords.Contains(CardKeyword.Innate),"140 Innate "+c.Id+up);
                Equal(keywords.Contains("保留"),c.Keywords.Contains(CardKeyword.Retain),"140 Retain "+c.Id+up);
            }
        }
        Equal(CardType.Power,ModelDb.Card<UnretreatingTide>().Type,"140 After Rain is Power");
        Equal(CardType.Skill,ModelDb.Card<EmberPierce>().Type,"140 Shelter is Skill");
        Equal(true,ModelDb.Card<StudyNotes>().Keywords.Contains(CardKeyword.Exhaust),"140 notes still Exhaust");
        foreach(string lang in new[]{"zhs","eng"})
        {
            LibrarianLanguage.Select(lang);
            var p=ModelDb.Power<AfterRainPower>();
            _=p.HoverTips.ToArray();
        }
        LibrarianLanguage.Select("zhs");
        await Task.CompletedTask;
        MainFile.Logger.Info("CARD140_CONTRACT_PASS slots=26 stages=52 latestTypes=Power/Skill");
    }

    private static async Task Check140NewMechanics(Func<Task> reset, Func<Task> turn, Func<Task> frame)
    {
        T New<T>(bool up=false) where T:CardModel
        {var c=_player.Creature.CombatState!.CreateCard<T>(_player);if(up){c.UpgradeInternal();c.FinalizeUpgradeInternal();}return c;}
        IEnumerable<Node> Desc(Node n) {foreach(var c in n.GetChildren()){yield return c;foreach(var d in Desc(c))yield return d;}}
        async Task EmptyPiles()
        {
            foreach(var c in _player.PlayerCombatState!.AllCards.ToArray()) await CardPileCmd.RemoveFromCombat(c,skipVisuals:true);
        }
        foreach(bool up in new[]{false,true})
        {
            await reset(); await EmptyPiles();
            var a=New<WearyIncantation>();var b=New<WearyIncantation>();var other=New<WearyIncantation>();
            await CardPileCmd.Add(other,PileType.Hand);
            await CardPileCmd.Add(a,PileType.Draw);await CardPileCmd.Add(b,PileType.Draw);
            var pair=New<Calibrate>(up);pair.EnergyCost.CapturedXValue=1;
            await CardCmd.AutoPlay(Context,pair,target:null,skipXCapture:true,skipCardPileVisuals:true);
            Equal(up?3:2,_player.PlayerCombatState!.Hand.Cards.Count,"140 Pair draws X or X+1");
            Equal(1,a.EnergyCost.GetWithModifiers(CostModifiers.All),"140 Pair drawn cost -X");
            Equal(up?1:2,b.EnergyCost.GetWithModifiers(CostModifiers.All),"140 Pair only drawn cards reduced");
            Equal(2,other.EnergyCost.GetWithModifiers(CostModifiers.All),"140 Pair existing hand unchanged");
            Equal(PileType.Exhaust,pair.Pile!.Type,"140 Pair Exhausts instead of bottom");
            await CardCmd.AutoPlay(Context,a,_enemy,skipCardPileVisuals:true);
            Equal(1,a.EnergyCost.GetWithModifiers(CostModifiers.All),"140 Pair discount survives first play");
            await turn();
            Equal(2,a.EnergyCost.GetWithModifiers(CostModifiers.All),"140 Pair discount expires native turn");

            await reset(); await EmptyPiles();
            var insert=New<Springwater>(up);var chosen=New<LibrarianDefend>();
            await CardPileCmd.Add(chosen,PileType.Hand);
            for(int i=0;i<2;i++) await CardPileCmd.Add(New<WearyIncantation>(),PileType.Draw);
            var owned=new HookPlayerChoiceContext(insert,LocalContext.NetId!.Value,_player.Creature.CombatState!,GameActionType.Combat);
            async Task Choice(){await owned.AssignTaskAndWaitForPauseOrCompletion(CardCmd.AutoPlay(owned,insert,target:null));await owned.WaitForCompletion();}
            var task=Choice();bool clicked=false;
            for(int i=0;!task.IsCompleted;i++)
            {
                if(i>1800)throw new TimeoutException("140 Insert Page selection");
                var hand=NCombatRoom.Instance?.Ui.Hand;
                if(!clicked && hand?.IsInCardSelection==true)
                {
                    var holder=Desc(hand).OfType<NHandCardHolder>().Single(h=>ReferenceEquals(h.CardModel,chosen));
                    holder.EmitSignal(NCardHolder.SignalName.Pressed,holder);
                    var confirm=hand.GetNode<NConfirmButton>("%SelectModeConfirmButton");
                    confirm.EmitSignal(NClickableControl.SignalName.Released,confirm);clicked=true;
                }
                await frame();
            }
            await task;
            Equal(true,clicked,"140 Insert real hand selection");
            Equal(chosen,_player.PlayerCombatState!.DrawPile.Cards.Last(),"140 Insert selected card goes bottom");
            Equal(up?2:1,_player.PlayerCombatState.Hand.Cards.Count,"140 Insert draw count then put one back");
            Equal(true,insert.Keywords.Contains(CardKeyword.Exhaust),"140 Insert now Exhausts");
            Equal(PileType.Exhaust,insert.Pile!.Type,"140 Insert native Exhaust destination");

            await reset();
            await Play<AncientCatalog>(up);
            await LibrarianRuntime.Dispatch(Session,Context,Session.Orbs.Lock(OrbKind.Tide,1,new("140-lock")));
            Equal(up?5:4,_player.Creature.Block,"140 Corruption lock grants Block");
            Equal(up?7:5,_player.Creature.GetPower<VigorPower>()!.Amount,"140 Corruption lock grants Vigor");
            await LibrarianRuntime.Dispatch(Session,Context,Session.Orbs.Lock(OrbKind.Tide,1,new("140-relock")));
            Equal(up?5:4,_player.Creature.Block,"140 Corruption only once across relock");

            await reset();
            await Play<BlazingChapter>(up); await Gain(OrbKind.Fire,9);
            var secondary=await CreatureCmd.Add<MegaCrit.Sts2.Core.Models.Monsters.Tunneler>(_player.Creature.CombatState!);
            await CreatureCmd.SetMaxAndCurrentHp(secondary,1000);
            int hp=_enemy.CurrentHp, hp2=secondary.CurrentHp;
            await Play<EarthCollapse>(up);
            Equal(up?28:24,hp-_enemy.CurrentHp,"140 Earth main damage");
            Equal(up?14:12,hp2-secondary.CurrentHp,"140 Earth other enemy damage");
            Equal(up?6:7,Session.Orbs.Value(OrbKind.Fire),"140 Earth one Chapter conversion after both targets");
            Equal(up?3:2,_player.Creature.GetPower<StrengthPower>()!.Amount,"140 Chapter upgraded cap");

            await reset();await CreatureCmd.SetCurrentHp(_player.Creature,900);
            await Play<UnretreatingTide>(up);
            Equal(2,Session.Orbs.LockedTurns(OrbKind.Tide),"140 Rain locks Tide two turns");
            Equal(0,Session.Waves.RetentionFloor,"140 Rain does not set old Waves floor");
            var minion=await CreatureCmd.Add<MegaCrit.Sts2.Core.Models.Monsters.Tunneler>(_player.Creature.CombatState!);
            await PowerCmd.Apply<MinionPower>(Context,minion,1,_enemy,null);
            await CreatureCmd.Damage(Context,minion,10000,ValueProp.Unpowered,_player.Creature);
            Equal(900,_player.Creature.CurrentHp,"140 Rain excludes native Minion fatal");
            var victim=await CreatureCmd.Add<MegaCrit.Sts2.Core.Models.Monsters.Tunneler>(_player.Creature.CombatState!);
            await CreatureCmd.Damage(Context,victim,10000,ValueProp.Unpowered,_player.Creature);
            Equal(up?905:903,_player.Creature.CurrentHp,"140 Rain owner non-attack kill heals");
            var foreign=await CreatureCmd.Add<MegaCrit.Sts2.Core.Models.Monsters.Tunneler>(_player.Creature.CombatState!);
            await CreatureCmd.Damage(Context,foreign,10000,ValueProp.Unpowered,_enemy);
            Equal(up?905:903,_player.Creature.CurrentHp,"140 Rain ignores other dealer");
        }
        await reset();await EmptyPiles();var zero=New<Calibrate>(true);zero.EnergyCost.CapturedXValue=0;
        var draw=New<WearyIncantation>();await CardPileCmd.Add(draw,PileType.Draw);
        await CardCmd.AutoPlay(Context,zero,target:null,skipXCapture:true,skipCardPileVisuals:true);
        Equal(1,_player.PlayerCombatState!.Hand.Cards.Count,"140 upgraded X0 draws one");
        Equal(2,draw.EnergyCost.GetWithModifiers(CostModifiers.All),"140 upgraded X0 discount zero");
        await reset();await Play<ReadWidely>();
        Equal(2,Session.Orbs.Value(OrbKind.Fire),"140 Read Widely Fire2");
        Equal(3,Session.Orbs.Value(OrbKind.Tide),"140 Read Widely Tide3");
        Equal(4,Session.Orbs.Value(OrbKind.Growth),"140 Read Widely Growth4");
        Equal(OrbKind.Growth,Session.Orbs.Foreground,"140 Read Widely last Growth");
        MainFile.Logger.Info("CARD140_NEW_MECHANICS_PASS realChoices=True realTurns=True minionExcluded=True screenshots=0");
    }
}
