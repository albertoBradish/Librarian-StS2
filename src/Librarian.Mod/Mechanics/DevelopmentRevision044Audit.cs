using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards.OrbBasics;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.TestSupport;

namespace Librarian.Mechanics;
internal static class DevelopmentRevision044Audit
{
    internal static async Task Run(Player player,Func<Task> freshFight)
    {
        var ctx=new ThrowingPlayerChoiceContext();int checks=0;
        LibrarianSession S()=>LibrarianRuntime.Get(player);
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException("044: "+label);checks++;MainFile.Logger.Info("CARD044_CHECK_PASS "+label);}
        async Task Reset(){await freshFight();for(int i=0;player.PlayerCombatState?.Phase!=PlayerTurnPhase.Play;i++){if(i>1200)throw new TimeoutException("044 play");await NGame.Instance!.ToSignal(NGame.Instance.GetTree(),SceneTree.SignalName.ProcessFrame);}foreach(var e in player.Creature.CombatState!.HittableEnemies)await CreatureCmd.SetMaxAndCurrentHp(e,10000);}
        T Create<T>(bool up) where T:CardModel{var c=player.Creature.CombatState!.CreateCard<T>(player);if(up){c.UpgradeInternal();c.FinalizeUpgradeInternal();}return c;}
        Task Play(CardModel c)=>CardCmd.AutoPlay(ctx,c,null,skipCardPileVisuals:true);
        Task Dispatch(OrbOperationResult r)=>LibrarianRuntime.Dispatch(S(),ctx,r);
        foreach(bool up in new[]{false,true})
        {
            foreach(bool locked in new[]{false,true})
            {
                await Reset();foreach(var k in S().Orbs.Positions.ToArray()){await Dispatch(S().Orbs.Strengthen(k,5,OrbScope.All));await Dispatch(S().Orbs.Extinguish(k,OrbScope.All));}
                if(locked)await Dispatch(S().Orbs.Lock(OrbKind.Tide));
                var expected=Enumerable.Range(0,up?2:1).SelectMany(_=>S().Orbs.Positions.Where(k=>!S().Orbs.IsLocked(k))).ToArray();
                var order=new List<OrbKind>();DevelopmentRevision040CardsAudit.SettlementObserver=(session,request)=>{if(session.Player==player)order.Add(request.Orb);};
                var card=Create<TreeRingBurst>(up);
                try{await Play(card);}finally{DevelopmentRevision040CardsAudit.SettlementObserver=null;}
                Check(order.SequenceEqual(expected),$"inactive rotation exact rounds up={up} locked={locked}");
                Check(S().Orbs.Positions.All(k=>!S().Orbs.IsActivated(k)),"rotation leaves inactive state unchanged");
                Check(card.EnergyCost.Canonical==2&&card.Pile?.Type==PileType.Exhaust,"rotation cost/exhaust unchanged");
                Check(locked?player.Creature.Block==0:player.Creature.Block>0,"rotation resolves inactive Tide but skips locked Tide");
            }
            await Reset();await Play(Create<PracticeMakesPerfect>(up));int energy=player.PlayerCombatState!.Energy;
            for(int i=0;i<(up?3:4);i++)await Dispatch(S().Orbs.Activate(OrbKind.Fire,OrbScope.All));
            Check(player.PlayerCombatState.Energy==energy+1,"Practice energy reward preserved "+up);
            Check(Create<PracticeMakesPerfect>(up).GetDescriptionForPile(PileType.None).Contains("energy_triangle_text.svg"),"Practice native card energy icon "+up);
            Check(player.Creature.GetPower<PracticeMakesPerfectPower>()!.HoverTips.OfType<MegaCrit.Sts2.Core.HoverTips.HoverTip>().First().Description.Contains("energy_triangle_text.svg"),"Practice status energy icon "+up);
            await Reset();foreach(var c in player.PlayerCombatState!.DrawPile.Cards.Concat(player.PlayerCombatState.DiscardPile.Cards).ToArray())await CardPileCmd.Add(c,PileType.Exhaust);
            var defend=Create<LibrarianDefend>(false);await CardPileCmd.Add(defend,PileType.Discard);int block=player.Creature.Block;var read=Create<ReadBackward>(up);await Play(read);
            Check(player.Creature.Block-block==5,"ReadBackward hidden shuffle still plays once "+up);
            Check(!read.GetDescriptionForPile(PileType.None).Contains("洗")&&!read.GetDescriptionForPile(PileType.None).Contains("弃牌"),"ReadBackward no second sentence "+up);
        }
        var layer=new CanvasLayer{Layer=120};NGame.Instance!.AddChild(layer);var size=NGame.Instance.GetViewport().GetVisibleRect().Size;var panel=new Control();layer.AddChild(panel);panel.AddChild(new ColorRect{Color=new Color("20272e"),Size=size});
        try{var models=new CardModel[]{ModelDb.Card<ReadBackward>().ToMutable(),ModelDb.Card<PracticeMakesPerfect>().ToMutable(),ModelDb.Card<TreeRingBurst>().ToMutable()};var nodes=new List<NCard>();for(int i=0;i<3;i++){var n=MegaCrit.Sts2.Core.Assets.PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>();panel.AddChild(n);n.Model=models[i];n.UpdateVisuals(PileType.None,CardPreviewMode.Normal);n.Scale=Vector2.One*Math.Min(size.X/1120,size.Y/570);n.Position=new(size.X*(i+0.5f)/3,size.Y/2);nodes.Add(n);}foreach(bool up in new[]{false,true}){if(up)foreach(var n in nodes){n.Model.UpgradeInternal();n.Model.FinalizeUpgradeInternal();n.UpdateVisuals(PileType.None,CardPreviewMode.Normal);}await NGame.Instance.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);using var img=NGame.Instance.GetViewport().GetTexture().GetImage();string dir=@"D:\Slay The Spire_Mod Dev\outputs\revision-v1.0.0-stable\audit-history\revision-v0.4.4\screenshots";System.IO.Directory.CreateDirectory(dir);Check(img.SavePng(System.IO.Path.Combine(dir,up?"cards-upgraded.png":"cards-base.png"))==Error.Ok,"capture cards "+up);}}finally{layer.QueueFree();}
        MainFile.Logger.Info($"CARD044_NATIVE_AUDIT_PASS checks={checks}");
    }
}
