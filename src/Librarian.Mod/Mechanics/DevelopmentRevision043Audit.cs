using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.OrbBasics;
using Librarian.LibrarianCode.Cards.OrbAdvancedBasics;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Cards.PowerCards;
using Librarian.LibrarianCode.Cards.Stateful;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.Mechanics;

internal static class DevelopmentRevision043Audit
{
    internal static async Task Run(Player player, Func<Task> freshFight)
    {
        var ctx = new ThrowingPlayerChoiceContext();
        int checks = 0;
        var extras = new List<Player>();
        LibrarianSession S() => LibrarianRuntime.Get(player);
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException("043: " + name); checks++; MainFile.Logger.Info("CARD043_CHECK_PASS " + name); }
        async Task Reset()
        {
            foreach (var extra in extras) if (extra.Creature.CombatState is CombatState c) c.RemoveCreature(extra.Creature);
            extras.Clear();
            await freshFight();
            for (int i = 0; player.PlayerCombatState?.Phase != PlayerTurnPhase.Play; i++)
            { if (i > 1200) throw new TimeoutException("043 Play phase"); await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame); }
            foreach (var enemy in player.Creature.CombatState!.HittableEnemies) await CreatureCmd.SetMaxAndCurrentHp(enemy, 10000);
            foreach (var card in player.PlayerCombatState!.Hand.Cards.ToArray()) await CardPileCmd.Add(card, PileType.Discard);
        }
        T Create<T>(bool up = false) where T : CardModel
        { var card = player.Creature.CombatState!.CreateCard<T>(player); if(up){card.UpgradeInternal();card.FinalizeUpgradeInternal();}return card; }
        Task Play(CardModel card, MegaCrit.Sts2.Core.Entities.Creatures.Creature? target = null)
            => CardCmd.AutoPlay(ctx, card, target, skipCardPileVisuals:true);
        Task Dispatch(OrbOperationResult op) => LibrarianRuntime.Dispatch(S(),ctx,op);
        async Task Choose(CardModel card, OrbKind kind)
        {
            var selector = new TestCardSelector();selector.PrepareToSelect(new[]{Array.IndexOf(S().Orbs.Positions.ToArray(),kind)});
            using(CardSelectCmd.PushSelector(selector)) await Play(card);
        }
        async Task Capture(string name)
        {
            string dir=@"D:\Slay The Spire_Mod Dev\outputs\revision-v1.0.0-stable\audit-history\revision-v0.4.3\screenshots";
            System.IO.Directory.CreateDirectory(dir);
            await NGame.Instance!.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            using var img=NGame.Instance.GetViewport().GetTexture().GetImage();Check(img.SavePng(System.IO.Path.Combine(dir,name+".png"))==Error.Ok,"capture "+name);
        }
        async Task CapturePower<T>(string name) where T : PowerModel
        {
            await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(0.4),SceneTreeTimer.SignalName.Timeout);
            var creatureNode=NCombatRoom.Instance!.GetCreatureNode(player.Creature)!;
            var status=creatureNode.FindChildren("*","",true,false).OfType<NPower>().Single(p=>p.Model is T);
            Check(status.IsVisibleInTree() && status.GetNode<TextureRect>("%Icon").Texture is not null,"native status visible "+name);
            status.EmitSignal(Control.SignalName.MouseEntered);
            await NGame.Instance.ToSignal(NGame.Instance.GetTree().CreateTimer(0.2),SceneTreeTimer.SignalName.Timeout);
            await Capture(name);
            status.EmitSignal(Control.SignalName.MouseExited);
        }
        foreach(bool up in new[]{false,true})
        {
            await Reset();var rek=Create<Rekindle>(up);await Play(rek);
            Check(rek.Pile?.Type==PileType.Exhaust,"Rekindle exhaust "+up);
            Check(player.PlayerCombatState!.DrawPile.Cards.Last() is Rekindle copy && copy.IsUpgraded==up && copy.Keywords.Contains(CardKeyword.Exhaust),"Rekindle copied exhaust "+up);
            Check(Create<ArchiveBulwark>(up).EnergyCost.Canonical==0 && Create<OutOfContext>(up).EnergyCost.Canonical==0,"zero costs "+up);
            Check(Create<OutOfContext>(up).DynamicVars.Cards.IntValue==(up?3:2),"OutOfContext draw "+up);
            Check(Create<EmberPierce>(up).DynamicVars.Damage.IntValue==(up?15:11),"Ember damage "+up);
            Check(Create<MultiplayerPlaceholderA>(up).DynamicVars["CrowdKindlingPower"].IntValue==(up?4:3),"Crowd 3/4 "+up);
            var immortal=Create<ImmortalSpark>(up);await Play(immortal);
            Check(immortal.PermanentIncrease==(up?6:4),"Immortal permanent 4/6 "+up);
            var saved=(ImmortalSpark)CardModel.FromSerializable(immortal.ToSerializable());
            Check(saved.PermanentIncrease==immortal.PermanentIncrease,"Immortal saved increments "+up);

            await Reset();await Dispatch(S().Orbs.Gain(OrbKind.Fire,5));
            var secondEnemy = await CreatureCmd.Add<MegaCrit.Sts2.Core.Models.Monsters.Tunneler>(player.Creature.CombatState!);
            await CreatureCmd.SetMaxAndCurrentHp(secondEnemy,10000);
            var foes=player.Creature.CombatState!.HittableEnemies.ToArray();var hp=foes.Select(e=>e.CurrentHp).ToArray();
            Check(foes.Length==2,"two native enemy targets "+up);
            await Play(Create<BookBurning>(up));
            Check(foes.Select((e,i)=>hp[i]-e.CurrentHp).All(loss=>loss==20),"BookBurning four hits on all enemies "+up);
            Check(S().Orbs.Value(OrbKind.Fire)==0,"BookBurning loses fire once "+up);
            await Dispatch(S().Orbs.Activate(OrbKind.Growth,OrbScope.All));hp=foes.Select(e=>e.CurrentHp).ToArray();
            await Play(Create<Rootbind>(up),foes[0]);
            Check(hp[0]-foes[0].CurrentHp==(up?12:9)&&hp[1]==foes[1].CurrentHp,"Rootbind 052 single damage "+up);
            Check(foes[0].GetPower<StrengthPower>()?.Amount==-6&&foes[1].GetPower<StrengthPower>() is null,"Rootbind 052 conditional Strength "+up);

            await Reset();await Play(Create<SeedburialStrike>(up),player.Creature.CombatState!.HittableEnemies.First());
            Check(player.Creature.GetPower<SeedburialPendingPower>()?.Amount==(up?15:11),"Seed pending status "+up);
            if(up) await CapturePower<SeedburialPendingPower>("seed-status");
            S().Orbs.BeginOwnerTurn();await ModelDb.Singleton<LibrarianCombatHooks>().BeforeHandDraw(player,ctx,player.Creature.CombatState!);
            Check(S().Orbs.Value(OrbKind.Growth)==(up?15:11)&&player.Creature.GetPower<SeedburialPendingPower>() is null,"Seed fires and clears "+up);
            for(int i=0;i<2;i++)await CardPileCmd.AddGeneratedCardToCombat(Create<LibrarianDefend>(),PileType.Hand,player);
            await Play(Create<DeepSeaBarrier>(up));
            Check(player.Creature.GetPower<DeepSeaPendingPower>()?.Amount==(up?12:10),"DeepSea actual exhausted count and status "+up);
            if(up)await CapturePower<DeepSeaPendingPower>("delayed-status");
            await ModelDb.Singleton<LibrarianCombatHooks>().BeforeSideTurnEnd(ctx,CombatSide.Player,[player.Creature]);
            Check(S().Orbs.Value(OrbKind.Tide)==(up?12:10)&&player.Creature.GetPower<DeepSeaPendingPower>() is null,"DeepSea end turn gain and clear "+up);

            await Reset();await Play(Create<PracticeMakesPerfect>(up));int energy=player.PlayerCombatState!.Energy;
            int interval=up?3:4;
            for(int i=0;i<interval-1;i++)await Dispatch(S().Orbs.Activate(OrbKind.Fire,OrbScope.All));
            Check(player.PlayerCombatState.Energy==energy,"Practice below threshold "+up);
            await Dispatch(S().Orbs.Gain(OrbKind.Fire,1));
            Check(player.PlayerCombatState.Energy==energy+1,"Practice repeated channel and gain exactly once "+up);
            Check(player.Creature.GetPower<PracticeMakesPerfectPower>()!.DisplayAmount==interval,"Practice display resets "+up);
            await Dispatch(S().Orbs.Lock(OrbKind.Fire));await Dispatch(S().Orbs.Gain(OrbKind.Fire,9));await Dispatch(S().Orbs.Activate(OrbKind.Fire,OrbScope.All));
            Check(player.Creature.GetPower<PracticeMakesPerfectPower>()!.DisplayAmount==interval,"Practice locked attempts ignored "+up);
            Check(!player.Creature.GetPower<PracticeMakesPerfectPower>()!.HoverTips.OfType<MegaCrit.Sts2.Core.HoverTips.HoverTip>().First().Description.Contains('{'),"Practice progress text resolves "+up);

            await Reset();await Dispatch(S().Orbs.Gain(OrbKind.Tide,20));await Dispatch(S().Orbs.Lose(OrbKind.Tide,17,OrbScope.All));await Dispatch(S().Orbs.Lock(OrbKind.Tide));
            var positions=S().Orbs.Positions.ToArray();await Choose(Create<ReRead>(up),OrbKind.Tide);
            Check(S().Orbs.Value(OrbKind.Tide)==20 && S().Orbs.IsLocked(OrbKind.Tide)&&!S().Orbs.IsActivated(OrbKind.Tide)&&positions.SequenceEqual(S().Orbs.Positions),"ReRead history including locked no activation/move "+up);
            await Reset();
            await Dispatch(S().Orbs.Strengthen(OrbKind.Fire,5,OrbScope.All));await Dispatch(S().Orbs.Strengthen(OrbKind.Tide,7,OrbScope.All));await Dispatch(S().Orbs.Strengthen(OrbKind.Growth,3,OrbScope.All));
            int before=player.Creature.Block;var unity=Create<ThreefoldUnity>(up);await Choose(unity,OrbKind.Tide);
            Check(S().Orbs.Value(OrbKind.Tide)==15&&S().Orbs.Value(OrbKind.Fire)==0&&S().Orbs.Value(OrbKind.Growth)==0,"Threefold transfers values "+up);
            Check(player.Creature.Block-before==45&&S().Orbs.SettlementsThisCombat==3&&unity.Pile?.Type==PileType.Exhaust,"Threefold settles three times and exhausts "+up);

            await Reset();var continued=Create<ToBeContinued>(up);await Play(continued);
            var chosen=Create<LibrarianDefend>();await CardPileCmd.Add(chosen,PileType.Hand);await CardPileCmd.Add(Create<LibrarianStrike>(),PileType.Draw,CardPilePosition.Top);
            var selector=new TestCardSelector();selector.PrepareToSelect(new[]{chosen});
            using(CardSelectCmd.PushSelector(selector))await player.Creature.GetPower<ToBeContinuedPower>()!.AfterPlayerTurnStart(ctx,player);
            Check(chosen.Pile?.Type==PileType.Draw&&player.PlayerCombatState!.DrawPile.Cards.Last()==chosen&&player.PlayerCombatState.Hand.Cards.Count==1,"Continued native draw then selected bottom "+up);
            Check(continued.Keywords.Contains(CardKeyword.Innate)==up,"Continued innate upgrade only "+up);
        }
        await Reset();await Play(Create<Lifeline>());await Dispatch(S().Orbs.Activate(OrbKind.Tide,OrbScope.All));
        Check(S().Orbs.Value(OrbKind.Tide)==1,"Lifeline channel strengthens current");
        await Play(Create<Spark>());Check(S().Orbs.Value(OrbKind.Fire)==7,"Lifeline gain channel plus card play two triggers");
        await Reset();await Play(Create<PracticeMakesPerfect>());await Play(Create<PracticeMakesPerfect>(true));int initial=player.PlayerCombatState!.Energy;
        for(int i=0;i<12;i++)await Dispatch(S().Orbs.Activate(OrbKind.Fire,OrbScope.All));
        Check(player.PlayerCombatState.Energy-initial==7,"Practice mixed cycles 4 and 3 independent");

        await Reset();await Play(Create<EndlessTide>());await Play(Create<EndlessTide>(true));
        Check(player.Creature.GetPower<EndlessTidePower>()!.Amount==1,"EndlessTide single nonstacking rule");
        S().Waves.Add(15);await Dispatch(S().Orbs.Gain(OrbKind.Tide,10));
        await ModelDb.Singleton<LibrarianCombatHooks>().BeforeSideTurnEnd(ctx,CombatSide.Player,[player.Creature]);
        Check(player.Creature.Block==25&&S().Waves.Amount==12,"EndlessTide W15 plus Tide10 gives25 then one decay");
        await Reset();await Play(Create<LifeSymphony>());
        Check(player.Creature.GetPower<LifeSymphonyPendingPower>()?.Amount==1,"LifeSymphony visible extra settlement");
        await ModelDb.Singleton<LibrarianCombatHooks>().BeforeSideTurnEnd(ctx,CombatSide.Player,[player.Creature]);
        Check(S().Orbs.SettlementsThisCombat==2&&player.Creature.GetPower<LifeSymphonyPendingPower>() is null,"LifeSymphony natural and extra then clears");

        await Reset();var combat=(CombatState)player.Creature.CombatState!;
        var ally=Player.CreateForNewRun<Ironclad>(UnlockState.all,904301);ally.RunState=player.RunState;ally.ResetCombatState();combat.AddPlayer(ally);extras.Add(ally);
        ally.AddRelicInternal(ModelDb.Relic<Vambrace>().ToMutable(),silent:true);await ally.Relics.OfType<Vambrace>().Single().BeforeCombatStart();
        await PowerCmd.Apply<DexterityPower>(ctx,player.Creature,2,player.Creature,null);
        await PowerCmd.Apply<DexterityPower>(ctx,ally.Creature,7,ally.Creature,null);
        await Play(Create<SharedShelter>(),ally.Creature);
        Check(player.Creature.Block==10&&ally.Creature.Block==10,"Shelter copies caster adjusted amount without recipient modifiers");
        var ownDefend=combat.CreateCard<MegaCrit.Sts2.Core.Models.Cards.DefendIronclad>(ally);await Play(ownDefend,ally.Creature);
        Check(ally.Creature.Block==34,"Shelter does not consume recipient Vambrace");
        await CreatureCmd.SetCurrentHp(ally.Creature,0);var alone=Create<SharedShelter>();alone.SetToFreeThisTurn();
        Check(alone.TargetType==TargetType.Self&&alone.CanPlay(),"Shelter playable with all allies dead");int previous=player.Creature.Block;await Play(alone);Check(player.Creature.Block-previous==10,"Shelter lone survivor gains block");

        await Reset();foreach(var c in player.PlayerCombatState!.DrawPile.Cards.Concat(player.PlayerCombatState.DiscardPile.Cards).ToArray())await CardPileCmd.Add(c,PileType.Exhaust);
        var bottom=Create<LibrarianDefend>();await CardPileCmd.Add(bottom,PileType.Discard);int block=player.Creature.Block;await Play(Create<ReadBackward>());
        Check(player.Creature.Block-block==5&&bottom.Pile?.Type==PileType.Discard,"ReadBackward empty draw reshuffles then plays once");
        foreach(var c in player.PlayerCombatState.DiscardPile.Cards.ToArray())await CardPileCmd.Add(c,PileType.Exhaust);
        block=player.Creature.Block;await Play(Create<ReadBackward>());Check(player.Creature.Block==block,"ReadBackward both piles empty terminates");

        await Reset();await CreatureCmd.SetMaxAndCurrentHp(player.Creature,92);await CreatureCmd.SetCurrentHp(player.Creature,34);
        S().Waves.Add(24);await CreatureCmd.GainBlock(player.Creature,15,ValueProp.Unpowered,null);
        var node=NCombatRoom.Instance!.GetCreatureNode(player.Creature)!;var health=node.FindChildren("*","",true,false).OfType<NHealthBar>().First();
        var band=health.FindChildren("*","",true,false).OfType<LibrarianWaveBar>().Single();var label=health.GetNode<Label>("%HpLabel");
        Check(band.GetParent()==label.GetParent()&&band.GetIndex()<label.GetIndex()&&band.ZIndex==0,"Wave below native HP text");
        var originalScale=health.Scale;
        foreach(float scale in new[]{1f,0.65f}){health.Scale=originalScale*scale;await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(0.3),SceneTreeTimer.SignalName.Timeout);Check(band.Visible&&Math.Abs(band.Position.Y-(health.GetNode<Control>("%HpForegroundContainer").Position.Y-7))<0.1,"Wave local position stable scale "+scale);await Capture("wave-scale-"+scale.ToString(System.Globalization.CultureInfo.InvariantCulture));}
        health.Scale=originalScale;
        await CaptureCards(player,Capture);
        MainFile.Logger.Info($"CARD043_NATIVE_AUDIT_PASS checks={checks} liveMulticlient=False");
    }
    private static async Task CaptureCards(Player player,Func<string,Task> capture)
    {
        var layer=new CanvasLayer{Layer=120};NGame.Instance!.AddChild(layer);var panel=new Control();layer.AddChild(panel);var size=NGame.Instance.GetViewport().GetVisibleRect().Size;panel.AddChild(new ColorRect{Color=new Color("20272e"),Size=size});
        CardModel[] models=[ModelDb.Card<PracticeMakesPerfect>().ToMutable(),ModelDb.Card<ToBeContinued>().ToMutable(),ModelDb.Card<ReRead>().ToMutable(),ModelDb.Card<EndlessTide>().ToMutable(),ModelDb.Card<ThornBurst>().ToMutable(),ModelDb.Card<ThreefoldUnity>().ToMutable(),ModelDb.Card<SharedShelter>().ToMutable(),ModelDb.Card<Rootbind>().ToMutable()];
        try{for(int page=0;page<2;page++){var nodes=new List<MegaCrit.Sts2.Core.Nodes.Cards.NCard>();for(int i=0;i<4;i++){var n=MegaCrit.Sts2.Core.Assets.PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<MegaCrit.Sts2.Core.Nodes.Cards.NCard>();panel.AddChild(n);n.Model=models[page*4+i];n.UpdateVisuals(PileType.None,CardPreviewMode.Normal);n.Scale=Vector2.One*Math.Min(size.X/1360,size.Y/530);n.Position=new(size.X*(i+0.5f)/4,size.Y/2);nodes.Add(n);}await capture("cards-base-"+page);foreach(var n in nodes){n.Model.UpgradeInternal();n.Model.FinalizeUpgradeInternal();n.UpdateVisuals(PileType.None,CardPreviewMode.Normal);}await capture("cards-upgraded-"+page);foreach(var n in nodes){panel.RemoveChild(n);n.QueueFree();}}}finally{layer.QueueFree();}
    }
}
