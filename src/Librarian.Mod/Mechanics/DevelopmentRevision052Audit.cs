using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.OrbBasics;
using Librarian.LibrarianCode.Cards.OrbAdvancedBasics;
using Librarian.LibrarianCode.Cards.Stateful;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.ValueProps;

namespace Librarian.Mechanics;

internal static class DevelopmentRevision052Audit
{
    internal static async Task Run(Player player, Func<Task> freshFight)
    {
        var ctx = new ThrowingPlayerChoiceContext(); int checks = 0;
        LibrarianSession S() => LibrarianRuntime.Get(player);
        Creature Enemy() => player.Creature.CombatState!.HittableEnemies.First();
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException("052: " + label); checks++; MainFile.Logger.Info("CARD052_CHECK_PASS " + label); }
        async Task Reset() { await freshFight(); for (int i=0;player.PlayerCombatState?.Phase!=PlayerTurnPhase.Play;i++) { if(i>1200)throw new TimeoutException("052 play");await NGame.Instance!.ToSignal(NGame.Instance.GetTree(),SceneTree.SignalName.ProcessFrame); } }
        T Create<T>(bool up) where T:CardModel { var c=player.Creature.CombatState!.CreateCard<T>(player); if(up){c.UpgradeInternal();c.FinalizeUpgradeInternal();} return c; }
        Task Play(CardModel c, Creature? target=null) => CardCmd.AutoPlay(ctx,c,target,skipCardPileVisuals:true);
        Task Dispatch(OrbOperationResult r) => LibrarianRuntime.Dispatch(S(),ctx,r);
        int Strength(Creature e) => e.GetPower<StrengthPower>()?.Amount ?? 0;
        foreach(bool up in new[]{false,true})
        {
            foreach(bool active in new[]{false,true})
            {
                await Reset();var e=Enemy();var other=await CreatureCmd.Add<MegaCrit.Sts2.Core.Models.Monsters.Tunneler>(player.Creature.CombatState!);await CreatureCmd.SetMaxAndCurrentHp(other,10000);
                await Dispatch(S().Orbs.Strengthen(OrbKind.Growth,5,OrbScope.All));
                if(active)await Dispatch(S().Orbs.Activate(OrbKind.Growth,OrbScope.All));
                int hp=e.CurrentHp, otherHp=other.CurrentHp;var c=Create<Rootbind>(up);await Play(c,e);
                Check(hp-e.CurrentHp==(up?12:9)&&other.CurrentHp==otherHp,"Rootbind single damage "+up+" "+active);
                Check(Strength(e)==(active?-6:0)&&Strength(other)==0,"Rootbind conditional single Strength "+up+" "+active);
                Check(S().Orbs.Value(OrbKind.Growth)==5&&S().Orbs.IsActivated(OrbKind.Growth)==active,"Rootbind no Growth mutation");
                Check(e.GetPower<WeakPower>() is null,"Rootbind no Weak");
                Check(c.TargetType==TargetType.AnyEnemy&&c.EnergyCost.Canonical==1&&c.Rarity==CardRarity.Uncommon,"Rootbind metadata");
                if(active)
                {
                    await Play(Create<Rootbind>(up),e);Check(Strength(e)==-12&&e.GetPower<RootbindPower>()?.Amount==12,"Rootbind stacks");
                    var power=e.GetPower<RootbindPower>()!;
                    Check(power.Title.GetFormattedText()==c.TitleLocString.GetFormattedText()&&power.Icon!=null&&power.BigIcon!=null,"Rootbind native status title and icons");
                    await power.AfterSideTurnEnd(ctx,CombatSide.Player,[player.Creature]);Check(Strength(e)==-12,"Rootbind persists through player side end");
                    await power.AfterSideTurnEnd(ctx,CombatSide.Enemy,[e,other]);Check(Strength(e)==0&&e.GetPower<RootbindPower>() is null,"Rootbind restores after enemy side end");
                }
            }
            foreach(int growth in new[]{0,7,20})
            {
                await Reset();var e=Enemy();await Dispatch(S().Orbs.Strengthen(OrbKind.Growth,growth,OrbScope.All));
                if(growth==20)await Dispatch(S().Orbs.Lock(OrbKind.Growth));
                await PowerCmd.Apply<StrengthPower>(ctx,player.Creature,3,player.Creature,null);
                await CreatureCmd.GainBlock(e,2,ValueProp.Unpowered,null);
                int hp=e.CurrentHp;var c=Create<Bookworm>(up);await Play(c,e);
                Check(hp-e.CurrentHp==growth+1,"Bookworm damage with Strength and block "+up+" "+growth);
                Check(Strength(e)==-growth,"Bookworm Strength uses Growth not damage "+up+" "+growth);
                Check(S().Orbs.Value(OrbKind.Growth)==growth&&!S().Orbs.IsActivated(OrbKind.Growth),"Bookworm works inactive/locked without changing Growth");
                Check(c.EnergyCost.GetWithModifiers(CostModifiers.None)==(up?0:1)&&c.Rarity==CardRarity.Rare&&c.Pile?.Type==PileType.Exhaust,"Bookworm 1/0 rare Exhaust");
                if(growth>0)
                {
                    var p=e.GetPower<BookwormPower>()!;Check(p.Amount==growth&&p.Icon!=null&&p.BigIcon!=null&&p.Title.GetFormattedText()==c.TitleLocString.GetFormattedText(),"Bookworm status title and native icons");
                    await p.AfterSideTurnEnd(ctx,CombatSide.Enemy,[e]);Check(Strength(e)==0&&e.GetPower<BookwormPower>() is null,"Bookworm exact restore");
                }
                else Check(e.GetPower<BookwormPower>() is null,"zero Growth creates no status");
            }
            await Reset();var enemy=Enemy();await Dispatch(S().Orbs.Gain(OrbKind.Growth,8));await PowerCmd.Apply<ArtifactPower>(ctx,enemy,1,enemy,null);await Play(Create<Rootbind>(up),enemy);
            Check(Strength(enemy)==0&&enemy.GetPower<RootbindPower>() is null&&enemy.GetPower<ArtifactPower>() is null,"Artifact blocks Rootbind once");
            await PowerCmd.Apply<ArtifactPower>(ctx,enemy,1,enemy,null);await Play(Create<Bookworm>(up),enemy);
            Check(Strength(enemy)==0&&enemy.GetPower<BookwormPower>() is null&&enemy.GetPower<ArtifactPower>() is null,"Artifact blocks Bookworm once");
            await PowerCmd.Apply<StrengthPower>(ctx,enemy,4,enemy,null);await Play(Create<Rootbind>(up),enemy);await Play(Create<Bookworm>(up),enemy);
            Check(Strength(enemy)==-10,"two sources combine with existing Strength");
            await enemy.GetPower<RootbindPower>()!.AfterSideTurnEnd(ctx,CombatSide.Enemy,[enemy]);await enemy.GetPower<BookwormPower>()!.AfterSideTurnEnd(ctx,CombatSide.Enemy,[enemy]);Check(Strength(enemy)==4,"two sources restore original Strength");
            await Reset();enemy=Enemy();int before=enemy.CurrentHp;await Play(Create<BurnTheRiver>(up));Check(S().Orbs.Value(OrbKind.Fire)==(up?10:7)&&before-enemy.CurrentHp==(up?10:7),"BurnTheRiver gain and settlement "+up);
            var shelter=Create<SharedShelter>(up);Check(!shelter.GetDescriptionForPile(PileType.None).Contains("相关效果"),"SharedShelter wording removed "+up);
            await Reset();enemy=Enemy();await Dispatch(S().Orbs.Strengthen(OrbKind.Growth,30,OrbScope.All));await CreatureCmd.SetCurrentHp(enemy,1);var potions=player.Potions.ToArray();await Play(Create<Bookworm>(up),enemy);Check(player.Potions.SequenceEqual(potions),"Bookworm lethal no potion "+up);
        }
        await Reset();
        var turnEnemy=Enemy();await Dispatch(S().Orbs.Gain(OrbKind.Growth,8));
        await Play(Create<Rootbind>(false),turnEnemy);await Play(Create<Bookworm>(false),turnEnemy);
        Check(Strength(turnEnemy)==-14,"real turn setup");
        int turn=player.Creature.CombatState!.RoundNumber;
        CombatManager.Instance.SetReadyToEndTurn(player,false);
        for(int i=0;player.Creature.CombatState!.RoundNumber==turn||player.PlayerCombatState!.Phase!=PlayerTurnPhase.Play;i++)
        {if(i>3600)throw new TimeoutException("052 native end turn");await NGame.Instance!.ToSignal(NGame.Instance.GetTree(),SceneTree.SignalName.ProcessFrame);}
        Check(Strength(turnEnemy)==0&&turnEnemy.GetPower<RootbindPower>() is null&&turnEnemy.GetPower<BookwormPower>() is null,"real enemy turn restores both statuses");
        var layer=new CanvasLayer{Layer=120};NGame.Instance!.AddChild(layer);var size=NGame.Instance.GetViewport().GetVisibleRect().Size;var panel=new Control();layer.AddChild(panel);panel.AddChild(new ColorRect{Color=new Color("20272e"),Size=size});
        try
        {
            CardModel[] models=[ModelDb.Card<Rootbind>().ToMutable(),ModelDb.Card<BurnTheRiver>().ToMutable(),ModelDb.Card<SharedShelter>().ToMutable(),ModelDb.Card<Bookworm>().ToMutable()];var nodes=new List<NCard>();
            for(int i=0;i<models.Length;i++){var n=MegaCrit.Sts2.Core.Assets.PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>();panel.AddChild(n);n.Model=models[i];n.UpdateVisuals(PileType.None,CardPreviewMode.Normal);n.Scale=Vector2.One*Math.Min(size.X/1480,size.Y/570);n.Position=new(size.X*(i+0.5f)/4,size.Y/2);nodes.Add(n);}
            foreach(bool up in new[]{false,true}){if(up)foreach(var n in nodes){n.Model.UpgradeInternal();n.Model.FinalizeUpgradeInternal();n.UpdateVisuals(PileType.None,CardPreviewMode.Normal);}await NGame.Instance.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);using var img=NGame.Instance.GetViewport().GetTexture().GetImage();string dir=System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT") ?? @"D:\Slay The Spire_Mod Dev\outputs\revision-v1.0.0-stable\audit-history\revision-v0.5.2\screenshots";System.IO.Directory.CreateDirectory(dir);Check(img.SavePng(System.IO.Path.Combine(dir,up?"cards-upgraded.png":"cards-base.png"))==Error.Ok,"capture cards "+up);}
        }
        finally {layer.QueueFree();}
        MainFile.Logger.Info($"CARD052_NATIVE_AUDIT_PASS checks={checks}");
    }
}
