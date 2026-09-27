using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Cards.OrbAdvancedBasics;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.Mechanics;

internal static class DevelopmentRevision054Audit
{
    internal static async Task Run(Player player, Func<Task> freshFight)
    {
        var ctx = new ThrowingPlayerChoiceContext(); int checks = 0;
        LibrarianSession S() => LibrarianRuntime.Get(player);
        Creature Enemy() => player.Creature.CombatState!.HittableEnemies.First();
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException("054: " + label); checks++; MainFile.Logger.Info("CARD054_CHECK_PASS " + label); }
        async Task Reset() { await freshFight(); for(int i=0;player.PlayerCombatState?.Phase!=PlayerTurnPhase.Play;i++) { if(i>1200)throw new TimeoutException("054 play");await NGame.Instance!.ToSignal(NGame.Instance.GetTree(),SceneTree.SignalName.ProcessFrame); } }
        T Create<T>(bool up) where T:CardModel { var c=player.Creature.CombatState!.CreateCard<T>(player);if(up){c.UpgradeInternal();c.FinalizeUpgradeInternal();}return c; }
        Task Play(CardModel c, Creature? target=null) => CardCmd.AutoPlay(ctx,c,target,skipCardPileVisuals:true);
        Task Dispatch(OrbOperationResult r) => LibrarianRuntime.Dispatch(S(),ctx,r);
        async Task Damage(int n, ValueProp prop=ValueProp.Unpowered) { await CreatureCmd.Damage(ctx,player.Creature,n,prop,Enemy()); }
        foreach(bool up in new[]{false,true})
        {
            await Reset();var spark=Create<AncientSpark>(up);int hp=Enemy().CurrentHp;
            await Play(spark);Check(S().Orbs.Value(OrbKind.Fire)==7,"AncientSpark Fire7 "+up);
            Check(hp-Enemy().CurrentHp==14,"AncientSpark two settlements "+up);
            Check(spark.EnergyCost.GetWithModifiers(CostModifiers.None)==(up?0:1),"AncientSpark cost "+up);
            for(int locks=0;locks<=3;locks++)
            {
                await Reset();foreach(var kind in new[]{OrbKind.Fire,OrbKind.Tide,OrbKind.Growth}.Take(locks))await Dispatch(S().Orbs.Lock(kind));
                var c=Create<Ignite>(up);await Play(c);Check(player.Creature.Block==(up?10:7)+locks*5,"Ignite locked formula "+up+" "+locks);
            }
            await Reset();var warmth=Create<ResidualWarmth>(up);
            Check(warmth.DynamicVars.Block.BaseValue==(up?11:8)&&warmth.DynamicVars["Fire"].BaseValue==(up?11:8),"ResidualWarmth both values "+up);
            bool attacks=Enemy().Monster!.NextMove.Intents.Any(x=>x.IntentType==MegaCrit.Sts2.Core.MonsterMoves.Intents.IntentType.Attack);
            await Play(warmth,Enemy());Check(attacks?player.Creature.Block==(up?11:8):S().Orbs.Value(OrbKind.Fire)==(up?11:8),"ResidualWarmth native intent "+up);
            foreach(bool attack in new[]{false,true})
            {
                await Reset();var e=Enemy();
                typeof(MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine.MoveState).GetProperty("Intents")!.SetValue(e.Monster!.NextMove,
                    attack ? new AbstractIntent[]{new SingleAttackIntent(1),new BuffIntent()} : new AbstractIntent[]{new BuffIntent()});
                await Play(Create<ResidualWarmth>(up),e);
                Check(player.Creature.Block==(attack?(up?11:8):0)&&S().Orbs.Value(OrbKind.Fire)==(attack?0:(up?11:8)),"ResidualWarmth selected intent branch "+up+" "+attack);
            }
            foreach(int growth in new[]{0,3,8,15})
            {
                await Reset();await Dispatch(S().Orbs.Strengthen(OrbKind.Growth,growth,OrbScope.All));
                await Dispatch(S().Orbs.Lock(OrbKind.Growth));
                int hand=player.PlayerCombatState!.Hand.Cards.Count;await Play(Create<SproutingBulwark>(up));
                Check(player.Creature.Block==(up?10:7)&&player.PlayerCombatState.Hand.Cards.Count==hand,"SproutingBulwark block and no draw "+up);
                var p=player.Creature.GetPower<SproutingBulwarkPower>()!;
                Check(p!=null&&p.Icon!=null&&p.BigIcon!=null&&p.Title.GetFormattedText()=="萌发护壁","native status title icons");
                for(int i=0;i<3;i++)p!.ModifyHpLostAfterOstyLate(player.Creature,8,ValueProp.Unpowered,Enemy(),null);
                Check(S().Orbs.Value(OrbKind.Growth)==growth,"preview does not spend Growth");
                int before=player.Creature.CurrentHp;await Damage((up?10:7)+8);
                Check(before-player.Creature.CurrentHp==Math.Max(8-growth,0)&&S().Orbs.Value(OrbKind.Growth)==Math.Max(growth-8,0),"block first then bounded Growth "+up+" "+growth);
                before=player.Creature.CurrentHp;int remaining=S().Orbs.Value(OrbKind.Growth);await Damage(8);
                Check(before-player.Creature.CurrentHp==8-Math.Min(remaining,8)&&S().Orbs.Value(OrbKind.Growth)==0,"second hit uses remaining Growth");
            }
            await Reset();await Dispatch(S().Orbs.Strengthen(OrbKind.Growth,20,OrbScope.All));await Play(Create<SproutingBulwark>(up));await Play(Create<SproutingBulwark>(up));
            Check(player.Creature.GetPower<SproutingBulwarkPower>()?.Amount==1,"reapplication single status");
            await Damage(2*(up?10:7));Check(S().Orbs.Value(OrbKind.Growth)==20,"fully blocked preserves Growth");
            await Damage(4);Check(S().Orbs.Value(OrbKind.Growth)==16,"reapplication spends once");
            int hpBefore=player.Creature.CurrentHp;await Damage(2,ValueProp.Unblockable);Check(player.Creature.CurrentHp==hpBefore-2&&S().Orbs.Value(OrbKind.Growth)==16,"direct HP loss remains HP loss");
        }
        await Reset();await Dispatch(S().Orbs.Strengthen(OrbKind.Growth,100,OrbScope.All));await Play(Create<SproutingBulwark>(false));
        int turn=player.Creature.CombatState!.RoundNumber;int beforeTurn=player.Creature.CurrentHp;
        CombatManager.Instance.SetReadyToEndTurn(player,false);
        for(int i=0;player.Creature.CombatState!.RoundNumber==turn||player.PlayerCombatState!.Phase!=PlayerTurnPhase.Play;i++)
        {if(i>3600)throw new TimeoutException("054 native end turn");await NGame.Instance!.ToSignal(NGame.Instance.GetTree(),SceneTree.SignalName.ProcessFrame);}
        Check(player.Creature.CurrentHp==beforeTurn&&S().Orbs.Value(OrbKind.Growth)<100,"native enemy turn protected and spent Growth");
        Check(player.Creature.GetPower<SproutingBulwarkPower>() is null,"expires at native owner next turn");
        int g=S().Orbs.Value(OrbKind.Growth);int hpAfter=player.Creature.CurrentHp;await Damage(player.Creature.Block+3);
        Check(player.Creature.CurrentHp==hpAfter-3&&S().Orbs.Value(OrbKind.Growth)==g,"no residual absorption next turn");
        await Reset();Check(player.Creature.GetPower<SproutingBulwarkPower>() is null,"no carry into next combat");
        var layer=new CanvasLayer{Layer=120};NGame.Instance!.AddChild(layer);var size=NGame.Instance.GetViewport().GetVisibleRect().Size;var panel=new Control();layer.AddChild(panel);panel.AddChild(new ColorRect{Color=new Color("20272e"),Size=size});
        try
        {
            CardModel[] models=[ModelDb.Card<AncientSpark>().ToMutable(),ModelDb.Card<SproutingBulwark>().ToMutable(),ModelDb.Card<ResidualWarmth>().ToMutable(),ModelDb.Card<Ignite>().ToMutable()];var nodes=new List<NCard>();
            for(int i=0;i<models.Length;i++){var n=MegaCrit.Sts2.Core.Assets.PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>();panel.AddChild(n);n.Model=models[i];n.UpdateVisuals(PileType.None,CardPreviewMode.Normal);n.Scale=Vector2.One*Math.Min(size.X/1480,size.Y/570);n.Position=new(size.X*(i+0.5f)/4,size.Y/2);nodes.Add(n);}
            foreach(bool up in new[]{false,true}){if(up)foreach(var n in nodes){n.Model.UpgradeInternal();n.Model.FinalizeUpgradeInternal();n.UpdateVisuals(PileType.None,CardPreviewMode.Normal);}await NGame.Instance.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);using var img=NGame.Instance.GetViewport().GetTexture().GetImage();string dir=System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT")!;System.IO.Directory.CreateDirectory(dir);Check(img.SavePng(System.IO.Path.Combine(dir,up?"cards-upgraded.png":"cards-base.png"))==Error.Ok,"capture cards "+up);}
        }
        finally {layer.QueueFree();}
        MainFile.Logger.Info($"CARD054_AUDIT_PASS checks={checks} nativeTurn=True");
    }
}
