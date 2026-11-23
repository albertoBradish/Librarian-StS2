using System.Text.Json;
using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.OrbAdvancedBasics;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Cards.PowerCards;
using Librarian.LibrarianCode.Cards.Stateful;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.Mechanics;

internal static partial class DevelopmentRuntimeAudit
{
    private static MegaCrit.Sts2.Core.Entities.Creatures.Creature? _targetBlocked142;
    private static bool Block142Target(MegaCrit.Sts2.Core.Entities.Creatures.Creature target, ref bool __result)
    {
        if (!ReferenceEquals(target,_targetBlocked142)) return true;
        __result=false;return false;
    }

    private static async Task Run142Cards()
    {
        string output = System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT")!;
        LibrarianUnlocks040.ApplyChoice(SaveManager.Instance.Progress, true);
        var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<LibrarianCharacter>(), true,
            ActModel.GetDefaultList(), [], "LIBRARIAN142", GameMode.Standard);
        _player = run.Players.Single();
        async Task Frame() => await NGame.Instance.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
        async Task Reset()
        {
            await FreshFight();
            foreach (var relic in _player.Relics.ToArray()) await RelicCmd.Remove(relic);
            for (int i=0; _player.PlayerCombatState?.Phase!=PlayerTurnPhase.Play; i++)
            { if(i>1200) throw new TimeoutException("142 Play phase"); await Frame(); }
        }
        T New<T>(bool up=false) where T:CardModel
        { var c=_player.Creature.CombatState!.CreateCard<T>(_player); if(up){c.UpgradeInternal();c.FinalizeUpgradeInternal();} return c; }
        async Task<T> Self<T>(bool up=false) where T:CardModel
        { var c=New<T>(up);await CardCmd.AutoPlay(Context,c,target:null,skipCardPileVisuals:true);return c; }
        async Task Turn()
        {
            int turn=_player.PlayerCombatState!.TurnNumber;
            CombatManager.Instance.SetReadyToEndTurn(_player,false);
            for(int i=0;_player.PlayerCombatState!.TurnNumber<=turn || _player.PlayerCombatState.Phase!=PlayerTurnPhase.Play;i++)
            {if(i>2400)throw new TimeoutException("142 native turn");await Frame();}
        }
        IEnumerable<Node> Desc(Node n)
        {foreach(var c in n.GetChildren()){yield return c;foreach(var d in Desc(c))yield return d;}}
        async Task Choice(CardModel card, OrbKind? orb=null, CardModel[]? selected=null)
        {
            var owned=new HookPlayerChoiceContext(card,LocalContext.NetId!.Value,_player.Creature.CombatState!,GameActionType.Combat);
            async Task Execute(){await owned.AssignTaskAndWaitForPauseOrCompletion(CardCmd.AutoPlay(owned,card,target:null));await owned.WaitForCompletion();}
            var task=Execute();bool clicked=false;
            for(int i=0;!task.IsCompleted;i++)
            {
                if(i>1800)throw new TimeoutException("142 real choice "+card.Id);
                if(orb is { } kind)
                {
                    var screen=Desc(NGame.Instance).OfType<NChooseACardSelectionScreen>().FirstOrDefault(s=>s.IsVisibleInTree());
                    var holder=screen is null?null:Desc(screen).OfType<NGridCardHolder>().FirstOrDefault(h=>LibrarianOrbChoice.Choices.TryGetValue(h.CardModel,out var choice)&&choice.Kind==kind);
                    // Native choose-a-card ignores input during its opening 350ms.
                    if(holder is not null){holder.EmitSignal(NCardHolder.SignalName.Pressed,holder);clicked=true;}
                }
                else if(!clicked && NCombatRoom.Instance?.Ui.Hand is { IsInCardSelection:true } hand)
                {
                    foreach(var pick in selected??[])
                    {
                        var holder=Desc(hand).OfType<NHandCardHolder>().Single(h=>ReferenceEquals(h.CardModel,pick));
                        holder.EmitSignal(NCardHolder.SignalName.Pressed,holder);
                    }
                    var confirm=hand.GetNode<NConfirmButton>("%SelectModeConfirmButton");
                    confirm.EmitSignal(NClickableControl.SignalName.Released,confirm);clicked=true;
                }
                await Frame();
            }
            await task;
            Equal(true,clicked,"142 synchronized native selection "+card.Id);
        }
        await Reset();
        Equal(91,ModelDb.CardPool<LibrarianCardPool>().AllCards.Count(),"142 normal cards remain 91");
        Equal(CardType.Attack,ModelDb.Card<TreeRingBurst>().Type,"142 replacement is Attack");
        Equal(CardType.Skill,ModelDb.Card<NourishingLife>().Type,"142 Nourishing is Skill");
        using(var contracts=JsonDocument.Parse(System.IO.File.ReadAllText(System.Environment.GetEnvironmentVariable("LIBRARIAN_142_CONTRACT")!)))
        foreach(var row in contracts.RootElement.EnumerateArray()) foreach(bool up in new[]{false,true})
        {
            var c=ModelDb.GetById<CardModel>(new ModelId("CARD",row.GetProperty("id").GetString()!)).ToMutable();c.Owner=_player;
            if(up){c.UpgradeInternal();c.FinalizeUpgradeInternal();}
            var t=row.GetProperty("target");int phase=up?1:0;
            Equal(t.GetProperty("cost")[phase].GetInt32(),c.EnergyCost.GetWithModifiers(CostModifiers.None),"142 contract cost "+c.Id+up);
            foreach(var v in t.GetProperty("vars").EnumerateObject())Equal(v.Value[phase].GetDecimal(),c.DynamicVars[v.Name].BaseValue,"142 contract var "+c.Id+v.Name+up);
            var keys=t.GetProperty("keywords")[phase].EnumerateArray().Select(k=>k.GetString()).ToArray();
            Equal(keys.Contains("Exhaust"),c.Keywords.Contains(CardKeyword.Exhaust),"142 contract Exhaust "+c.Id+up);
            Equal(keys.Contains("Retain"),c.Keywords.Contains(CardKeyword.Retain),"142 contract Retain "+c.Id+up);
        }
        var formatted=new List<object>();
        foreach(string lang in new[]{"zhs","eng"})
        {
            LibrarianLanguage.Select(lang);
            foreach(var model in ModelDb.CardPool<LibrarianCardPool>().AllCards.Append(ModelDb.Card<StudyNotes>()))
            foreach(bool up in new[]{false,true})
            {
                var c=_player.Creature.CombatState!.CreateCard(model,_player);if(up){c.UpgradeInternal();c.FinalizeUpgradeInternal();}
                await CardPileCmd.Add(c,PileType.Hand,skipVisuals:true);
                foreach(var pile in new[]{PileType.None,PileType.Hand,PileType.Play})
                {
                    string text=c.GetDescriptionForPile(pile);
                    Equal(false,text.Contains('{')||text.Contains('}'),"142 formatter "+c.Id+lang+up+pile);
                    _=c.HoverTips.ToArray();formatted.Add(new{id=c.Id.Entry,lang,up,pile=pile.ToString(),text});
                }
                var node=MegaCrit.Sts2.Core.Assets.PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>();
                NGame.Instance.AddChild(node);node.Model=c;node.UpdateVisuals(PileType.None,CardPreviewMode.Normal);node.QueueFree();
                await CardPileCmd.RemoveFromCombat(c,skipVisuals:true);await Frame();
            }
            foreach(var p in ModelDb.AllPowers.OfType<Librarian.LibrarianCode.Powers.LibrarianPower>())
            {var tip=p.GetDumbHoverTip();Equal(false,tip.Description.Contains('{'),"142 generic Power hover "+p.Id+lang);}
        }
        LibrarianLanguage.Select("zhs");
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,"formatted.json"),JsonSerializer.Serialize(formatted));
        MainFile.Logger.Info($"CARD142_TEXT_PASS cards=92 contexts={formatted.Count} screenshots=0");
        foreach(bool up in new[]{false,true})
        {
            await Reset();await PowerCmd.Apply<StrengthPower>(Context,_player.Creature,3,_player.Creature,null);
            int hp=_enemy.CurrentHp;await Play<EmberReckoning>(up);
            Equal((up?13:9)+3,hp-_enemy.CurrentHp,"142 Ember damage precedes new Strength "+up);
            Equal(3+(up?3:2),_player.Creature.GetPower<StrengthPower>()!.Amount,"142 Ember grants Strength after hit "+up);
            await Reset();await PowerCmd.Apply<DexterityPower>(Context,_player.Creature,2,_player.Creature,null);
            await Self<DamBreak>(up);
            Equal((up?10:7)+2,_player.Creature.Block,"142 Dam Block precedes new Dexterity "+up);
            Equal(2+(up?2:1),_player.Creature.GetPower<DexterityPower>()!.Amount,"142 Dam grants Dexterity after Block "+up);
            await Reset();var extra=await CreatureCmd.Add<MegaCrit.Sts2.Core.Models.Monsters.Tunneler>(_player.Creature.CombatState!);
            await CreatureCmd.SetMaxAndCurrentHp(extra,1000);
            await PowerCmd.Apply<StrengthPower>(Context,_player.Creature,2,_player.Creature,null);
            await PowerCmd.Apply<VigorPower>(Context,_player.Creature,4,_player.Creature,null);
            hp=_enemy.CurrentHp;int hp2=extra.CurrentHp;var earth=await Play<EarthCollapse>(up);
            Equal((up?32:28)+6,hp-_enemy.CurrentHp,"142 Earth main with native modifiers "+up);
            Equal((up?16:14)+6,hp2-extra.CurrentHp,"142 Earth splash shares native attack context "+up);
            Equal(PileType.Exhaust,earth.Pile!.Type,"142 Earth exhausts "+up);
            await Reset();int energy=_player.PlayerCombatState!.Energy;var gravity=await Self<TidalGravity>(up);
            Equal(energy+(up?3:2),_player.PlayerCombatState.Energy,"142 Gravity energy "+up);
            Equal(up?PileType.Discard:PileType.Exhaust,gravity.Pile!.Type,"142 Gravity retains earlier Exhaust upgrade "+up);

            foreach(int value in new[]{0,5,6,7,8,15,16,17})
            {
                await Reset();await Gain(OrbKind.Growth,value);await LibrarianRuntime.Dispatch(Session,Context,Session.Orbs.Lock(OrbKind.Growth));
                var life=await Self<NourishingLife>(up);
                Equal(0,Session.Orbs.Value(OrbKind.Growth),"142 Life clears locked Growth "+up+value);
                Equal(value/(up?6:8),_player.Creature.GetPower<RegenPower>()?.Amount??0,"142 Life integer threshold "+up+value);
                Equal(PileType.Exhaust,life.Pile!.Type,"142 Life exhausts "+up+value);
            }
            await CreatureCmd.SetCurrentHp(_player.Creature,900);await CreatureCmd.GainBlock(_player.Creature,1000,ValueProp.Unpowered,null);await Turn();
            Equal(900+17/(up?6:8),_player.Creature.CurrentHp,"142 native Regeneration heals at owner turn end "+up);

            await Reset();await Gain(OrbKind.Fire,12);await Gain(OrbKind.Tide,3);await Gain(OrbKind.Growth,6);
            var position=Session.Orbs.Foreground;int settled=Session.Orbs.SettlementsThisCombat;
            var branch=await Self<DrawBranch>(up);
            Equal(settled+1,Session.Orbs.SettlementsThisCombat,"142 Branch settles Growth exactly once "+up);
            // Growth6 first strengthens Tide3, so 12+9+6=27 before redistribution.
            foreach(var kind in Session.Orbs.Positions)Equal(9,Session.Orbs.Value(kind),"142 Branch redistributes post-settlement total "+kind+up);
            Equal(position,Session.Orbs.Foreground,"142 Branch keeps position "+up);
            Equal(up?PileType.Discard:PileType.Exhaust,branch.Pile!.Type,"142 Branch upgrade removes Exhaust "+up);
            await Reset();await Gain(OrbKind.Fire,11);await Gain(OrbKind.Growth,5);await LibrarianRuntime.Dispatch(Session,Context,Session.Orbs.Lock(OrbKind.Growth));
            settled=Session.Orbs.SettlementsThisCombat;await Self<DrawBranch>(up);
            Equal(settled,Session.Orbs.SettlementsThisCombat,"142 locked Growth skips settlement "+up);
            Equal(6,Session.Orbs.Value(OrbKind.Growth),"142 Branch remainder starts foreground "+up);
            Equal(true,Session.Orbs.IsLocked(OrbKind.Growth),"142 redistribution preserves lock "+up);

            await Reset();await Gain(OrbKind.Fire,2);await Gain(OrbKind.Tide,3);await Gain(OrbKind.Growth,4);
            settled=Session.Orbs.SettlementsThisCombat;var unity=New<ThreefoldUnity>(up);await Choice(unity,OrbKind.Tide);
            Equal(9,Session.Orbs.Value(OrbKind.Tide),"142 Unity transfers all values "+up);
            Equal(settled+1,Session.Orbs.SettlementsThisCombat,"142 Unity exactly one extra settlement "+up);
            Equal(PileType.Discard,unity.Pile!.Type,"142 Unity does not exhaust "+up);

            foreach(int count in new[]{0,1,3})
            {
                await Reset();foreach(var c in _player.PlayerCombatState!.AllCards.ToArray())await CardPileCmd.RemoveFromCombat(c,skipVisuals:true);
                var hand=new List<CardModel>();for(int i=0;i<3;i++){var c=New<LibrarianDefend>();await CardPileCmd.Add(c,PileType.Hand);hand.Add(c);}
                var barrier=New<DeepSeaBarrier>(up);await Choice(barrier,selected:hand.Take(count).ToArray());
                Equal(count*(up?5:4),Session.Waves.Amount,"142 Barrier immediately grants selected Waves "+up+count);
                Equal(3-count,_player.PlayerCombatState.Hand.Cards.Count,"142 Barrier keeps unselected hand "+up+count);
                Equal(count,hand.Count(c=>c.Pile?.Type==PileType.Exhaust),"142 Barrier successful selected exhausts "+up+count);
                Equal(PileType.Exhaust,barrier.Pile!.Type,"142 Barrier itself exhausts without granting Waves "+up+count);
            }
            await Reset();await Self<SpacetimeTwist>(up);await Turn();
            Equal(1,Session.Orbs.Positions.Count(Session.Orbs.IsLocked),"142 Twist locks exactly one "+up);
            foreach(var kind in Session.Orbs.Positions.Where(k=>!Session.Orbs.IsLocked(k)))
            {Equal(up?7:5,Session.Orbs.Value(kind),"142 Twist revised value "+up+kind);Equal(true,Session.Orbs.IsActivated(kind),"142 Twist activates "+up+kind);}
        }
        MainFile.Logger.Info("CARD142_ORB_AND_CHOICES_PASS basic=True upgraded=True nativeHandZeroPartialAll=True regen=True");

        await Reset();var second=await CreatureCmd.Add<MegaCrit.Sts2.Core.Models.Monsters.Tunneler>(_player.Creature.CombatState!);await CreatureCmd.SetMaxAndCurrentHp(second,1000);
        await Play<LibrarianStrike>();
        Equal(_enemy,Session.LastAttackedEnemy,"142 remembers attack before Charged Strike");
        var charged=await Self<TreeRingBurst>();Equal(PileType.Discard,charged.Pile!.Type,"142 Charged does not Exhaust");
        await CreatureCmd.GainBlock(_enemy,5,ValueProp.Unpowered,null);
        await CreatureCmd.Damage(Context,_enemy,13,ValueProp.Unpowered,_player.Creature);
        await CreatureCmd.Damage(Context,_player.Creature,7,ValueProp.Unpowered,_player.Creature);
        await CreatureCmd.Damage(Context,_enemy,3,ValueProp.Unpowered,_enemy);
        int before=_enemy.CurrentHp;await Turn();Equal(before,_enemy.CurrentHp,"142 Charged waits through current end");
        Equal(13m,_player.Creature.GetPower<ChargedStrikePower>()!.DynamicVars["Damage"].BaseValue,"142 owner-only damage includes Block");
        var attack=New<LibrarianStrike>();await CardCmd.AutoPlay(Context,attack,second,skipCardPileVisuals:true);
        int attackDamage=1000-second.CurrentHp;Equal(second,Session.LastAttackedEnemy,"142 last attack updates chosen enemy");
        await PowerCmd.Apply<StrengthPower>(Context,_player.Creature,10,_player.Creature,null);
        int targetHp=second.CurrentHp;await Turn();
        Equal(13+attackDamage,targetHp-second.CurrentHp,"142 delayed amount exact without adding Strength twice");
        Equal(true,_player.Creature.GetPower<ChargedStrikePower>() is null,"142 Charged expires after payout");

        await Reset();var doomed=await CreatureCmd.Add<MegaCrit.Sts2.Core.Models.Monsters.Tunneler>(_player.Creature.CombatState!);await CreatureCmd.SetMaxAndCurrentHp(doomed,100);
        var hit=New<LibrarianStrike>();await CardCmd.AutoPlay(Context,hit,doomed,skipCardPileVisuals:true);
        await Self<TreeRingBurst>();await Self<TreeRingBurst>(true);
        await CreatureCmd.Damage(Context,_enemy,11,ValueProp.Unpowered,_player.Creature);
        await CreatureCmd.Damage(Context,doomed,1000,ValueProp.Unpowered,_enemy);
        await Turn();before=_enemy.CurrentHp;await Turn();
        Equal(22,before-_enemy.CurrentHp,"142 dead last target falls back; same-turn stacks freeze together");
        await Reset();var blocked=await CreatureCmd.Add<MegaCrit.Sts2.Core.Models.Monsters.Tunneler>(_player.Creature.CombatState!);await CreatureCmd.SetMaxAndCurrentHp(blocked,1000);
        hit=New<LibrarianStrike>();await CardCmd.AutoPlay(Context,hit,blocked,skipCardPileVisuals:true);
        await Self<TreeRingBurst>();await CreatureCmd.Damage(Context,_enemy,9,ValueProp.Unpowered,_player.Creature);await Turn();
        var harmony142=new HarmonyLib.Harmony("Librarian.Audit142Targeting");
        harmony142.Patch(HarmonyLib.AccessTools.Method(typeof(MegaCrit.Sts2.Core.Hooks.Hook),"ShouldAllowTargeting"),
            prefix:new HarmonyLib.HarmonyMethod(typeof(DevelopmentRuntimeAudit),nameof(Block142Target)));
        try
        {
            _targetBlocked142=blocked;before=_enemy.CurrentHp;int blockedHp=blocked.CurrentHp;
            await Turn();
            Equal(9,before-_enemy.CurrentHp,"142 living untargetable last enemy falls back");
            Equal(blockedHp,blocked.CurrentHp,"142 untargetable last enemy not hit");
        }
        finally{_targetBlocked142=null;harmony142.Unpatch(HarmonyLib.AccessTools.Method(typeof(MegaCrit.Sts2.Core.Hooks.Hook),"ShouldAllowTargeting"),HarmonyLib.HarmonyPatchType.Prefix,"Librarian.Audit142Targeting");}
        await Reset();await Self<TreeRingBurst>();await Turn();await Self<TreeRingBurst>();
        await CreatureCmd.Damage(Context,_enemy,7,ValueProp.Unpowered,_player.Creature);before=_enemy.CurrentHp;
        await Turn();Equal(7,before-_enemy.CurrentHp,"142 staggered first window pays");
        Equal(14m,_player.Creature.GetPower<ChargedStrikePower>()!.DynamicVars["Damage"].BaseValue,"142 later window includes earlier payout");
        foreach(var power in _enemy.Powers.ToArray())await PowerCmd.Remove(power);_enemy.LoseBlockInternal(_enemy.Block);
        before=_enemy.CurrentHp;await Turn();Equal(14,before-_enemy.CurrentHp,"142 staggered later window pays its own total");
        await Reset();await Self<TreeRingBurst>();await Turn();await Turn();Equal(true,_player.Creature.GetPower<ChargedStrikePower>() is null,"142 zero-damage window expires");
        MainFile.Logger.Info("CARD142_DELAYED_PASS nativeTurns=True lastTarget=True randomFallback=True ownerAttribution=True independentWindows=True");

        await Reset();var study=New<Overfishing>(true);await Choice(study,OrbKind.Fire);await Turn();
        Equal(true,_player.PlayerCombatState!.Hand.Cards.OfType<StudyNotes>().Any(c=>c.IsUpgraded),"142 notes generate at real next turn");
        await LibrarianRuntime.Dispatch(Session,Context,Session.Orbs.Lock(OrbKind.Growth));await LibrarianRuntime.Dispatch(Session,Context,Session.Orbs.Lock(OrbKind.Tide));
        await Self<StudyNotes>(true);
        Equal(4,_player.Creature.GetPower<ThornsPower>()!.Amount,"142 notes Growth condition");
        Equal(3,_player.Creature.GetPower<StrengthPower>()!.Amount,"142 notes Fire condition");
        Equal(2,_player.Creature.GetPower<DexterityPower>()!.Amount,"142 notes Tide condition");
        await SaveManager.Instance.SaveRun(null);var saved=SaveManager.Instance.LoadRunSave();
        Equal(true,saved.Success&&saved.SaveData is not null,"142 native save read");
        var restored=RunState.FromSerializable(saved.SaveData!);await NGame.Instance.ReturnToMainMenu();
        await RunManager.Instance.SetUpSavedSingleplayer(restored,saved.SaveData!);await NGame.Instance.LoadRun(restored,saved.SaveData!.PreFinishedRoom);await NGame.Instance.Transition.FadeIn();
        Equal(true,RunManager.Instance.IsInProgress,"142 native save reload");
        MainFile.Logger.Info($"CARD142_AUDIT_PASS checks={_checks} menu=True newRun=True combat=True save=True reload=True realMultiplayer=False screenshots=0");
        await NGame.Instance.ReturnToMainMenu();
        await DevelopmentOnboardingAudit.Check142Practice(output);
        NGame.Instance.Quit();
    }
}

