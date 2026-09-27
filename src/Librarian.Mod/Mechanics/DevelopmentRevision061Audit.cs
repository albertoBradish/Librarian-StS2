using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.OrbBasics;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Cards.OrbAdvancedBasics;
using Librarian.LibrarianCode.Cards.Stateful;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace Librarian.Mechanics;

internal static class DevelopmentRevision061Audit
{
    internal static async Task Run(Player player, Func<Task> freshFight)
    {
        var ctx = new ThrowingPlayerChoiceContext(); int checks = 0;
        LibrarianSession S() => LibrarianRuntime.Get(player);
        Creature Enemy() => player.Creature.CombatState!.HittableEnemies.First();
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException("061: " + label); checks++; MainFile.Logger.Info("CARD061_CHECK_PASS " + label); }
        async Task Reset() { await freshFight(); for(int i=0;player.PlayerCombatState?.Phase!=PlayerTurnPhase.Play;i++) { if(i>1200)throw new TimeoutException("061 play");await NGame.Instance!.ToSignal(NGame.Instance.GetTree(),SceneTree.SignalName.ProcessFrame); } }
        T Create<T>(bool up=false) where T:CardModel { var c=player.Creature.CombatState!.CreateCard<T>(player);if(up){c.UpgradeInternal();c.FinalizeUpgradeInternal();}return c; }
        Task Play(CardModel c, Creature? target=null) => CardCmd.AutoPlay(ctx,c,target,skipCardPileVisuals:true);
        Task Dispatch(OrbOperationResult r) => LibrarianRuntime.Dispatch(S(),ctx,r);
        async Task Turn() { int turn=player.Creature.CombatState!.RoundNumber;CombatManager.Instance.SetReadyToEndTurn(player,false);for(int i=0;player.Creature.CombatState!.RoundNumber==turn||player.PlayerCombatState!.Phase!=PlayerTurnPhase.Play;i++){if(i>3600)throw new TimeoutException("061 native turn");await NGame.Instance!.ToSignal(NGame.Instance.GetTree(),SceneTree.SignalName.ProcessFrame);} }
        foreach(bool up in new[]{false,true})
        {
            await Reset();
            Check(Create<BurnTheRiver>(up).DynamicVars["Fire"].BaseValue==(up?9:6),"BurnTheRiver "+up);
            Check(Create<SteamBlast>(up).DynamicVars.Damage.BaseValue==(up?25:20),"SteamBlast "+up);
            Check(Create<EmberPierce>(up).DynamicVars.Damage.BaseValue==(up?16:12),"EmberPierce "+up);
            Check(Create<SharedShelter>(up).DynamicVars.Block.BaseValue==(up?12:9),"SharedShelter "+up);
            Check(Create<SproutingSeed>(up).DynamicVars["Growth"].BaseValue==(up?10:8),"SproutingSeed "+up);
            Check(Create<VineShield>(up).DynamicVars.Block.BaseValue==(up?14:11)&&Create<VineShield>(up).DynamicVars["Growth"].BaseValue==(up?6:4),"VineShield "+up);
            Check(Create<SeedburialStrike>(up).DynamicVars["Growth"].BaseValue==(up?16:12),"SeedburialStrike "+up);
            Check(Create<Renewal>(up).DynamicVars["Growth"].BaseValue==(up?12:8),"Renewal "+up);
            Check(Create<OpeningTide>(up).DynamicVars["Tide"].BaseValue==(up?8:5),"OpeningTide "+up);
            var rekindle=Create<Rekindle>(up);rekindle.EnchantInternal(ModelDb.Enchantment<Glam>().ToMutable(),1);
            Check(rekindle.EnergyCost.GetWithModifiers(CostModifiers.None)==(up?0:1)&&rekindle.DynamicVars["Fire"].BaseValue==3,"Rekindle cost Fire "+up);
            await Play(rekindle);
            var copies=player.PlayerCombatState!.AllCards.OfType<Rekindle>().Where(c=>c!=rekindle).ToArray();
            Check(copies.Length==2&&copies.All(c=>c.Enchantment is null&&c.IsUpgraded==up),"Glam generates two clean bodies "+up);
            var copy=copies.First();await Play(copy);
            Check(S().Orbs.Value(OrbKind.Fire)==9,"generated body plays once "+up);
            Check(player.PlayerCombatState.DrawPile.Cards.Last() is Rekindle,"copy inserted at draw bottom "+up);
            await Reset();var zero=Create<ZeroSearch>(up);
            await CardPileCmd.AddGeneratedCardToCombat(zero,PileType.Hand,player);
            for(int locks=0;locks<=3;locks++)
            {
                if(locks>0)await Dispatch(S().Orbs.Lock((OrbKind)(locks-1),1));
                zero.DynamicVars["Cards"].UpdateCardPreview(zero,CardPreviewMode.Normal,null,true);
                Check(zero.DynamicVars["Cards"].PreviewValue==(up?4:3)+locks,"dynamic draw preview "+up+" locks="+locks);
            }
            int hand=player.PlayerCombatState!.Hand.Cards.Count;
            await CardPileCmd.Add(player.PlayerCombatState.Hand.Cards.ToArray(),PileType.Discard);
            await Play(zero);Check(player.PlayerCombatState.Hand.Cards.Count==(up?7:6),"actual draw agrees "+up);
            foreach(int x in new[]{0,2})
            {
                await Reset();await Dispatch(S().Orbs.Strengthen(OrbKind.Growth,9,OrbScope.All));
                var earth=Create<EarthCollapse>(up);earth.EnergyCost.CapturedXValue=x;
                int hp=Enemy().CurrentHp;await CardCmd.AutoPlay(ctx,earth,null,skipXCapture:true,skipCardPileVisuals:true);
                Check(S().Orbs.Value(OrbKind.Growth)==(x==0?9:0)&&Enemy().CurrentHp==hp-(x==0?0:9+2*(up?11:8)),"EarthCollapse zero/nonzero "+up+" x="+x);
            }
            await Reset();foreach(var kind in S().Orbs.Positions.ToArray()){await Dispatch(S().Orbs.Strengthen(kind,4,OrbScope.All));await Dispatch(S().Orbs.Lock(kind));}
            var nourish=Create<NourishingLife>(up);int hpBefore=Enemy().CurrentHp;await Play(nourish,Enemy());
            Check(hpBefore-Enemy().CurrentHp==(up?12:9)&&nourish.DynamicVars.Damage.BaseValue==(up?12:9),"NourishingLife absorbs before one hit "+up);
            Check(S().Orbs.Positions.Sum(S().Orbs.Value)==8,"NourishingLife consumes one locked orb "+up);
            await Reset();await Play(Create<Cooldown>(up));
            Check(player.Creature.Block==(up?10:7)&&S().Orbs.LockedTurns(OrbKind.Fire)==1&&S().Waves.Amount==0,"Cooldown block lock no waves "+up);
            var power=player.Creature.GetPower<CooldownPower>();Check(power?.Icon!=null&&power.BigIcon!=null,"Cooldown icon loads "+up);
            await Turn();Check(player.Creature.GetPower<CooldownPower>()?.Amount==1,"empty Waves preserves charge "+up);
            S().Waves.Add(9);await Turn();Check(S().Waves.Amount==9&&player.Creature.GetPower<CooldownPower>() is null,"native nonempty Waves consumes charge without decay "+up);
            await Turn();Check(S().Waves.Amount==4,"following native turn decays normally "+up);
        }
        await Reset();await Play(Create<Cooldown>());await Play(Create<Cooldown>());S().Waves.Add(8);
        await Turn();Check(S().Waves.Amount==8&&player.Creature.GetPower<CooldownPower>()?.Amount==1,"stack charge one");
        await Turn();Check(S().Waves.Amount==8&&player.Creature.GetPower<CooldownPower>() is null,"stack charge two");
        await Reset();Check(player.Creature.GetPower<CooldownPower>() is null,"no next combat carry");
        var layer=new CanvasLayer{Layer=120};NGame.Instance!.AddChild(layer);var panel=new Control();layer.AddChild(panel);
        var background=new ColorRect{Color=new Color("20272e")};panel.AddChild(background);
        CardModel[] cards=[Create<Rekindle>(),Create<Cooldown>(),Create<ZeroSearch>(),Create<NourishingLife>()];
        foreach(var card in cards)await CardPileCmd.AddGeneratedCardToCombat(card,PileType.Discard,player);
        foreach(var kind in S().Orbs.Positions.ToArray())await Dispatch(S().Orbs.Lock(kind));
        var nodes=new List<NCard>();
        foreach(var card in cards){var n=MegaCrit.Sts2.Core.Assets.PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>();panel.AddChild(n);n.Model=card;nodes.Add(n);}
        try
        {
            foreach(var resolution in new[]{new Vector2I(1280,720),new Vector2I(1920,1080)})
            {
                DisplayServer.WindowSetSize(resolution);await NGame.Instance.ToSignal(NGame.Instance.GetTree(),SceneTree.SignalName.ProcessFrame);
                var size=NGame.Instance.GetViewport().GetVisibleRect().Size;background.Size=size;
                for(int i=0;i<nodes.Count;i++){nodes[i].Scale=Vector2.One*Math.Min(size.X/1480,size.Y/570);nodes[i].Position=new(size.X*(i+0.5f)/4,size.Y/2);}
                foreach(bool up in new[]{false,true})
                {
                    foreach(var n in nodes){if(up&&!n.Model.IsUpgraded){n.Model.UpgradeInternal();n.Model.FinalizeUpgradeInternal();}else if(!up&&n.Model.IsUpgraded)n.Model.DowngradeInternal();n.UpdateVisuals(PileType.Hand,CardPreviewMode.Normal);}
                    await NGame.Instance.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    using var img=NGame.Instance.GetViewport().GetTexture().GetImage();string dir=System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT")!;
                    Check(img.SavePng(System.IO.Path.Combine(dir,$"cards-{resolution.X}-{(up?"upgraded":"base")}.png"))==Error.Ok,"card screenshot "+resolution+" "+up);
                }
            }
        }
        finally {layer.QueueFree();DisplayServer.WindowSetSize(new Vector2I(1280,720));}
        MainFile.Logger.Info($"CARD061_AUDIT_PASS checks={checks} nativeTurn=True");
    }
}
