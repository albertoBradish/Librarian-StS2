using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Cards.OrbAdvancedBasics;
using Librarian.LibrarianCode.Cards.PowerCards;
using Librarian.LibrarianCode.Powers.Implemented;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Timeline;
using MegaCrit.Sts2.Core.TestSupport;

namespace Librarian.Mechanics;

internal static class DevelopmentBeta2Audit
{
    private static async Task Wait(double seconds) => await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
    private static async Task Capture(string name)
    {
        await NGame.Instance!.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        using var image=NGame.Instance.GetViewport().GetTexture().GetImage();
        string dir=@"D:\Slay The Spire_Mod Dev\outputs\revision-v1.0.0-stable\audit-history\revision-v1.0-beta2\screenshots";System.IO.Directory.CreateDirectory(dir);
        if(image.SavePng(System.IO.Path.Combine(dir,name+".png"))!=Error.Ok)throw new InvalidOperationException("beta2 capture "+name);
    }
    private static IEnumerable<Node> Descendants(Node root){foreach(var n in root.GetChildren()){yield return n;foreach(var c in Descendants(n))yield return c;}}
    internal static async Task Timeline(NMainMenu menu)
    {
        menu.GetNodeOrNull<LibrarianUpdateNotice051>("LibrarianUpdateNoticeScheduler")?.SetProcess(false);
        LibrarianNoticeHistory051.Acknowledge(LibrarianUpdateNotice051.CurrentVersion!);
        MegaCrit.Sts2.Core.Nodes.CommonUi.NModalContainer.Instance?.Clear();
        SaveManager.Instance.Progress.ObtainEpochOverride("NEOW_EPOCH",EpochState.Revealed);
        foreach(string lang in new[]{"zhs","eng"})
        {
            LibrarianLanguage.Select(lang);
            var timeline=menu.SubmenuStack.PushSubmenuType<NTimelineScreen>();await Wait(1);
            var slot=Descendants(timeline).OfType<NEpochSlot>().First(s=>s.model is LibrarianEpoch040);
            var inspect=timeline.GetNode<NEpochInspectScreen>("%EpochInspectScreen");
            foreach(var size in new[]{new Vector2I(1280,720),new Vector2I(1920,1080)})
            {
                DisplayServer.WindowSetSize(size);timeline.OpenInspectScreen(slot,playAnimation:false);await Wait(.5);
                if(inspect.GetNode<TextureRect>("%Portrait").Texture.ResourcePath!=LibrarianUnlocks040.Placeholder)throw new InvalidOperationException("wrong timeline texture "+lang);
                await Capture($"timeline-{lang}-{size.X}");inspect.Close();await Wait(.4);
            }
            menu.SubmenuStack.Pop();await Wait(.5);
        }
        DisplayServer.WindowSetSize(new(1280,720));LibrarianLanguage.Select("zhs");
        MainFile.Logger.Info("BETA2_TIMELINE_UI_PASS languages=2 resolutions=2");
    }
    internal static async Task Run(Player player, Func<Task> freshFight)
    {
        var ctx=new ThrowingPlayerChoiceContext(); int checks=0;
        void Check(bool ok,string label) { if(!ok)throw new InvalidOperationException("BETA2: "+label); checks++;MainFile.Logger.Info("BETA2_CHECK_PASS "+label); }
        LibrarianSession S()=>LibrarianRuntime.Get(player);
        async Task Frame()=>await NGame.Instance!.ToSignal(NGame.Instance.GetTree(),SceneTree.SignalName.ProcessFrame);
        async Task Reset(){await freshFight();for(int i=0;player.PlayerCombatState?.Phase!=PlayerTurnPhase.Play;i++){if(i>1200)throw new TimeoutException();await Frame();}}
        T Create<T>(bool up=false) where T:CardModel {var c=player.Creature.CombatState!.CreateCard<T>(player);if(up){c.UpgradeInternal();c.FinalizeUpgradeInternal();}return c;}
        Task Play(CardModel c)=>CardCmd.AutoPlay(ctx,c,null,skipCardPileVisuals:true);
        Task Dispatch(OrbOperationResult r)=>LibrarianRuntime.Dispatch(S(),ctx,r);
        async Task Turn(){int turn=player.Creature.CombatState!.RoundNumber;CombatManager.Instance.SetReadyToEndTurn(player,false);for(int i=0;player.Creature.CombatState!.RoundNumber==turn||player.PlayerCombatState!.Phase!=PlayerTurnPhase.Play;i++){if(i>3600)throw new TimeoutException();await Frame();}}
        foreach(bool up in new[]{false,true})
        {
            await Reset();var gravity=Create<TidalGravity>(up);int energy=player.PlayerCombatState!.Energy;
            await Play(gravity);
            Check(player.PlayerCombatState.Energy==energy+2,"gravity gains two energy "+up);
            Check(S().Orbs.LockedTurns(OrbKind.Tide)==(up?1:2),"gravity lock duration "+up);
            Check(gravity.Keywords.Contains(CardKeyword.Exhaust)==!up,"gravity exhaust keyword "+up);
            Check(gravity.Pile?.Type==(up?PileType.Discard:PileType.Exhaust),"gravity destination "+up);
            await Reset();var life=Create<LifeSymphony>(up);await Play(life);
            Check(S().Orbs.Value(OrbKind.Growth)==(up?11:7),"life symphony +1 "+up);
            Check(Create<EndlessTide>(up).Rarity==CardRarity.Rare&&Create<UnretreatingTide>(up).Rarity==CardRarity.Uncommon,"swapped rarity "+up);
            await Reset();var endless=Create<EndlessTide>(up);Check(endless.EnergyCost.GetWithModifiers(CostModifiers.None)==(up?1:2),"endless cost "+up);await Play(endless);
            S().Waves.Add(9);await Dispatch(S().Orbs.Gain(OrbKind.Tide,8));
            await S().Orbs.SettleImmediatelyAsync(OrbSelector.Named(OrbKind.Tide,OrbScope.All),1,r=>LibrarianRuntime.Settle(S(),ctx,r),"beta2");
            Check(S().Waves.Amount==9&&player.Creature.Block>0,"tide still gives block but no new waves "+up);
            await Turn();Check(S().Orbs.IsActivated(OrbKind.Tide),"real start turn activates tide "+up);
            Check(S().Waves.Amount<9,"old wave no unconditional payout/retention "+up);
        }
        await Reset();await Play(Create<EndlessTide>());await Dispatch(S().Orbs.Lock(OrbKind.Tide,2));await Turn();
        Check(!S().Orbs.IsActivated(OrbKind.Tide),"endless respects tide lock");
        await Reset();await Play(Create<OverlimitForm>());
        int flashes=0;LibrarianCardVfx050.Observer=(stage,target,source)=>{if(source=="OverlimitForm"&&stage=="trigger")flashes++;};
        await Turn();LibrarianCardVfx050.Observer=null;
        Check(S().Orbs.Positions.All(S().Orbs.IsActivated),"overlimit real turn activates all three");
        Check(flashes==1,"overlimit trigger visual emitted once");
        Check(GD.Load<Texture2D>("res://Librarian/images/vfx/v1.0-beta2/overlimit_sigil.svg")!=null,"dedicated overlimit resource loads");
        foreach(var size in new[]{new Vector2I(1280,720),new Vector2I(1920,1080)})
        {
            DisplayServer.WindowSetSize(size);await Wait(.2);
            LibrarianCardVfx050.PowerTriggered(player.Creature.GetPower<OverlimitFormPower>()!);await Wait(.12);await Capture("overlimit-"+size.X);
        }
        DisplayServer.WindowSetSize(new(1280,720));
        await Reset();
        foreach(var pile in new[]{PileType.Hand,PileType.Draw,PileType.Discard}) foreach(var card in pile.GetPile(player).Cards.ToArray())await CardPileCmd.Add(card,PileType.Exhaust);
        var bottom=Create<LibrarianDefend>();await CardPileCmd.Add(bottom,PileType.Discard);
        await LibrarianBottomPlay041.ResolveEndTurnBottomStageAsync(ctx,player,new(null,1));
        Check(player.Creature.Block>0&&bottom.Pile?.Type==PileType.Discard,"fuel empty draw pile shuffles and plays discard card");
        await CardPileCmd.Add(bottom,PileType.Exhaust);
        await LibrarianBottomPlay041.ResolveEndTurnBottomStageAsync(ctx,player,new(null,2));
        Check(PileType.Draw.GetPile(player).Cards.Count==0,"fuel both piles empty safely ends");
        var returnCard=Create<LibrarianDefend>();await CardPileCmd.Add(returnCard,PileType.Hand);
        Check(await LibrarianBottomPlay041.MoveToBottomAsync(ctx,returnCard)&&ReferenceEquals(LibrarianBottomPlay041.PeekBottom(player),returnCard),"return preserves card instance at bottom");
        // Native CardPileCmd awaits scheduling, not completion of NCardFlyVfx.
        // Let the native fly effect finish before tearing down this fixture's room.
        await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(3),SceneTreeTimer.SignalName.Timeout);
        await Play(Create<ToBeContinued>());
        var selected=Create<LibrarianDefend>();await CardPileCmd.Add(selected,PileType.Hand);
        await CardPileCmd.Add(Create<LibrarianStrike>(),PileType.Draw,CardPilePosition.Top);
        var selector=new TestCardSelector();selector.PrepareToSelect(new[]{selected});
        using(CardSelectCmd.PushSelector(selector))await player.Creature.GetPower<ToBeContinuedPower>()!.AfterPlayerTurnStart(ctx,player);
        Check(ReferenceEquals(LibrarianBottomPlay041.PeekBottom(player),selected),"continued draws then returns chosen instance");
        await Wait(1.1);await Capture("continued-after-return");await Wait(2);
        foreach(string lang in new[]{"zhs","eng"})
        {
            LibrarianLanguage.Select(lang);
            var dialogues=ModelDb.Event<TheArchitect>().DialogueSet.CharacterDialogues[ModelDb.Character<LibrarianCharacter>().Id.Entry];
            Check(dialogues.Count>0&&dialogues.All(d=>d.Lines.Count==3),"architect three actual dialogue lines "+lang);
            Check(dialogues.SelectMany(d=>d.Lines).All(l=>!l.LineText!.GetFormattedText().Contains("THE_ARCHITECT")),"architect localized "+lang);
            Check(GD.Load<Texture2D>(LibrarianUnlocks040.Placeholder) is { } picture&&picture.GetWidth()==1200,"plain timeline language texture "+lang);
            foreach(var card in new CardModel[]{Create<TidalGravity>(),Create<PracticeMakesPerfect>(),Create<ReadWidely>(),Create<BuildCanal>()})
            {
                string formatted=card.GetDescriptionForPile(PileType.None);int count=formatted.Split("energy_triangle_text.svg").Length-1;
                Check(count==card.DynamicVars["Energy"].IntValue,"native repeated energy icons "+lang+" "+card.GetType().Name);
            }
        }
        await CaptureCards(player,Create<TidalGravity>(),Create<EndlessTide>(),Create<UnretreatingTide>(),Create<LifeSymphony>(),Create<PracticeMakesPerfect>());
        LibrarianLanguage.Select("zhs");
        MainFile.Logger.Info($"BETA2_AUDIT_PASS checks={checks} liveMulticlient=False");
    }
    private static async Task CaptureCards(Player player,params CardModel[] cards)
    {
        var layer=new CanvasLayer{Layer=120};NGame.Instance!.AddChild(layer);var panel=new Control();layer.AddChild(panel);
        var background=new ColorRect{Color=new Color("20272e")};panel.AddChild(background);var nodes=new List<NCard>();
        foreach(var card in cards){var n=MegaCrit.Sts2.Core.Assets.PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>();panel.AddChild(n);n.Model=card;nodes.Add(n);}
        try { foreach(var resolution in new[]{new Vector2I(1280,720),new Vector2I(1920,1080)})
        {
            DisplayServer.WindowSetSize(resolution);await NGame.Instance.ToSignal(NGame.Instance.GetTree(),SceneTree.SignalName.ProcessFrame);
            var size=NGame.Instance.GetViewport().GetVisibleRect().Size;background.Size=size;
            for(int i=0;i<nodes.Count;i++){nodes[i].Scale=Vector2.One*Math.Min(size.X/1700,size.Y/600);nodes[i].Position=new(size.X*(i+0.5f)/nodes.Count,size.Y/2);}
            foreach(string lang in new[]{"zhs","eng"}) foreach(bool up in new[]{false,true})
            {
                LibrarianLanguage.Select(lang);
                foreach(var n in nodes){if(up&&!n.Model.IsUpgraded){n.Model.UpgradeInternal();n.Model.FinalizeUpgradeInternal();}else if(!up&&n.Model.IsUpgraded)n.Model.DowngradeInternal();n.UpdateVisuals(PileType.Hand,CardPreviewMode.Normal);}
                await NGame.Instance.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                using var img=NGame.Instance.GetViewport().GetTexture().GetImage();string dir=@"D:\Slay The Spire_Mod Dev\outputs\revision-v1.0.0-stable\audit-history\revision-v1.0-beta2\screenshots";System.IO.Directory.CreateDirectory(dir);img.SavePng(System.IO.Path.Combine(dir,$"cards-{lang}-{resolution.X}-{up}.png"));
            }
        }}finally{layer.QueueFree();DisplayServer.WindowSetSize(new(1280,720));await NGame.Instance.ToSignal(NGame.Instance.GetTree(),SceneTree.SignalName.ProcessFrame);}
    }
}
