using Godot;
using System.Text.Json;
using System.IO;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.OrbAdvancedBasics;
using Librarian.LibrarianCode.Cards.OrbBasics;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Cards.PowerCards;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace Librarian.Mechanics;

// User-requested simple smoke test: no screenshots, no repeat/performance/full-release matrix.
internal static partial class DevelopmentRuntimeAudit
{
    private static class FaultyPortrait1311
    {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static void TryReplacePortrait(NCard card) => throw new ObjectDisposedException("Godot.CompressedTexture2D");
    }
    private static async Task Run1311Simple()
    {
        LibrarianUnlocks040.ApplyChoice(SaveManager.Instance.Progress, true);
        var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<LibrarianCharacter>(), true,
            ActModel.GetDefaultList(), [], "LIBRARIAN1311", GameMode.Standard);
        _player = run.Players.Single();
        async Task Frame() => await NGame.Instance.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
        async Task Reset()
        {
            await FreshFight();
            foreach (var relic in _player.Relics.ToArray()) await RelicCmd.Remove(relic);
            for (int i=0; _player.PlayerCombatState?.Phase != PlayerTurnPhase.Play; i++)
            { if(i>1200) throw new TimeoutException("1311 Play phase"); await Frame(); }
        }
        T Create<T>(bool up=false) where T: CardModel
        {
            var c=_player.Creature.CombatState!.CreateCard<T>(_player);
            if(up) { c.UpgradeInternal(); c.FinalizeUpgradeInternal(); }
            return c;
        }
        async Task Turn()
        {
            int turn=_player.PlayerCombatState!.TurnNumber;
            CombatManager.Instance.SetReadyToEndTurn(_player,false);
            for(int i=0; _player.PlayerCombatState!.TurnNumber<=turn || _player.PlayerCombatState.Phase!=PlayerTurnPhase.Play; i++)
            { if(i>2400) throw new TimeoutException("1311 next turn"); await Frame(); }
        }
        IEnumerable<Node> Desc(Node root)
        { foreach(var child in root.GetChildren()) { yield return child; foreach(var d in Desc(child)) yield return d; } }
        await Reset();
        Equal(true,LibrarianPortraitCompatibility1311.InstallFor(typeof(FaultyPortrait1311)),"issue4 logged portrait signature patched");
        var portraitNode=MegaCrit.Sts2.Core.Assets.PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>();
        NGame.Instance.AddChild(portraitNode); portraitNode.Model=Create<RidgeWard>();
        FaultyPortrait1311.TryReplacePortrait(portraitNode);
        portraitNode.Model=_player.Creature.CombatState!.CreateCard<MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad>(_player);
        bool foreignFault=false;
        try { FaultyPortrait1311.TryReplacePortrait(portraitNode); } catch(ObjectDisposedException) { foreignFault=true; }
        Equal(true,foreignFault,"issue4 other characters keep original portrait handler");
        portraitNode.QueueFree();
        string lang=LibrarianLanguage.Selected;
        bool textRecheck = System.Environment.GetEnvironmentVariable("LIBRARIAN_1311_TEXT_RECHECK") == "1";
        var ids=new HashSet<string>(["OVERFISHING","BLAZING_CHAPTER","READ_WIDELY","SCATTERED_FLAMES","OPENING_TIDE","DAM_BREAK","EMBER_RECKONING","EMBER_PIERCE","EARTH_COLLAPSE","ROOTBIND","GAP_NEEDLE","MULTIPLAYER_PLACEHOLDER_B","ANCIENT_CATALOG","TIDAL_MARK","ARCHIVE_BULWARK","EVAPORATION","WAVE_CURTAIN","SPRINGWATER","REIGNITE","WAVE_STRIKE","STUDY_NOTES","RIDGE_WARD"]);
        var models=LibrarianRitsuCardRegistration.LegacyEntries.Keys.Where(t=>typeof(CardModel).IsAssignableFrom(t))
            .Select(t=>ModelDb.GetById<CardModel>(new ModelId("CARD",LibrarianRitsuCardRegistration.LegacyEntries[t])))
            .Where(c=>ids.Contains(c.Id.Entry["LIBRARIAN-".Length..])).ToArray();
        if (textRecheck)
            models = ModelDb.CardPool<LibrarianCardPool>().AllCards.Append(ModelDb.Card<StudyNotes>()).ToArray();
        Equal(textRecheck ? 92 : 22,models.Length,"1311 selected description models");
        var texts=new List<object>();
        foreach(string language in new[]{"zhs","eng"})
        {
            LibrarianLanguage.Select(language);
            foreach(var model in models)
                foreach(bool up in new[]{false,true})
                {
                    var c=model.ToMutable(); c.Owner=_player;
                    if(up) { c.UpgradeInternal(); c.FinalizeUpgradeInternal(); }
                    string text=c.GetDescriptionForPile(PileType.None);
                    if(text.Contains('{') || text.Contains('}')) throw new InvalidOperationException("1311 formatter "+c.Id);
                    _=c.HoverTips.ToArray();
                    var node=MegaCrit.Sts2.Core.Assets.PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>();
                    NGame.Instance.AddChild(node); node.Model=c; node.UpdateVisuals(PileType.None,CardPreviewMode.Normal);
                    node.QueueFree(); await Frame();
                    texts.Add(new{language,id=c.Id.Entry,up,text});
                }
        }
        LibrarianLanguage.Select(lang);
        string output=System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT")!;
        File.WriteAllText(Path.Combine(output,"formatted.json"),JsonSerializer.Serialize(texts));
        if (textRecheck) DevelopmentTextRecheck1311Audit.Run(_player, output);
        Equal(91,ModelDb.CardPool<LibrarianCardPool>().AllCards.Count(),"1311 normal pool remains 91");