internal static partial class DevelopmentRuntimeAudit
{
    private static async Task Run142Foreign()
    {
        var run=await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Ironclad>(),true,
            ActModel.GetDefaultList(),[],"LIBRARIAN142FOREIGN",GameMode.Standard);
        _player=run.Players.Single();
        async Task Frame()=>await NGame.Instance.ToSignal(NGame.Instance.GetTree(),SceneTree.SignalName.ProcessFrame);
        async Task Reset()
        {
            await FreshFight();foreach(var relic in _player.Relics.ToArray())await RelicCmd.Remove(relic);
            for(int i=0;_player.PlayerCombatState?.Phase!=PlayerTurnPhase.Play;i++)
            {if(i>1200)throw new TimeoutException("142 foreign Play");await Frame();}
        }
        T New<T>(bool up=false)where T:CardModel
        {var c=_player.Creature.CombatState!.CreateCard<T>(_player);if(up){c.UpgradeInternal();c.FinalizeUpgradeInternal();}return c;}
        async Task<T> Self<T>(bool up=false)where T:CardModel
        {var c=New<T>(up);await CardCmd.AutoPlay(Context,c,target:null,skipCardPileVisuals:true);return c;}
        async Task Turn()
        {
            int turn=_player.PlayerCombatState!.TurnNumber;CombatManager.Instance.SetReadyToEndTurn(_player,false);
            for(int i=0;_player.PlayerCombatState!.TurnNumber<=turn||_player.PlayerCombatState.Phase!=PlayerTurnPhase.Play;i++)
            {if(i>2400)throw new TimeoutException("142 foreign turn");await Frame();}
        }
        IEnumerable<Node> Desc(Node n){foreach(var c in n.GetChildren()){yield return c;foreach(var d in Desc(c))yield return d;}}
        void NoOrbs()
        {
            Equal(false,Session.HasCharacterOrbs,"142 foreign no character orbs");
            Equal(true,Session.Orbs.Positions.All(k=>Session.Orbs.Value(k)==0&&!Session.Orbs.IsActivated(k)&&!Session.Orbs.IsLocked(k)),"142 foreign no fabricated values activation locks");
            Equal(true,LibrarianOrbPanel.GetDisplay(Session)is null,"142 foreign no visible orb panel");
        }
        foreach(bool up in new[]{false,true})
        {
            await Reset();await PowerCmd.Apply<StrengthPower>(Context,_player.Creature,2,_player.Creature,null);int hp=_enemy.CurrentHp;
            await Play<EmberReckoning>(up);Equal((up?13:9)+2,hp-_enemy.CurrentHp,"142 foreign Ember printed Damage then Strength "+up);
            Equal(2+(up?3:2),_player.Creature.GetPower<StrengthPower>()!.Amount,"142 foreign Ember grants Strength "+up);NoOrbs();
            await Reset();await PowerCmd.Apply<DexterityPower>(Context,_player.Creature,2,_player.Creature,null);await Self<DamBreak>(up);
            Equal((up?10:7)+2,_player.Creature.Block,"142 foreign Dam printed Block first "+up);
            Equal(2+(up?2:1),_player.Creature.GetPower<DexterityPower>()!.Amount,"142 foreign Dam grants Dexterity "+up);NoOrbs();
            await Reset();var other=await CreatureCmd.Add<MegaCrit.Sts2.Core.Models.Monsters.Tunneler>(_player.Creature.CombatState!);await CreatureCmd.SetMaxAndCurrentHp(other,1000);
            hp=_enemy.CurrentHp;int hp2=other.CurrentHp;var earth=await Play<EarthCollapse>(up);
            Equal(up?32:28,hp-_enemy.CurrentHp,"142 foreign Earth main "+up);Equal(up?16:14,hp2-other.CurrentHp,"142 foreign Earth splash "+up);
            Equal(PileType.Exhaust,earth.Pile!.Type,"142 foreign Earth Exhaust "+up);NoOrbs();
            await Reset();hp=_enemy.CurrentHp;var life=await Self<NourishingLife>(up);
            Equal(hp,_enemy.CurrentHp,"142 foreign Life no obsolete attack "+up);Equal(true,_player.Creature.GetPower<RegenPower>()is null,"142 foreign Life no value means no Regeneration "+up);
            Equal(PileType.Exhaust,life.Pile!.Type,"142 foreign Life Exhaust "+up);NoOrbs();
            await Reset();foreach(var c in _player.PlayerCombatState!.AllCards.ToArray())await CardPileCmd.RemoveFromCombat(c,skipVisuals:true);
            var hand=new List<CardModel>();for(int i=0;i<3;i++){var c=New<MegaCrit.Sts2.Core.Models.Cards.DefendIronclad>();await CardPileCmd.Add(c,PileType.Hand);hand.Add(c);}
            var barrier=New<DeepSeaBarrier>(up);var owned=new HookPlayerChoiceContext(barrier,LocalContext.NetId!.Value,_player.Creature.CombatState!,GameActionType.Combat);
            async Task Execute(){await owned.AssignTaskAndWaitForPauseOrCompletion(CardCmd.AutoPlay(owned,barrier,target:null));await owned.WaitForCompletion();}
            var task=Execute();bool clicked=false;
            for(int i=0;!task.IsCompleted;i++)
            {
                if(i>1800)throw new TimeoutException("142 foreign Barrier choice");
                if(!clicked&&NCombatRoom.Instance?.Ui.Hand is{IsInCardSelection:true}ui)
                {
                    var h=Desc(ui).OfType<NHandCardHolder>().Single(h=>ReferenceEquals(h.CardModel,hand[1]));h.EmitSignal(NCardHolder.SignalName.Pressed,h);
                    var confirm=ui.GetNode<NConfirmButton>("%SelectModeConfirmButton");confirm.EmitSignal(NClickableControl.SignalName.Released,confirm);clicked=true;
                }
                await Frame();
            }
            await task;Equal(true,clicked,"142 foreign Barrier real hand choice "+up);
            Equal(up?5:4,Session.Waves.Amount,"142 foreign Barrier immediate Waves "+up);Equal(2,_player.PlayerCombatState.Hand.Cards.Count,"142 foreign Barrier keeps unselected cards "+up);NoOrbs();
            await Reset();var strike=await Self<TreeRingBurst>(up);Equal(up,strike.Keywords.Contains(CardKeyword.Retain),"142 foreign Charged upgrade Retain "+up);
            await CreatureCmd.Damage(Context,_enemy,13,ValueProp.Unpowered,_player.Creature);await Turn();hp=_enemy.CurrentHp;await Turn();
            Equal(13,hp-_enemy.CurrentHp,"142 foreign Charged real next-turn payout "+up);Equal(true,_player.Creature.GetPower<ChargedStrikePower>()is null,"142 foreign Charged expires "+up);NoOrbs();
            Equal(true,LibrarianCrossCharacter040.IsSeaGlassUseful(strike,_player),"142 Charged is useful across characters");
            Equal(true,LibrarianCrossCharacter040.IsSeaGlassBlank(life),"142 Nourishing is orb-only across characters");
            await Reset();int energy=_player.PlayerCombatState!.Energy;await Self<TidalGravity>(up);
            Equal(energy+(up?3:2),_player.PlayerCombatState.Energy,"142 foreign Gravity energy "+up);NoOrbs();
        }
        await SaveManager.Instance.SaveRun(null);var saved=SaveManager.Instance.LoadRunSave();Equal(true,saved.Success&&saved.SaveData is not null,"142 foreign native save");
        var restored=RunState.FromSerializable(saved.SaveData!);await NGame.Instance.ReturnToMainMenu();await RunManager.Instance.SetUpSavedSingleplayer(restored,saved.SaveData!);
        await NGame.Instance.LoadRun(restored,saved.SaveData!.PreFinishedRoom);await NGame.Instance.Transition.FadeIn();Equal(true,RunManager.Instance.IsInProgress,"142 foreign native reload");
        MainFile.Logger.Info($"CARD142_FOREIGN_PASS checks={_checks} basic=True upgraded=True choice=True realTurns=True save=True reload=True noVisibleOrbs=True screenshots=0 realMultiplayer=False");
        await NGame.Instance.ReturnToMainMenu();NGame.Instance.Quit();
    }
}
