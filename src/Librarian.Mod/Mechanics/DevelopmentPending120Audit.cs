using System.IO;
using System.Text.Json;
using Godot;
using HarmonyLib;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Cards.OrbAdvancedBasics;
using Librarian.LibrarianCode.Potions;
using Librarian.LibrarianCode.Powers.Implemented;
using Librarian.LibrarianCode.Relics;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace Librarian.Mechanics;

/// <summary>Opt-in R01–R04/P01–P02 and block-projection regression using native actions.</summary>
internal static class DevelopmentPending120Audit
{
    private static Player? _watch;
    private static int? _blockAfterEnd;
    [HarmonyPatch(typeof(LibrarianCombatHooks), nameof(LibrarianCombatHooks.BeforeSideTurnEnd))]
    private static class ObserveEnd
    {
        private static bool Prepare() => System.Environment.GetEnvironmentVariable("LIBRARIAN_041_FOCUS") is "120-pending" or "ritsu-full";
        private static void Postfix(CombatSide side, IEnumerable<Creature> participants, ref Task __result)
        {
            var player = _watch;
            if (side == CombatSide.Player && player is not null && participants.Contains(player.Creature))
                __result = Observe(__result, player);
        }
        private static async Task Observe(Task original, Player player)
        {
            await original;
            if (_watch == player)
                _blockAfterEnd = player.Creature.Block;
        }
    }
    internal static async Task Run(Player player, Func<Task> freshFight)
    {
        var context = new ThrowingPlayerChoiceContext();
        string output = System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT")!;
        Directory.CreateDirectory(output);
        int checks = 0, turns = 0, manual = 0;
        var evidence = new List<object>();
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("120 pending: " + label);
            checks++; MainFile.Logger.Info("PENDING120_CHECK_PASS " + label);
        }
        async Task Frame() => await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
        async Task Wait(Func<bool> ready, string label)
        {
            for (int i = 0; !ready(); i++) { if (i > 3600) throw new TimeoutException(label); await Frame(); }
        }
        LibrarianSession S() => LibrarianRuntime.Get(player);
        Creature Enemy() => player.Creature.CombatState!.HittableEnemies.First();
        async Task Reset()
        {
            await freshFight(); await Wait(() => player.PlayerCombatState!.Phase == PlayerTurnPhase.Play, "native Play phase");
            foreach (var relic in player.Relics.ToArray()) await RelicCmd.Remove(relic);
        }
        Task Dispatch(OrbOperationResult operation) => LibrarianRuntime.Dispatch(S(), context, operation);
        T Card<T>() where T : CardModel => player.Creature.CombatState!.CreateCard<T>(player);
        async Task Manual(CardModel card)
        {
            await CardPileCmd.Add(card, PileType.Hand);
            var action = new PlayCardAction(card, card.TargetType == TargetType.AnyEnemy ? Enemy() : null);
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
            await Wait(() => action.CompletionTask.IsCompleted, "manual card action");
            Check(action.Exception is null && action.CompletionTask.IsCompletedSuccessfully, "native manual action " + card.Id);
            manual++;
        }
        void Resources(int energy, int stars)
        {
            var combat = player.PlayerCombatState!;
            combat.LoseEnergy(combat.Energy); combat.GainEnergy(energy);
            combat.LoseStars(combat.Stars); combat.GainStars(stars);
        }
        ResourceInfo Last(CardModel card) => CombatManager.Instance.History.CardPlaysFinished.Last(h => h.CardPlay.Card == card).CardPlay.Resources;
        async Task Turn(int expected, string label)
        {
            var preview = LibrarianEndTurnPreview.Read(S());
            Check(preview.Exact && preview.FinalMinimum == expected && player.Creature.Block + preview.Minimum == expected,
                "preview " + label + "=" + JsonSerializer.Serialize(preview));
            _watch = player; _blockAfterEnd = null;
            int round = player.Creature.CombatState!.RoundNumber;
            CombatManager.Instance.SetReadyToEndTurn(player, false);
            await Wait(() => player.Creature.CombatState!.RoundNumber != round && player.PlayerCombatState!.Phase == PlayerTurnPhase.Play, "end turn " + label);
            _watch = null;
            Check(_blockAfterEnd == expected, "actual end-turn Block " + label + "=" + _blockAfterEnd);
            evidence.Add(new { label, expected, preview, actual = _blockAfterEnd }); turns++;
        }
        IEnumerable<Node> Desc(Node node)
        {
            foreach (var child in node.GetChildren()) { yield return child; foreach (var next in Desc(child)) yield return next; }
        }
        async Task Shot(string label)
        {
            await Wait(() => !Desc(NGame.Instance!).OfType<Control>().Any(c => c is NCombatStartBanner or NPlayerTurnBanner && c.IsVisibleInTree()),"banner finished before screenshot");
            for (int i = 0; i < 24; i++) await Frame();
            await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
            Check(image.SavePng(Path.Combine(output,label + ".png")) == Error.Ok, "screenshot " + label);
        }
        string language = LibrarianLanguage.Selected;
        bool bar = LibrarianPreferences050.Current.WaveBar, reduced = LibrarianPreferences050.Current.ReducedMotion;
        try
        {
            await Reset();
            var models = ModelDb.CardPool<LibrarianCardPool>().AllCards.ToArray();
            Check(models.Length == 91, "91 active card models");
            var texts = new List<object>();
            foreach (string lang in new[] { "zhs", "eng" })
            {
                LibrarianLanguage.Select(lang);
                foreach (var model in models)
                {
                    var card = model.ToMutable(); card.Owner = player;
                    foreach (bool upgraded in new[] { false,true })
                    {
                        if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
                        string text = card.GetDescriptionForPile(PileType.None);
                        Check(!text.Contains('{') && !text.Contains('}'), "formatted " + lang + " " + card.Id + " " + upgraded);
                        texts.Add(new { id = card.Id.Entry, lang, upgraded, text });
                    }
                }
                DevelopmentRelicAudit.ValidateModels();
                Check(LibrarianHoverTips.Tip("WAVES").Description.Contains(lang == "zhs" ? "不低于" : "at least"), "new Waves wording " + lang);
            }
            File.WriteAllText(Path.Combine(output,"formatted-cards.json"),JsonSerializer.Serialize(texts));
            LibrarianLanguage.Select("zhs");
            await Reset();
            var fire = await RelicCmd.Obtain<LibrarianCommonPlaceholder>(player);
            await fire.BeforeCombatStart();
            int initial = S().Orbs.Value(OrbKind.Fire);
            await fire.BeforeSideTurnStart(context, CombatSide.Player, [player.Creature], player.Creature.CombatState!);
            await fire.BeforeSideTurnStart(context, CombatSide.Player, [player.Creature], player.Creature.CombatState!);
            Check(S().Orbs.Value(OrbKind.Fire) == initial + 7, "Fire Starter seven once");
            foreach (var mode in new[] { "energy", "stars", "mixed", "energy-x", "star-x", "free" })
            {
                await Reset(); var dust = await RelicCmd.Obtain<LibrarianUncommonPlaceholderTwo>(player); await dust.BeforeCombatStart();
                CardModel card = mode switch { "energy-x" => Card<EarthCollapse>(), "star-x" => Card<Stardust>(), _ => Card<Comet>() };
                card.AddKeyword(CardKeyword.Exhaust);
                if (mode is not "energy-x" and not "star-x")
                {
                    card.EnergyCost.SetCustomBaseCost(mode is "energy" or "mixed" ? 2 : 0);
                    card.SetStarCostThisCombat(mode is "stars" or "mixed" ? 5 : 0);
                }
                Resources(mode == "energy-x" ? 3 : 0, mode == "star-x" ? 4 : 0);
                await CardPileCmd.Add(card,PileType.Hand);
                Check(card.CanPlay() && dust.Qualifies(card), "free eligibility no resources " + mode);
                var canceled = new PlayCardAction(card,card.TargetType == TargetType.AnyEnemy ? Enemy() : null); canceled.Cancel();
                Check(dust.Qualifies(card), "cancel does not consume " + mode);
                int hp = Enemy().CurrentHp;
                await Manual(card);
                var resource = Last(card);
                Check(player.PlayerCombatState!.Energy == (mode == "energy-x" ? 3 : 0)
                    && player.PlayerCombatState.Stars == (mode == "star-x" ? 4 : 0), "both native resources retained " + mode);
                Check(resource.EnergySpent == 0 && resource.StarsSpent == 0
                    && resource.EnergyValue == (mode == "energy-x" ? 3 : 0)
                    && resource.StarValue == (mode == "star-x" ? 4 : 0), "native resource history " + mode);
                Check(!dust.Qualifies(card), "one use " + mode);
                if(mode == "energy-x") Check(Enemy().CurrentHp == hp-24, "energy X effects preserved");
                if(mode == "star-x") Check(Enemy().CurrentHp == hp-20, "star X effects preserved");
                if(mode == "mixed") Check(card.GetStarCostWithModifiers()==5 && card.EnergyCost.GetWithModifiers(CostModifiers.All)==2, "fixed costs restored");
                evidence.Add(new { mode, resource });
            }
            await Reset();
            var autoDust=await RelicCmd.Obtain<LibrarianUncommonPlaceholderTwo>(player); await autoDust.BeforeCombatStart();
            var auto=Card<Comet>(); auto.AddKeyword(CardKeyword.Exhaust); Resources(2,5);
            await CardCmd.AutoPlay(context,auto,Enemy(),skipCardPileVisuals:true);
            Check(!autoDust.Qualifies(auto) && player.PlayerCombatState!.Stars==5, "auto play consumes first use without spending");
            var unplayable=Card<Comet>(); unplayable.AddKeyword(CardKeyword.Exhaust); unplayable.AddKeyword(CardKeyword.Unplayable);
            await autoDust.BeforeCombatStart(); await CardCmd.AutoPlay(context,unplayable,Enemy(),skipCardPileVisuals:true);
            Check(autoDust.Qualifies(unplayable), "unplayable auto attempt does not consume");
            await Reset(); await RelicCmd.Obtain<LibrarianRarePlaceholderTwo>(player);
            await Dispatch(S().Orbs.Gain(OrbKind.Fire,4)); await Dispatch(S().Orbs.Gain(OrbKind.Tide,8)); await Dispatch(S().Orbs.Gain(OrbKind.Growth,0));
            int enemyHp=Enemy().CurrentHp, count=S().Orbs.SettlementsThisCombat;
            await S().Orbs.SettleImmediatelyAsync(OrbSelector.Named(OrbKind.Growth,OrbScope.All),2,r=>LibrarianRuntime.Settle(S(),context,r),"vines-audit");
            Check(Enemy().CurrentHp==enemyHp-14 && S().Orbs.SettlementsThisCombat==count+2, "Vines two zero-value settlements deal seven each");
            Check(S().Orbs.Value(OrbKind.Fire)==4 && S().Orbs.Value(OrbKind.Tide)==8, "Vines no longer strengthens high Orb");
            await PowerCmd.Apply<StrengthPower>(context,player.Creature,99,player.Creature,null);
            await S().Orbs.SettleImmediatelyAsync(OrbSelector.Named(OrbKind.Growth,OrbScope.All),1,r=>LibrarianRuntime.Settle(S(),context,r),"vines-unpowered");
            Check(Enemy().CurrentHp==enemyHp-21, "Vines Unpowered ignores Strength");
            foreach(var kind in new[] {OrbKind.Tide,OrbKind.Growth,OrbKind.Fire})
            {
                await Reset(); await RelicCmd.Obtain<LibrarianShopPlaceholder>(player);
                foreach(var orb in new[] {OrbKind.Fire,OrbKind.Tide,OrbKind.Growth}) await Dispatch(S().Orbs.Gain(orb,orb==OrbKind.Tide?8:orb==OrbKind.Growth?12:100));
                await Dispatch(S().Orbs.Activate(kind,OrbScope.All));
                var preview=LibrarianEndTurnPreview.Read(S()); Check(preview.Exact,"badge deterministic "+kind+" "+JsonSerializer.Serialize(preview));
                await Turn(preview.Minimum!.Value,"badge-front-"+kind);
                Check(S().Orbs.SettlementsThisCombat==4,"badge exactly one extra "+kind);
            }
            await Reset(); await RelicCmd.Obtain<LibrarianShopPlaceholder>(player);
            await Dispatch(S().Orbs.Gain(OrbKind.Tide,8)); S().Waves.Add(14); await Turn(14,"badge-inactive-condition");
            Check(S().Orbs.SettlementsThisCombat==1,"badge inactive does not trigger");
            await Reset();
            foreach (var potion in player.Potions.ToArray()) await PotionCmd.Discard(potion);
            var clarity=(await PotionCmd.TryToProcure<ClarityPotion>(player)).potion;
            await clarity.OnUseWrapper(context,player.Creature); Check(S().Waves.Amount==9,"Clarity nine Waves");
            var kindling=(await PotionCmd.TryToProcure<KindlingPotion>(player)).potion;
            await kindling.OnUseWrapper(context,player.Creature); Check(S().Orbs.Value(OrbKind.Fire)==6 && S().Orbs.IsActivated(OrbKind.Fire),"Kindling six Fire and activation");
            foreach(var fixture in new[] { (14,20,false,false,false,20),(14,8,false,false,false,14),(14,20,true,false,false,14),
                (14,20,false,true,false,14),(14,20,false,false,true,14),(0,20,false,false,false,20) })
            {
                var (waves,tide,back,inactive,locked,expected)=fixture;
                await Reset(); await Dispatch(S().Orbs.Gain(OrbKind.Tide,tide));
                if(back) await Dispatch(S().Orbs.Gain(OrbKind.Fire,0));
                if(inactive) await Dispatch(S().Orbs.Extinguish(OrbKind.Tide,OrbScope.All));
                if(locked) await Dispatch(S().Orbs.Lock(OrbKind.Tide)); S().Waves.Add(waves);
                string before=JsonSerializer.Serialize(S().Orbs.Snapshot()), rng=JsonSerializer.Serialize(player.RunState.Rng.ToSerializable());
                for(int i=0;i<20;i++) LibrarianEndTurnPreview.Read(S());
                Check(before==JsonSerializer.Serialize(S().Orbs.Snapshot()) && rng==JsonSerializer.Serialize(player.RunState.Rng.ToSerializable()) && S().Waves.Amount==waves,"preview leaves state and RNG intact "+fixture);
                await Turn(expected,"example-"+fixture);
            }
            await Reset(); await Dispatch(S().Orbs.Gain(OrbKind.Fire,100)); await Dispatch(S().Orbs.Gain(OrbKind.Tide,8)); await Dispatch(S().Orbs.Gain(OrbKind.Growth,12));
            S().Orbs.QueueExtraSettlement(OrbSelector.Named(OrbKind.Tide,OrbScope.All),2,"audit-extra"); await Turn(50,"Growth before Tide and full extras");
            await Reset(); S().Waves.Add(9); var pending=await PowerCmd.Apply<DeepSeaPendingPower>(context,player.Creature,30,player.Creature,null); pending!.Schedule(S(),30);
            await PowerCmd.Apply<CooldownPower>(context,player.Creature,1,player.Creature,null); await Turn(39,"scheduled Waves pay this turn with Cooldown");
            Check(S().Waves.Amount==39,"Cooldown preserves delayed Waves");
            await Reset(); await Dispatch(S().Orbs.Gain(OrbKind.Tide,20)); await PowerCmd.Apply<EndlessTidePower>(context,player.Creature,1,player.Creature,null); await Turn(20,"Endless Tide no Wave gain");
            await Reset(); S().Waves.Add(14); await PowerCmd.Apply<UnretreatingTidePower>(context,player.Creature,10,player.Creature,null);
            await Turn(14,"Wave floor"); Check(S().Waves.Amount==10,"Wave floor retained after decay");
            await Reset(); S().Waves.Add(14); await PowerCmd.Apply<RidgeWardPower>(context,player.Creature,1,player.Creature,null);
            await Turn(14,"Ridge Ward retention"); Check(S().Waves.Amount==14,"Ridge Ward does not decay");
            await Reset(); await Dispatch(S().Orbs.Gain(OrbKind.Tide,20)); await Dispatch(S().Orbs.Gain(OrbKind.Fire,100));
            await PowerCmd.Apply<EternalGrimoirePower>(context,player.Creature,1,player.Creature,null); await Turn(20,"Eternal full background Tide");
            await Reset(); await Dispatch(S().Orbs.Gain(OrbKind.Fire,100)); await Dispatch(S().Orbs.Gain(OrbKind.Tide,8)); await Dispatch(S().Orbs.Gain(OrbKind.Growth,12));
            await PowerCmd.Apply<LifeSymphonyPendingPower>(context,player.Creature,1,player.Creature,null);
            await Turn(10,"Life Symphony extra Growth after natural Tide");
            await Reset(); await Dispatch(S().Orbs.Gain(OrbKind.Tide,20));
            await PowerCmd.Apply<DexterityPower>(context,player.Creature,99,player.Creature,null); await PowerCmd.Apply<FrailPower>(context,player.Creature,3,player.Creature,null);
            await Turn(20,"Unpowered ignores Dexterity and Frail");
            await Reset(); await Dispatch(S().Orbs.Gain(OrbKind.Tide,20));
            await LibrarianRuntime.GainTidalBlock(S(),7);
            S().Orbs.BlockLedger.Consume(7);
            S().Orbs.BlockLedger.RecordTideGain(7,S().Orbs.OwnerTurn-1);
            var expiry=LibrarianEndTurnPreview.Read(S()); Check(expiry.ExpiringBlock==7 && expiry.Minimum==13 && expiry.FinalMinimum==20,"old Tidal Block expiry is included in net preview");
            await Turn(20,"old Tidal Block expires before new gain");
            await Reset(); await RelicCmd.Obtain<LibrarianRarePlaceholderOne>(player); S().Orbs.Strengthen(OrbKind.Tide,20,OrbScope.All);
            string randomBefore=JsonSerializer.Serialize(player.RunState.Rng.ToSerializable());
            var range=LibrarianEndTurnPreview.Read(S());
            for(int i=0;i<20;i++) LibrarianEndTurnPreview.Read(S());
            Check(range.Minimum==0 && range.Maximum==20 && randomBefore==JsonSerializer.Serialize(player.RunState.Rng.ToSerializable()),"Unstable Spell enumerates range without RNG");
            LibrarianPreferences050.Current.WaveBar=true; LibrarianPreferences050.Current.ReducedMotion=true;
            await CreatureCmd.SetMaxAndCurrentHp(player.Creature,72);
            await Shot("preview-random-range");
            await PowerCmd.Apply<FuelTheFirePower>(context,player.Creature,1,player.Creature,null);
            Check(LibrarianEndTurnPreview.Read(S()).Uncertainty=="bottom","bottom automatic chain reports uncertainty");
            await Shot("preview-uncertain-bottom");
            await Reset(); await Dispatch(S().Orbs.Gain(OrbKind.Tide,20));
            await CreatureCmd.SetMaxAndCurrentHp(player.Creature,72);
            LibrarianPreferences050.Current.WaveBar=true; LibrarianPreferences050.Current.ReducedMotion=true;
            foreach(var lang in new[] {"zhs","eng"}) foreach(var size in new[] {new Vector2I(1280,720),new Vector2I(1920,1080)})
            {
                LibrarianLanguage.Select(lang); DisplayServer.WindowSetSize(size); await Shot("preview-"+lang+"-"+size.X);
                var band=Desc(NCombatRoom.Instance!).OfType<LibrarianWaveBar>().Single();
                Check(band.Visible && band.GetNode<Label>("WaveAmount").Text=="+20","zero-Waves blue band visible "+lang+size.X);
                if(lang=="zhs" && size.X==1280)
                {
                    band.GetNode<Label>("WaveAmount").AddThemeFontSizeOverride("font_size",22);
                    await Shot("preview-font22-zhs-1280");
                    band.GetNode<Label>("WaveAmount").AddThemeFontSizeOverride("font_size",24);
                }
                band.EmitSignal(Control.SignalName.MouseEntered); await Shot("preview-hover-"+lang+"-"+size.X); band.EmitSignal(Control.SignalName.MouseExited);
            }
            LibrarianPreferences050.Current.WaveBar=false; for(int i=0;i<20;i++) await Frame();
            Check(!Desc(NCombatRoom.Instance!).OfType<LibrarianWaveBar>().Single().Visible,"blue band setting hides preview");
            LibrarianPreferences050.Current.WaveBar=true; LibrarianPreferences050.Current.ReducedMotion=false;
            await Dispatch(S().Orbs.Strengthen(OrbKind.Tide,100000,OrbScope.All));
            await Shot("preview-long-number-motion");
            var longBand=Desc(NCombatRoom.Instance!).OfType<LibrarianWaveBar>().Single();
            Check(longBand.GetNode<Label>("WaveAmount").Text=="+100020" && longBand.GetNode<Label>("WaveAmount").GetMinimumSize().X<=220,"long preview label fits");
            await Dispatch(S().Orbs.Lose(OrbKind.Tide,100000,OrbScope.All));
            await CreatureCmd.GainBlock(player.Creature,31,MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered,null);
            await Shot("preview-existing-shield");
            Check(player.Creature.Block==31 && LibrarianEndTurnPreview.Read(S()).Minimum==20,"existing shield separate from new preview");
            // Restore a normal state before the driver's real save/reload and retain the revised IDs.
            await Reset();
            await RelicCmd.Obtain<LibrarianCommonPlaceholder>(player); await RelicCmd.Obtain<LibrarianUncommonPlaceholderTwo>(player);
            await RelicCmd.Obtain<LibrarianRarePlaceholderTwo>(player); await RelicCmd.Obtain<LibrarianShopPlaceholder>(player);
            File.WriteAllText(Path.Combine(output,"evidence.json"),JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
            MainFile.Logger.Info($"PENDING120_AUDIT_PASS checks={checks} realTurns={turns} manualPlays={manual} cards=91 phases=364 liveMulticlient=False");
        }
        finally { _watch=null; LibrarianLanguage.Select(language); LibrarianPreferences050.Current.WaveBar=bar; LibrarianPreferences050.Current.ReducedMotion=reduced; }
    }

    internal static void AfterReload(Player player)
    {
        foreach(var type in new[] { typeof(LibrarianCommonPlaceholder),typeof(LibrarianUncommonPlaceholderTwo),typeof(LibrarianRarePlaceholderTwo),typeof(LibrarianShopPlaceholder) })
            if(!player.Relics.Any(r=>r.GetType()==type)) throw new InvalidOperationException("120 revised relic missing after native reload: "+type.Name);
        MainFile.Logger.Info("PENDING120_RELOAD_PASS revisedRelics=4");
    }
}
