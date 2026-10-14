using Godot;
using HarmonyLib;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.OrbBasics;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Cards.Stateful;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Relics;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace Librarian.Mechanics;

internal static class DevelopmentBeta3Audit
{
    private static int _checks;
    internal static Action<NCreature,string>? AnimObserver;
    private static readonly string Dir = @"D:\Slay The Spire_Mod Dev\outputs\revision-v1.0-beta3\screenshots";
    private static void Check(bool ok,string text){if(!ok)throw new InvalidOperationException("BETA3: "+text);_checks++;MainFile.Logger.Info("BETA3_CHECK_PASS "+text);}
    private static async Task Wait(double t)=>await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(t),SceneTreeTimer.SignalName.Timeout);
    private static async Task Shot(string name){System.IO.Directory.CreateDirectory(Dir);await NGame.Instance!.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);using var i=NGame.Instance.GetViewport().GetTexture().GetImage();if(i.SavePng(System.IO.Path.Combine(Dir,name+".png"))!=Error.Ok)throw new Exception("beta3 screenshot");}
    internal static async Task Run(Player player,Func<Task> fresh)
    {
        var ctx=new ThrowingPlayerChoiceContext();LibrarianSession S()=>LibrarianRuntime.Get(player);
        async Task Reset(){await fresh();for(int i=0;player.PlayerCombatState?.Phase!=PlayerTurnPhase.Play;i++){if(i>1200)throw new TimeoutException();await Wait(.02);}}
        T Card<T>(bool up=false) where T:CardModel{var c=player.Creature.CombatState!.CreateCard<T>(player);if(up){c.UpgradeInternal();c.FinalizeUpgradeInternal();}return c;}
        Task Play(CardModel c)=>CardCmd.AutoPlay(ctx,c,c.TargetType==TargetType.AnyEnemy?player.Creature.CombatState!.HittableEnemies.First():null,skipCardPileVisuals:true);
        Task Dispatch(OrbOperationResult r)=>LibrarianRuntime.Dispatch(S(),ctx,r);
        foreach(bool up in new[]{false,true})
        {
            await Reset();var zero=Card<ZeroSearch>(up);await CardPileCmd.Add(zero,PileType.Hand);
            for(int locks=0;locks<=3;locks++)
            {
                if(locks>0)await Dispatch(S().Orbs.Lock((OrbKind)(locks-1),1));
                zero.DynamicVars["Cards"].UpdateCardPreview(zero,CardPreviewMode.Normal,null,true);
                Check(zero.DynamicVars["Cards"].PreviewValue==(up?5:4)+locks,"zero preview "+up+" "+locks);
            }
            await CardPileCmd.Add(player.PlayerCombatState!.Hand.Cards.ToArray(),PileType.Discard);await Play(zero);
            Check(player.PlayerCombatState.Hand.Cards.Count==(up?8:7),"zero actual draw "+up);
            await Reset();var attack=Card<ScatteredFlames>(up);Check(attack.DynamicVars.CalculationBase.BaseValue==8,"forbidden base damage "+up);
            for(int i=0;i<2;i++)await Dispatch(S().Orbs.Lock((OrbKind)i));
            var enemy=player.Creature.CombatState!.HittableEnemies.First();int hp=enemy.CurrentHp;await Play(attack);
            Check(hp-enemy.CurrentHp==8+2*(up?7:5),"forbidden actual lock scaling "+up);
            foreach(var kind in Enum.GetValues<OrbKind>())
            {
                await Reset();foreach(var k in S().Orbs.Positions.ToArray())await Dispatch(S().Orbs.LoseAll(k,OrbScope.All));
                await Dispatch(S().Orbs.Strengthen(kind,6,OrbScope.All));await Dispatch(S().Orbs.Lock(kind));
                var nourish=Card<NourishingLife>(up);enemy=player.Creature.CombatState!.HittableEnemies.First();hp=enemy.CurrentHp;await Play(nourish);
                Check(S().Orbs.Value(kind)==0&&nourish.DynamicVars.Damage.BaseValue==(up?14:11)&&hp-enemy.CurrentHp==(up?14:11),"nourish only positive locked orb "+kind+" "+up);
            }
            await Reset();foreach(var k in S().Orbs.Positions.ToArray())await Dispatch(S().Orbs.LoseAll(k,OrbScope.All));
            var empty=Card<NourishingLife>(up);enemy=player.Creature.CombatState!.HittableEnemies.First();hp=enemy.CurrentHp;await Play(empty);
            Check(hp-enemy.CurrentHp==(up?8:5),"nourish all zero still attacks "+up);
            await Reset();foreach(var k in S().Orbs.Positions.ToArray())await Dispatch(S().Orbs.LoseAll(k,OrbScope.All));
            await Dispatch(S().Orbs.Strengthen(OrbKind.Fire,6,OrbScope.All));await Dispatch(S().Orbs.Strengthen(OrbKind.Tide,9,OrbScope.All));
            await Dispatch(S().Orbs.Lock(OrbKind.Fire));await Dispatch(S().Orbs.Lock(OrbKind.Tide));
            var repeated=Card<NourishingLife>(up);await Play(repeated);
            Check(repeated.DynamicVars.Damage.BaseValue is var first && (first==(up?8:5)+6||first==(up?8:5)+9),"nourish random chooses one of two positive candidates "+up);
            await Play(repeated);
            Check(S().Orbs.Value(OrbKind.Fire)==0&&S().Orbs.Value(OrbKind.Tide)==0&&repeated.DynamicVars.Damage.BaseValue==(up?8:5)+15,"nourish next play ignores newly emptied orb "+up);
        }
        foreach(string lang in new[]{"zhs","eng"})
        {
            LibrarianLanguage.Select(lang);var ds=ModelDb.Event<TheArchitect>().DialogueSet.CharacterDialogues[ModelDb.Character<LibrarianCharacter>().Id.Entry];
            Check(ds.All(d=>d.Lines.Count==3&&d.Lines.Select(l=>l.LineText!.GetFormattedText()).Distinct().Count()==3),"three distinct lines "+lang);
            Check(ds.SelectMany(d=>d.Lines).All(l=>l.NextButtonText is null||!l.NextButtonText.GetFormattedText().Contains("THE_ARCHITECT")),"all continuation labels localized "+lang);
        }
        LibrarianLanguage.Select("zhs");await Relics();
        MainFile.Logger.Info($"BETA3_AUDIT_PASS checks={_checks}");
    }
    private static async Task Relics()
    {
        var relics=ModelDb.RelicPool<LibrarianRelicPool>().AllRelics.Append(ModelDb.Relic<TatteredSpellScroll>()).Append(ModelDb.Relic<RestoredSpellScroll>()).DistinctBy(r=>r.Id).ToArray();Check(relics.Length==9,"nine relics inspected");
        var layer=new CanvasLayer{Layer=120};NGame.Instance!.AddChild(layer);var bg=new ColorRect{Color=new Color("20272e"),Size=new(1920,1080)};layer.AddChild(bg);
        int at=0;foreach(var r in relics.Append(ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.Anchor>()).Append(ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.BurningBlood>()))
        {
            var n=NRelic.Create(r,NRelic.IconSize.Small)!;bg.AddChild(n);n.Position=new(35+(at%6)*200,130+(at/6)*210);n.Scale=Vector2.One*1.8f;
            var label=new Label{Text=r.Title.GetFormattedText(),Position=n.Position+new Vector2(-10,130)};bg.AddChild(label);at++;
            Check(n.Outline.Visible&&Math.Abs(n.Outline.SelfModulate.A-0.501961f)<.01f&&n.Outline.SelfModulate.R==0,"native outline tint "+r.GetType().Name);
            if(r is LibrarianRelic){using var mask=r.IconOutline.GetImage();using var icon=r.Icon.GetImage();Check(mask.GetSize()==icon.GetSize(),"outline canvas "+r.GetType().Name);Check(ReferenceEquals(r.IconOutline,r.IconOutline),"outline cached "+r.GetType().Name);}
        }
        await Wait(.5);await Shot("all-relics-native-outline");layer.QueueFree();await Wait(.2);
    }
    internal static async Task Architect()
    {
        foreach(string lang in new[]{"zhs","eng"})
        {
            await NGame.Instance!.ReturnToMainMenu();LibrarianLanguage.Select(lang);
            await NGame.Instance.StartNewSingleplayerRun(ModelDb.Character<LibrarianCharacter>(),true,ActModel.GetDefaultList(),[],"BETA3ARCHITECT",GameMode.Standard);
            var room=(EventRoom)await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<TheArchitect>());
            var ev=room.LocalMutableEvent;await Wait(3.5);
            // Debug entry skips the intervening rooms that normally create a run backup.
            // Exercise the normal save API before the native finale deletes both files.
            await MegaCrit.Sts2.Core.Saves.SaveManager.Instance.SaveRun(null);
            for(int line=0;line<3;line++)
            {
                Check(ev.CurrentOptions.Count==1,"architect actionable line "+lang+" "+line);
                Check(!ev.CurrentOptions[0].Title.GetFormattedText().Contains("THE_ARCHITECT"),"architect actual button "+lang+" "+line);
                await Shot($"architect-{lang}-line{line+1}");if(line<2){await ev.CurrentOptions[0].Chosen();await Wait(.5);}
            }
            int playerAttacks=0,architectAttacks=0;AnimObserver=(n,s)=>{if(s=="Attack"){if(n.Entity.IsPlayer)playerAttacks++;else architectAttacks++;}};
            var frames=new List<(Image image,double t)>();var timer=System.Diagnostics.Stopwatch.StartNew();var finish=ev.CurrentOptions[0].Chosen();
            while(!finish.IsCompleted&&timer.Elapsed.TotalSeconds<20)
            {
                await NGame.Instance.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                frames.Add((NGame.Instance.GetViewport().GetTexture().GetImage(),timer.Elapsed.TotalSeconds));await Wait(.06);
            }
            Check(finish.IsCompleted,"architect finale terminates "+lang);await finish;AnimObserver=null;
            Check(playerAttacks==5&&architectAttacks==1,"native finale player five hits and architect retaliation "+lang);
            var folder=System.IO.Path.Combine(Dir,"architect-"+lang+"-finale");System.IO.Directory.CreateDirectory(folder);
            for(int i=0;i<frames.Count;i++){frames[i].image.SaveJpg(System.IO.Path.Combine(folder,$"frame-{i:D4}.jpg"),.9f);frames[i].image.Dispose();}
            System.IO.File.WriteAllText(System.IO.Path.Combine(folder,"timing.json"),System.Text.Json.JsonSerializer.Serialize(frames.Select(f=>f.t)));
        }
        LibrarianLanguage.Select("zhs");MainFile.Logger.Info($"BETA3_ARCHITECT_PASS checks={_checks} nativeFinale=True languages=2");
    }
}
[HarmonyPatch(typeof(NCreature),nameof(NCreature.SetAnimationTrigger))]
internal static class Beta3AnimationObservation
{
    [HarmonyPostfix] private static void Postfix(NCreature __instance,string trigger)=>DevelopmentBeta3Audit.AnimObserver?.Invoke(__instance,trigger);
}
