using Godot;
using HarmonyLib;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.Stateful;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Cards.OrbAdvancedBasics;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Relics;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.InspectScreens;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace Librarian.Mechanics;

internal static class DevelopmentRevision101Audit
{
    private static int _checks;
    private static string Dir => System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT") ?? @"D:\Slay The Spire_Mod Dev\outputs\revision-v1.0.1-beta1\screenshots";
    private static void Check(bool ok,string label){if(!ok)throw new InvalidOperationException("101: "+label);_checks++;MainFile.Logger.Info("V101_CHECK_PASS "+label);}
    private static async Task Wait(double seconds)=>await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
    private static async Task Shot(string name){System.IO.Directory.CreateDirectory(Dir);await NGame.Instance!.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);using var im=NGame.Instance.GetViewport().GetTexture().GetImage();Check(im.SavePng(System.IO.Path.Combine(Dir,name+".png"))==Error.Ok,"screenshot "+name);}
    private static IEnumerable<Node> Desc(Node n){foreach(var c in n.GetChildren()){yield return c;foreach(var d in Desc(c))yield return d;}}
    internal static async Task Selection(NMainMenu menu)
    {
        var prefs=LibrarianPreferences050.Current;
        bool oldReduced=prefs.ReducedMotion,oldIdle=prefs.OrbIdle,oldParticles=prefs.Particles;
        prefs.ReducedMotion=false;prefs.OrbIdle=true;prefs.Particles=true;
        var screen=menu.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();screen.InitializeSingleplayer();menu.SubmenuStack.Push(screen);await Wait(.8);
        var button=Desc(screen).OfType<NCharacterSelectButton>().Single(b=>b.Character is LibrarianCharacter);button.Select();await Wait(1);
        foreach(var size in new[]{new Vector2I(1280,720),new Vector2I(1920,1080)})
        {
            DisplayServer.WindowSetSize(size);await Wait(.5);var painting=Desc(screen).OfType<LibrarianCharacterSelect040>().Single();var before=painting.Illustration.Position;
            float maxTravel=0;await Shot("selection-"+size.X+"-a");
            for(int i=0;i<8;i++){await Wait(.25);maxTravel=Math.Max(maxTravel,painting.Illustration.Position.DistanceTo(before));}
            await Shot("selection-"+size.X+"-b");Check(maxTravel>.1f,"slow idle moves "+size.X);
            Check(Desc(painting).OfType<LibrarianSelectionMotes101>().Count()==1,"single particle layer "+size.X);
            var settings=LibrarianPreferences050.Current;bool reduced=settings.ReducedMotion;settings.ReducedMotion=true;await Wait(.1);before=painting.Illustration.Position;await Wait(.2);Check(painting.Illustration.Position==before,"reduced motion freezes idle");settings.ReducedMotion=reduced;
        }
        prefs.ReducedMotion=oldReduced;prefs.OrbIdle=oldIdle;prefs.Particles=oldParticles;
        DisplayServer.WindowSetSize(new(1280,720));menu.SubmenuStack.Pop();await Wait(.5);
        MainFile.Logger.Info("V101_SELECTION_PASS");
    }
    internal static async Task Run(Player player,Func<Task> fresh)
    {
        var ctx=new ThrowingPlayerChoiceContext();
        async Task Reset(){await fresh();for(int i=0;player.PlayerCombatState?.Phase!=PlayerTurnPhase.Play;i++){if(i>1200)throw new TimeoutException();await Wait(.02);}}
        T Card<T>(bool up=false) where T:CardModel{var c=player.Creature.CombatState!.CreateCard<T>(player);if(up){c.UpgradeInternal();c.FinalizeUpgradeInternal();}return c;}
        Task Play(CardModel c)=>CardCmd.AutoPlay(ctx,c,c.TargetType==TargetType.AnyEnemy?player.Creature.CombatState!.HittableEnemies.First():null,skipCardPileVisuals:true);
        foreach(bool up in new[]{false,true})
        {
            await Reset();var root=Card<Rootbind>(up);var enemy=player.Creature.CombatState!.HittableEnemies.First();int hp=enemy.CurrentHp;await Play(root);Check(hp-enemy.CurrentHp==(up?11:8),"root actual damage "+up);
            await Reset();var session=LibrarianRuntime.Get(player);int growth=session.Orbs.Value(OrbKind.Growth);await Play(Card<LifeSymphony>(up));Check(session.Orbs.Value(OrbKind.Growth)-growth==(up?12:8),"symphony actual Growth "+up);
            await Reset();var outOfContext=Card<OutOfContext>(up);await CardPileCmd.Add(outOfContext,PileType.Hand);await Play(outOfContext);Check(outOfContext.Pile?.Type==PileType.Exhaust,"out of context exhausts "+up);
            await Reset();var source=Card<LibrarianDefend>(up);source.EnchantInternal(ModelDb.Enchantment<Glam>().ToMutable(),1);source.Enchantment!.ModifyCard();await CardPileCmd.Add(source,PileType.Hand);await Play(source);
            var transcribe=Card<Transcribe>(up);Check(transcribe.Keywords.Contains(CardKeyword.Exhaust)==!up&&!transcribe.Keywords.Contains(CardKeyword.Retain),"transcribe keyword transition "+up);await CardPileCmd.Add(transcribe,PileType.Hand);await Play(transcribe);
            var copies=player.PlayerCombatState!.Hand.Cards.Concat(player.PlayerCombatState.DrawPile.Cards).Where(c=>c.Id==source.Id&&c.Keywords.Contains(CardKeyword.Ethereal)).ToArray();
            Check(copies.Length==2&&copies.All(c=>c.Enchantment is null&&c.IsUpgraded==up&&c.Keywords.Contains(CardKeyword.Exhaust)),"two unenchanted upgraded copies "+up);
            Check(copies.Count(c=>c.Pile?.Type==PileType.Hand)==1&&copies.Count(c=>c.Pile?.Type==PileType.Draw)==1,"copy hand and draw destinations "+up);
            Check(source.Enchantment is Glam,"source enchantment preserved "+up);Check(transcribe.Pile?.Type==(up?PileType.Discard:PileType.Exhaust),"transcribe actual final pile "+up);
            await Play(copies.Single(c=>c.Pile?.Type==PileType.Hand));Check(source.Enchantment is Glam,"copy play does not alter source "+up);
        }
        Check(ModelDb.Card<Rekindle>().Rarity==CardRarity.Rare&&ModelDb.Card<ToBeContinued>().Rarity==CardRarity.Uncommon,"clarified rarity swap");
        foreach(string lang in new[]{"zhs","eng"})
        {
            LibrarianLanguage.Select(lang);
            foreach(var ancient in ModelDb.AllAncients)
            {
                var ds=ancient.DialogueSet.CharacterDialogues[ModelDb.Character<LibrarianCharacter>().Id.Entry];
                Check(ds.Count>0&&ds.All(d=>d.Lines.Count==3),"three specific dialogue lines "+lang+" "+ancient.Id);
                Check(ds.SelectMany(d=>d.Lines).All(l=>!l.LineText!.GetFormattedText().Contains(".talk.")),"localized dialogue "+lang+" "+ancient.Id);
                Check(ds.All(d=>d.IsRepeating),"dialogue remains available on repeat visits "+ancient.Id);
            }
        }
        LibrarianLanguage.Select("zhs");await Relics();MainFile.Logger.Info($"V101_CARDS_PASS checks={_checks}");
    }
    private static async Task Relics()
    {
        var relics=ModelDb.RelicPool<LibrarianRelicPool>().AllRelics.Append(ModelDb.Relic<TatteredSpellScroll>()).Append(ModelDb.Relic<RestoredSpellScroll>()).DistinctBy(r=>r.Id).ToArray();
        var screen=NInspectRelicScreen.Create()!;NGame.Instance!.AddChild(screen);
        foreach(var size in new[]{new Vector2I(1280,720),new Vector2I(1920,1080)})
        {
            DisplayServer.WindowSetSize(size);await Wait(.3);
            foreach(var relic in relics)
            {
                using var image=relic.BigIcon.GetImage();Check(image.GetSize()==new Vector2I(256,256)&&ReferenceEquals(relic.BigIcon,relic.BigIcon),"large canvas cached "+relic.Id);
                screen.Open(relics,relic);await Wait(.3);var texture=screen.GetNode<TextureRect>("%RelicImage");Check(ReferenceEquals(texture.Texture,relic.BigIcon),"native inspect uses large corrected icon "+relic.Id);
                await Shot("relic-"+relic.Id.Entry+"-"+size.X);AccessTools.Method(typeof(NInspectRelicScreen),"Close").Invoke(screen,null);await Wait(.3);
            }
        }
        screen.QueueFree();DisplayServer.WindowSetSize(new(1280,720));await Wait(.3);
    }
    internal static async Task Ancients()
    {
        await NGame.Instance!.ReturnToMainMenu();
        var run=await NGame.Instance.StartNewSingleplayerRun(ModelDb.Character<LibrarianCharacter>(),true,ActModel.GetDefaultList(),[],"LIBRARIAN101DIALOGUE",GameMode.Standard);
        foreach(string lang in new[]{"zhs","eng"})
        {
            LibrarianLanguage.Select(lang);
            foreach(var ancient in ModelDb.AllAncients)
            {
                // Represent a first visit by Librarian after another character has met
                // the Ancient, so native first-ever introductions keep their priority.
                var stats=(Dictionary<ModelId,AncientStats>)AccessTools.Field(typeof(ProgressState),"_ancientStats").GetValue(SaveManager.Instance.Progress)!;
                stats.TryGetValue(ancient.Id,out var previous);
                stats[ancient.Id]=new AncientStats {Id=ancient.Id,CharStats=[new AncientCharacterStats{Character=ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Ironclad>().Id,Wins=1}]};
                var room=new EventRoom(ancient);await RunManager.Instance.EnterRoom(room);await NGame.Instance.Transition.FadeIn();await Wait(.5);
                var layout=(NAncientEventLayout)NEventRoom.Instance!.Layout!;
                var lines=(System.Collections.ICollection)AccessTools.Field(typeof(NAncientEventLayout),"_dialogue").GetValue(layout)!;
                Check(lines.Count==3,"actual ancient three-line conversation "+lang+" "+ancient.Id);
                for(int i=0;i<3;i++){await Shot("ancient-"+ancient.Id.Entry+"-"+lang+"-"+i);AccessTools.Method(typeof(NAncientEventLayout),"OnDialogueHitboxClicked").Invoke(layout,[null]);await Wait(.3);}
                Check(room.LocalMutableEvent.CurrentOptions.Count>0,"ancient options reachable "+ancient.Id);
                if(previous is null)stats.Remove(ancient.Id);else stats[ancient.Id]=previous;
            }
        }
        LibrarianLanguage.Select("zhs");MainFile.Logger.Info($"V101_ANCIENTS_PASS checks={_checks}");
    }
}