        // Issue #4: real selection-style rendering, play Still Tide, then a real second turn and usable draw pile.
        await Gain(OrbKind.Tide,8);
        await Play<RidgeWard>();
        Equal(true,Session.Orbs.IsLocked(OrbKind.Tide),"issue4 Tide permanently locked");
        Equal(8,Session.Waves.Amount,"issue4 immediate settlement");
        await Turn();
        Equal(true,_player.PlayerCombatState!.Hand.Cards.Count>0,"issue4 next turn drew cards");
        foreach(var c in _player.PlayerCombatState.DrawPile.Cards) _=c.GetDescriptionForPile(PileType.Draw);
        await Play<LibrarianDefend>();
        Equal(true,_player.PlayerCombatState.Phase==PlayerTurnPhase.Play,"issue4 next turn card play completed");
        MainFile.Logger.Info("ISSUE4_SMOKE_PASS selectionDescription=True nextTurn=True draw=True play=True drawPile=True");

        await Reset();
        await Play<TidalMark>();
        await Play<DamBreak>();
        Equal(4,Session.Waves.Amount,"1311 lock grants four Waves");
        Equal(7,_player.Creature.Block,"1311 Dexterity precedes six Block");
        await Play<EmberReckoning>();
        Equal(2,_player.Creature.GetPower<StrengthPower>()!.Amount,"1311 Fire lock grants Strength");
        int hp=_enemy.CurrentHp;
        await Play<ScatteredFlames>();
        Equal(24,hp-_enemy.CurrentHp,"1311 four hits with Strength");
        int block=_player.Creature.Block;
        await Play<ArchiveBulwark>(true);
        Equal(20,_player.Creature.Block-block,"1311 four independent blocks each with Dexterity");
        hp=_enemy.CurrentHp;
        var earth=Create<EarthCollapse>(); earth.EnergyCost.CapturedXValue=0;
        await CardCmd.AutoPlay(Context,earth,_enemy,skipXCapture:true,skipCardPileVisuals:true);
        Equal(16,hp-_enemy.CurrentHp,"1311 X0 has two lock hits");
        await Play<StudyNotes>(true);
        Equal(5,_player.Creature.GetPower<StrengthPower>()!.Amount,"1311 upgraded notes Fire");
        Equal(3,_player.Creature.GetPower<DexterityPower>()!.Amount,"1311 upgraded notes Tide");

