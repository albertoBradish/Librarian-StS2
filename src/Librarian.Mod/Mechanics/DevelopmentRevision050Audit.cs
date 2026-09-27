using Godot;
using System.Diagnostics;
using System.Text.Json;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.NativeBatch;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.TestSupport;
using STS2RitsuLib.Settings;

namespace Librarian.Mechanics;

internal static class DevelopmentRevision050Audit
{
    private static int _checks;
    internal static string Output => System.Environment.GetEnvironmentVariable("LIBRARIAN_041_FOCUS") == "051"
        ? @"D:\Slay The Spire_Mod Dev\outputs\revision-v1.0.0-stable\audit-history\revision-v0.5.1\screenshots"
        : @"D:\Slay The Spire_Mod Dev\outputs\revision-v1.0.0-stable\audit-history\revision-v0.5.0\screenshots";
    internal static void Check(bool ok, string label)
    { if (!ok) throw new InvalidOperationException("050: " + label); _checks++; MainFile.Logger.Info("V050_CHECK_PASS " + label); }
    internal static async Task Wait(double seconds = 0.6) => await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    internal static async Task Capture(string name)
    {
        Directory.CreateDirectory(Output);
        await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
        Check(image.SavePng(Path.Combine(Output, name + ".png")) == Error.Ok, "screenshot " + name);
    }
    internal static async Task Settings()
    {
        Check(OS.GetUserDataDir().Contains("revision030-userdata"), "isolated settings profile");
        LibrarianPreferences050.Reset();
        var binding = LibrarianPreferences050.Bind("opacity", p => p.EffectOpacity, (p,v) => p.EffectOpacity=v, 80);
        binding.Write(30); binding.Save(); LibrarianPreferences050.Load();
        Check(binding.Read() == 30, "RitsuLib binding persists and reloads");
        binding.Write(-100); Check(binding.Read() == 10, "invalid values normalized");
        string valid = File.ReadAllText(LibrarianPreferences050.FilePath);
        File.WriteAllText(LibrarianPreferences050.FilePath, "{bad json"); LibrarianPreferences050.Load();
        Check(LibrarianPreferences050.Current.EffectOpacity == 80 && File.ReadAllText(LibrarianPreferences050.FilePath) == "{bad json", "corrupt config preserved and defaults used");
        File.WriteAllText(LibrarianPreferences050.FilePath, valid); LibrarianPreferences050.Reset();
        Check(ModSettingsRegistry.TryGetPage("Librarian", LibrarianSettings041.PageId, out var page), "registered settings page");
        Check(page!.Sections.Select(s => s.Id).SequenceEqual(new[]{"effects","display","audio","progression","diagnostics"}), "five ordered sections");
        foreach (string section in new[]{"effects","display","audio","progression","diagnostics"})
        {
            var opened = await ModSettingsNavigator.OpenByIdsAsync("Librarian", LibrarianSettings041.PageId, sectionId:section,
                options:new ModSettingsOpenOptions{Highlight=false,Focus=true});
            Check(opened.Success, "navigate " + section); await Wait(0.35); await Capture("settings-"+section);
            var submenu = NGame.Instance!.FindChildren("*","",true,false).OfType<RitsuModSettingsSubmenu>().Last(n=>n.Visible);
            Check(submenu.GetGlobalRect().End.X <= submenu.GetViewportRect().End.X + 2, "settings width " + section);
            if(submenu.GetParent() is NSubmenuStack stack && ReferenceEquals(stack.Peek(),submenu)) stack.Pop();
        }
        MainFile.Logger.Info("V050_SETTINGS_PASS");
    }
    private sealed class FirstSelector : ICardSelector
    {
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options,int minSelect,int maxSelect)
            => Task.FromResult(options.Take(Math.Max(minSelect,Math.Min(1,maxSelect))));
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,IReadOnlyList<CardRewardAlternative> alternatives)
            => new(){card=options.FirstOrDefault()?.Card};
    }
    internal static async Task Run(Player player, Func<Task> freshFight)
    {
        var context=new ThrowingPlayerChoiceContext();
        var oldFast=SaveManager.Instance.PrefsSave.FastMode;
        SaveManager.Instance.PrefsSave.FastMode=FastModeType.Normal;
        LibrarianPreferences050.Reset();
        var observed=new List<(string Stage,Creature Target,string Source)>();
        LibrarianCardVfx050.Observer=(stage,target,source)=>observed.Add((stage,target,source));
        try
        {
            var cards=ModelDb.CardPool<LibrarianCardPool>().AllCards.ToArray();
            Check(cards.Length==91&&cards.All(c=>LibrarianCardVfx050.TryProfile(c,out _)),"all 91 active cards explicitly mapped");
            bool visualOnly=System.Environment.GetEnvironmentVariable("LIBRARIAN_050_MODE")=="visual";
            foreach(bool upgraded in visualOnly ? Array.Empty<bool>() : new[]{false,true}) foreach(var canonical in cards)
            {
                await freshFight();
                while(player.PlayerCombatState!.Phase!=PlayerTurnPhase.Play) await Wait(0.05);
                var session=LibrarianRuntime.Get(player);
                foreach(var kind in session.Orbs.Positions.ToArray()) await LibrarianRuntime.Dispatch(session,context,session.Orbs.Gain(kind,8));
                await Wait(); observed.Clear();
                var card=player.Creature.CombatState!.CreateCard(canonical,player);
                if(upgraded){card.UpgradeInternal();card.FinalizeUpgradeInternal();}
                var target=card.TargetType is TargetType.AnyEnemy or TargetType.RandomEnemy ? player.Creature.CombatState.HittableEnemies.First() : card.TargetType is TargetType.AnyAlly or TargetType.Self ? player.Creature : null;
                MainFile.Logger.Info("V050_PLAY_BEGIN "+card.GetType().Name+" up="+upgraded);
                using(CardSelectCmd.PushSelector(new FirstSelector())) await CardCmd.AutoPlay(context,card,target,skipCardPileVisuals:true);
                Check(observed.Any(e=>e.Stage=="cast"&&e.Source==card.GetType().Name),"native play presentation "+card.GetType().Name+" up="+upgraded);
                if(card.Type==CardType.Attack)
                    Check(observed.Any(e=>e.Stage=="impact"&&e.Source==card.GetType().Name),"actual attack impact "+card.GetType().Name+" up="+upgraded);
            }
            await freshFight();await Wait(2);
            await DevelopmentCharacter050Audit.Run(player);
            var enemy=player.Creature.CombatState!.HittableEnemies.First();
            // Showcase each authored impact family at actual creature anchors, then inspect the rendered frames.
            foreach(var profile in LibrarianCardVfx050.Profiles.Values.Distinct())
            {
                LibrarianCardVfx050.Emit(enemy,profile,"impact","gallery",player);
                await Wait(0.12);await Capture("effect-"+profile.Element+"-"+profile.Shape);await Wait();
            }
            observed.Clear();
            var needle=player.Creature.CombatState.CreateCard<NeedleFlurry>(player);
            await CardCmd.AutoPlay(context,needle,enemy,skipCardPileVisuals:true);
            Check(observed.Count(e=>e.Stage=="impact"&&e.Source==nameof(NeedleFlurry))==1,"multi-hit event count follows actual native hits");
            await Wait();
            LibrarianPreferences050.Current.CardEffects=false;observed.Clear();
            await CardCmd.AutoPlay(context,player.Creature.CombatState.CreateCard<LibrarianStrike>(player),enemy,skipCardPileVisuals:true);
            Check(observed.Count==0,"card effects off preserves resolution without new visual nodes");
            LibrarianPreferences050.Current.CardEffects=true;
            SaveManager.Instance.PrefsSave.FastMode=FastModeType.Instant;
            LibrarianCardVfx050.Emit(enemy,new(SpellElement050.Fire,SpellShape050.Ember),"impact","instant",player);
            Check(LibrarianCardVfx050.Active==0,"instant mode skips new effects");
            SaveManager.Instance.PrefsSave.FastMode=FastModeType.Fast;
            LibrarianPreferences050.Current.EffectLimit=4;
            for(int i=0;i<100;i++) LibrarianCardVfx050.Emit(enemy,new(SpellElement050.Water,SpellShape050.Ripple),"impact","stress",player);
            Check(LibrarianCardVfx050.Active<=4&&LibrarianCardVfx050.Dropped>=96,"100-event burst bounded to four nodes");
            await Wait();Check(LibrarianCardVfx050.Active==0,"expired effects reclaimed");
            LibrarianPreferences050.Current.ReducedMotion=true;
            LibrarianCardVfx050.Emit(enemy,new(SpellElement050.Leaf,SpellShape050.Root),"impact","reduced",player);
            await Wait(0.08);await Capture("reduced-motion");await Wait();
            LibrarianPreferences050.Reset();SaveManager.Instance.PrefsSave.FastMode=FastModeType.Normal;
            // Draw-call CPU comparison, warmed first; not a claim of live-multiplayer performance.
            var watch=Stopwatch.StartNew();
            for(int i=0;i<400;i++)LibrarianCardVfx050.Emit(enemy,new(SpellElement050.Page,SpellShape050.Pages),"impact","bounded-performance",player);
            watch.Stop();MainFile.Logger.Info($"V050_VFX_METRICS submit400_ms={watch.Elapsed.TotalMilliseconds:F3} active={LibrarianCardVfx050.Active} peak={LibrarianCardVfx050.Peak} dropped={LibrarianCardVfx050.Dropped}");
            var oldNodes=NCombatRoom.Instance!.CombatVfxContainer.GetChildren().OfType<LibrarianCardSpell050>().ToArray();
            await freshFight();
            while(player.PlayerCombatState!.Phase!=PlayerTurnPhase.Play) await Wait(0.05);
            await Wait(0.8);
            Check(oldNodes.All(n=>!GodotObject.IsInstanceValid(n)),"room transition frees all old effect nodes");
            Check(LibrarianCardVfx050.Active==0,"new opening-draw effects expire after play phase");
            if (visualOnly)
            {
                foreach (var roomType in new[]{MegaCrit.Sts2.Core.Rooms.RoomType.RestSite,MegaCrit.Sts2.Core.Rooms.RoomType.Shop})
                {
                    SaveManager.Instance.Progress.MarkFtueAsComplete(roomType==MegaCrit.Sts2.Core.Rooms.RoomType.RestSite?"rest_site_ftue":"merchant_ftue");
                    await MegaCrit.Sts2.Core.Runs.RunManager.Instance.EnterRoomDebug(roomType); await Wait(2);
                    Node room=roomType==MegaCrit.Sts2.Core.Rooms.RoomType.RestSite?NRestSiteRoom.Instance!:NMerchantRoom.Instance!;
                    var motion=room.FindChildren("*","",true,false).OfType<LibrarianCharacterMotion>().Single();
                    var mask=motion.GetParent().GetNode<Sprite2D>(motion.VisualPath+"/Mask");
                    Check(motion.Rig is not null&&mask.IsVisibleInTree(),roomType+" layered character visible in native room");
                    var point=mask.GetGlobalTransformWithCanvas().Origin;
                    Check(room.GetViewport().GetVisibleRect().HasPoint(point),roomType+" character stays on screen");
                    await Capture("room-"+roomType);
                }
                await freshFight();await Wait(2);
                MainFile.Logger.Info("V050_ROOMS_PASS nativeRest=True nativeShop=True");
            }
            MainFile.Logger.Info($"V050_CARD_AUDIT_PASS checks={_checks} cards=91 variants={(visualOnly?0:182)}");
        }
        finally{LibrarianCardVfx050.Observer=null;LibrarianPreferences050.Reset();SaveManager.Instance.PrefsSave.FastMode=oldFast;}
    }
}
