using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards.OrbBasics;
using Librarian.LibrarianCode.Cards.OrbAdvancedBasics;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Cards.PowerCards;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Random;

namespace Librarian.Mechanics;

/// <summary>Actual native card plays, observing completed benefits at the first lock callback.</summary>
internal static class DevelopmentRevision0310CardsAudit
{
    internal static async Task Run(Player player, Func<Task> freshFight)
    {
        var context = new ThrowingPlayerChoiceContext();
        LibrarianSession S() => LibrarianRuntime.Get(player);
        int checks = 0;
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("0310 cards: " + label);
            checks++; MainFile.Logger.Info("CARD0310_CHECK_PASS " + label);
        }
        async Task Reset()
        {
            await freshFight();
            for (int i = 0; player.PlayerCombatState?.Phase != PlayerTurnPhase.Play; i++)
            {
                if (i > 900) throw new TimeoutException("0310 native play phase");
                await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
        CardModel Create<T>(bool upgraded) where T : CardModel
        {
            var card = player.Creature.CombatState!.CreateCard<T>(player);
            if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
            return card;
        }
        async Task SeedDraw()
        {
            foreach (var c in player.PlayerCombatState!.Hand.Cards.ToArray())
                await CardPileCmd.Add(c, PileType.Discard, skipVisuals: true);
            for (int i = 0; i < 6; i++)
                await CardPileCmd.Add(Create<QuietEmbers>(false), PileType.Draw, CardPilePosition.Top, skipVisuals: true);
        }
        Func<LibrarianSession, PlayerChoiceContext, OrbEvent, Task>? observer = null;
        try
        {
            foreach (bool upgraded in new[] { false, true })
            {
                var cases = new (Func<CardModel> Create, OrbKind Lock, int Turns, string Effect)[] {
                    (()=>Create<BurningPages>(upgraded),OrbKind.Tide,1,"fire"),
                    (()=>Create<ColdFlame>(upgraded),OrbKind.Fire,1,"tide"),
                    (()=>Create<Renewal>(upgraded),OrbKind.Fire,1,"growth"),
                    (()=>Create<SproutingSeed>(upgraded),OrbKind.Fire,upgraded?1:2,"block-growth"),
                    (()=>Create<DryBranchSearch>(upgraded),OrbKind.Growth,1,"draw"),
                    (()=>Create<BurnTheRiver>(upgraded),OrbKind.Tide,upgraded?1:2,"fire-settle"),
                    (()=>Create<BurnRoots>(upgraded),OrbKind.Growth,1,"attack-fire"),
                    (()=>Create<SteamBlast>(upgraded),OrbKind.Fire,1,"attack-two-locks"),
                    (()=>Create<TidalGravity>(upgraded),OrbKind.Tide,2,"energy"),
                    (()=>Create<Evaporation>(upgraded),OrbKind.Tide,1,"fire-draw"),
                    (()=>Create<Cooldown>(upgraded),OrbKind.Fire,1,"block-waves"),
                    (()=>Create<SproutingBulwark>(upgraded),OrbKind.Growth,1,"block-draw"),
                    (()=>Create<RidgeWard>(upgraded),OrbKind.Tide,-1,"settle-power"),
                    (()=>Create<DroughtEdict>(upgraded),OrbKind.Tide,-1,"lose-attack")
                };
                foreach (var test in cases)
                {
                    await Reset(); await SeedDraw();
                    foreach (var kind in Enum.GetValues<OrbKind>())
                        await LibrarianRuntime.Dispatch(S(),context,S().Orbs.Gain(kind,4));
                    await LibrarianRuntime.Dispatch(S(),context,S().Orbs.Activate(test.Lock,OrbScope.All));
                    var card=test.Create(); int V(string name)=>card.DynamicVars[name].IntValue;
                    var enemy=player.Creature.CombatState!.HittableEnemies.First();
                    int hp=enemy.CurrentHp, block=player.Creature.Block, energy=player.PlayerCombatState!.Energy;
                    int waves=S().Waves.Amount, hand=player.PlayerCombatState.Hand.Cards.Count;
                    int fire=S().Orbs.Value(OrbKind.Fire),tide=S().Orbs.Value(OrbKind.Tide),growth=S().Orbs.Value(OrbKind.Growth);
                    int settled=S().Orbs.SettlementsThisTurn;
                    int events=0;
                    observer=(session,_,change)=> {
                        if(session.Player!=player||change.Kind!=OrbEventKind.Locked)return Task.CompletedTask;
                        events++;
                        bool done=test.Effect switch {
                            "fire"=>session.Orbs.Value(OrbKind.Fire)==fire+V("Fire")&&session.Orbs.Foreground==OrbKind.Fire,
                            "block"=>player.Creature.Block==block+V("Block"),
                            "tide"=>session.Orbs.Value(OrbKind.Tide)==tide+V("Tide")&&session.Orbs.Foreground==OrbKind.Tide,
                            "growth"=>session.Orbs.Value(OrbKind.Growth)==growth+V("Growth")&&session.Orbs.Foreground==OrbKind.Growth,
                            "block-growth"=>player.Creature.Block==block+V("Block")&&session.Orbs.Value(OrbKind.Growth)==growth+V("Growth"),
                            "draw"=>player.PlayerCombatState.Hand.Cards.Count==hand+V("Cards"),
                            "fire-settle"=>session.Orbs.SettlementsThisTurn==settled+1&&enemy.CurrentHp==hp-fire-V("Fire"),
                            "attack-fire"=>enemy.CurrentHp==hp-V("Damage")&&session.Orbs.Value(OrbKind.Fire)==fire+V("Fire"),
                            "xattack"=>enemy.CurrentHp==hp-3*V("Damage"),
                            "attack-two-locks"=>enemy.CurrentHp==hp-V("Damage"),
                            "energy"=>player.PlayerCombatState.Energy==energy+V("Energy"),
                            "fire-draw"=>session.Orbs.Value(OrbKind.Fire)==fire+V("Fire")&&player.PlayerCombatState.Hand.Cards.Count==hand+V("Cards"),
                            "block-waves"=>player.Creature.Block==block+V("Block")&&session.Waves.Amount==waves+V("Waves"),
                            "block-draw"=>player.Creature.Block==block+V("Block")&&player.PlayerCombatState.Hand.Cards.Count==hand+V("Cards"),
                            "settle-power"=>session.Orbs.SettlementsThisTurn==settled+1&&player.Creature.GetPower<RidgeWardPower>()!=null,
                            "lose-attack"=>session.Orbs.Value(OrbKind.Tide)==0&&enemy.CurrentHp==hp-tide,
                            _=>false};
                        Check(done,$"benefits complete before lock {card.Id} upgraded={upgraded} event={events}");
                        return Task.CompletedTask;
                    };
                    LibrarianRuntime.OrbChanged+=observer;
                    try {
                        if(card is EarthCollapse)card.EnergyCost.CapturedXValue=3;
                        await CardCmd.AutoPlay(context,card,card.TargetType==TargetType.AnyEnemy?enemy:null,skipXCapture:card is EarthCollapse,skipCardPileVisuals:true);
                    } finally {LibrarianRuntime.OrbChanged-=observer;observer=null;}
                    Check(events==(card is SteamBlast?2:1),"exact lock count "+card.Id);
                    Check(S().Orbs.LockedTurns(test.Lock)==test.Turns,"original kind and duration "+card.Id);
                    if(card is SteamBlast)Check(S().Orbs.LockedTurns(OrbKind.Tide)==1,"second Steam lock retains kind/duration");
                }
                await Reset();
                var zero=Create<EarthCollapse>(upgraded);zero.EnergyCost.CapturedXValue=0;
                int hpZero=player.Creature.CombatState!.HittableEnemies.First().CurrentHp;
                await CardCmd.AutoPlay(context,zero,null,skipXCapture:true,skipCardPileVisuals:true);
                Check(!S().Orbs.IsLocked(OrbKind.Growth)&&player.Creature.CombatState.HittableEnemies.First().CurrentHp==hpZero,"v040 X0 no lock, no damage");
            }
            await Reset();
            await LibrarianRuntime.Dispatch(S(),context,S().Orbs.Gain(OrbKind.Fire,1));
            var twist=Create<SpacetimeTwist>(true);
            await CardCmd.AutoPlay(context,twist,null,skipCardPileVisuals:true);
            int index=S().Orbs.Positions.ToList().IndexOf(OrbKind.Fire);
            for(ulong seed=0;seed<1000;seed++) {
                var rng=new Rng(seed);var saved=rng.ToSerializable();
                if(rng.NextInt(3)!=index)continue;
                player.RunState.Rng.CombatTargets.LoadFromSerializable(saved);break;
            }
            int triggers=0;
            observer=(session,_,change)=> {
                if(session.Player==player&&change.Kind==OrbEventKind.Locked) {
                    triggers++;
                    Check(change.Orb==OrbKind.Fire,"Twist locks original random identity after movement");
                    Check(session.Orbs.Value(OrbKind.Tide)==6&&session.Orbs.Value(OrbKind.Growth)==6
                        &&session.Orbs.IsActivated(OrbKind.Tide)&&session.Orbs.IsActivated(OrbKind.Growth),"Twist strengthens/activates others before lock");
                    Check(session.Orbs.Foreground!=OrbKind.Fire,"Twist movement occurs before original foreground locks");
                }return Task.CompletedTask;
            };
            LibrarianRuntime.OrbChanged+=observer;
            try {await player.Creature.GetPower<SpacetimeTwistPower>()!.BeforeHandDraw(player,context,player.Creature.CombatState!);}
            finally {LibrarianRuntime.OrbChanged-=observer;observer=null;}
            Check(triggers==1,"Twist locks once per trigger");
            await Reset();
            await CardCmd.AutoPlay(context,Create<BurningPages>(false),null,skipCardPileVisuals:true);
            await CardCmd.AutoPlay(context,Create<ColdFlame>(false),null,skipCardPileVisuals:true);
            Check(S().Orbs.LockedTurns(OrbKind.Tide)==1&&S().Orbs.LockedTurns(OrbKind.Fire)==1,"consecutive plays do not leak or replay deferred locks");
            await Reset();Check(!S().Orbs.Positions.Any(S().Orbs.IsLocked),"fresh fight contains no pending lock state");
            MainFile.Logger.Info($"CARD0310_RUNTIME_AUDIT_PASS checks={checks} cards=14 delayedPower=1");
        }
        finally {if(observer!=null)LibrarianRuntime.OrbChanged-=observer;await Reset();}
    }
}