        await Reset();
        await Play<AncientCatalog>(); await Play<BlazingChapter>();
        await Gain(OrbKind.Fire,5);
        hp=_enemy.CurrentHp; await Play<WaveStrike>();
        Equal(6,hp-_enemy.CurrentHp,"1311 attack finishes before conversion");
        Equal(3,Session.Orbs.Value(OrbKind.Fire),"1311 conversion spends up to two Fire");
        Equal(2,_player.Creature.GetPower<StrengthPower>()!.Amount,"1311 conversion grants actual loss Strength");
        Equal(4,_player.Creature.GetPower<VigorPower>()!.Amount,"1311 first loss grants Vigor");
        Equal(3,Session.RevisionDamageBonus,"1311 Revision shared growth");
        var bottom=Create<WaveStrike>();
        await CardPileCmd.Add(bottom,PileType.Draw,CardPilePosition.Bottom);
        hp=_enemy.CurrentHp;
        await LibrarianBottomPlay041.TryPlayBottomAsync(Context,_player);
        Equal(30,hp-_enemy.CurrentHp,"1311 two equal Revision hits before one growth");
        Equal(6,Session.RevisionDamageBonus,"1311 bottom Revision grows once");
        Equal(4,_player.Creature.GetPower<StrengthPower>()!.Amount,"1311 multi-hit conversion only once");
        await CardPileCmd.Add(Create<Evaporation>(),PileType.Draw,CardPilePosition.Bottom);
        await LibrarianBottomPlay041.TryPlayBottomAsync(Context,_player);
        Equal(4,_player.Creature.GetPower<VigorPower>()!.Amount,"1311 Foreshadowing bottom Vigor");

        await Reset();
        await Play<EmberPierce>();
        Equal(true,_player.Creature.GetPower<BufferPower>() is null,"1311 first turn no Buffer");
        await Turn(); await Play<EmberPierce>();
        Equal(1,_player.Creature.GetPower<BufferPower>()!.Amount,"1311 no prior Tide settlement grants Buffer");
        await Gain(OrbKind.Tide,0); // Zero-value channel still reaches the actual settlement lifecycle.
        await Play<Springwater>();
        await Turn();
        int previous=_player.Creature.GetPower<BufferPower>()?.Amount??0;
        await Play<EmberPierce>();
        Equal(previous,_player.Creature.GetPower<BufferPower>()?.Amount??0,"1311 prior zero Tide settlement suppresses Buffer");

        // Real synchronized orb choice; click a native grid holder without screenshots.
        var study=Create<Overfishing>(true);
        var owned=new HookPlayerChoiceContext(study,LocalContext.NetId!.Value,_player.Creature.CombatState!,GameActionType.Combat);
        async Task PlayStudy()
        {
            await owned.AssignTaskAndWaitForPauseOrCompletion(CardCmd.AutoPlay(owned,study,target:null));
            await owned.WaitForCompletion();
        }
        var task=PlayStudy();
        for(int i=0; !task.IsCompleted; i++)
        {
            if(i>1800) throw new TimeoutException("1311 study selection");
            var screen=Desc(NGame.Instance).OfType<NChooseACardSelectionScreen>().FirstOrDefault(s=>s.IsVisibleInTree());
            if(screen is not null)
            {
                var holder=Desc(screen).OfType<NGridCardHolder>().FirstOrDefault();
                if(holder is not null) holder.EmitSignal(NCardHolder.SignalName.Pressed,holder);
            }
            await Frame();
        }
        await task; await owned.WaitForCompletion();
        await Turn();
        Equal(true,_player.PlayerCombatState!.Hand.Cards.OfType<StudyNotes>().Any(c=>c.IsUpgraded),"1311 upgraded notes generated at next start");

        // One native save and reload; keep no test save in the delivery folder.
        await SaveManager.Instance.SaveRun(null);
        var saved=SaveManager.Instance.LoadRunSave();
        Equal(true,saved.Success && saved.SaveData is not null,"1311 native save read");
        var restored=RunState.FromSerializable(saved.SaveData!);
        await NGame.Instance.ReturnToMainMenu();
        await RunManager.Instance.SetUpSavedSingleplayer(restored,saved.SaveData!);
        await NGame.Instance.LoadRun(restored,saved.SaveData!.PreFinishedRoom); await NGame.Instance.Transition.FadeIn();
        Equal(true,RunManager.Instance.IsInProgress,"1311 native save reload");
        MainFile.Logger.Info($"CARD1311_SIMPLE_PASS checks={_checks} descriptions={texts.Count} screenshots=0 fullReleaseMatrix=False");
        await NGame.Instance.ReturnToMainMenu();
        NGame.Instance.Quit();
    }
}
