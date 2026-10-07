using System.IO;
using System.Text.Json;
using Godot;
using HarmonyLib;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.OrbBasics;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;

namespace Librarian.Mechanics;

/// <summary>Observe native draws before the combined choice; drive actual turn-start controls.</summary>
internal static class DevelopmentContinuedOrderAudit
{
    private static ToBeContinuedPower? _watch;
    private static bool _active, _finished, _drawComplete, _drawBeforeChoice, _correctOwner;
    private static int _drawCalls, _requests, _requestedDraws, _handBeforeDraw, _handAtChoice, _minSelect, _maxSelect;
    private static CardModel[] _drawn = [], _selected = [];
    private static string _prompt = "";
    private static bool Enabled() => System.Environment.GetEnvironmentVariable("LIBRARIAN_RUNTIME_AUDIT") == "1"
        && System.Environment.GetEnvironmentVariable("LIBRARIAN_041_FOCUS") is "continued-order" or "pending-fixes-20261006";

    [HarmonyPatch(typeof(ToBeContinuedPower), nameof(ToBeContinuedPower.AfterPlayerTurnStart))]
    private static class ObservePower
    {
        private static bool Prepare() => Enabled();
        private static void Prefix(ToBeContinuedPower __instance, Player player)
        {
            if (__instance != _watch || __instance.Owner.Player != player || __instance.Owner.IsDead) return;
            _active = true; _finished = false; _drawComplete = false; _drawBeforeChoice = false;
            _drawCalls = 0; _requests = 0; _requestedDraws = 0; _minSelect = 0; _maxSelect = 0;
            _handBeforeDraw = PileType.Hand.GetPile(player).Cards.Count;
            _handAtChoice = -1; _drawn = []; _selected = []; _prompt = ""; _correctOwner = false;
        }
        private static void Postfix(ToBeContinuedPower __instance, Player player, ref Task __result)
        {
            if (__instance == _watch && __instance.Owner.Player == player && _active)
                __result = Complete(__result);
        }
        private static async Task Complete(Task original)
        {
            try { await original; _finished = true; }
            finally { _active = false; }
        }
    }

    [HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Draw), new[] { typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool) })]
    private static class ObserveDraw
    {
        private static bool Prepare() => Enabled();
        private static void Postfix(decimal count, Player player, bool fromHandDraw, ref Task<IEnumerable<CardModel>> __result)
        {
            if (_active && _watch?.Owner.Player == player && !fromHandDraw)
            {
                _drawCalls++; _requestedDraws += (int)count;
                __result = Complete(__result);
            }
        }
        private static async Task<IEnumerable<CardModel>> Complete(Task<IEnumerable<CardModel>> original)
        {
            var result = (await original).ToArray();
            _drawn = [.. _drawn, .. result]; _drawComplete = true;
            return result;
        }
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromHand))]
    private static class ObserveChoice
    {
        private static bool Prepare() => Enabled();
        private static void Prefix(PlayerChoiceContext context, Player player, CardSelectorPrefs prefs, AbstractModel source)
        {
            if (!_active || source != _watch) return;
            _requests++; _drawBeforeChoice = _drawComplete && _drawCalls == 1;
            _minSelect = prefs.MinSelect; _maxSelect = prefs.MaxSelect;
            _handAtChoice = PileType.Hand.GetPile(player).Cards.Count;
            _drawBeforeChoice &= _drawn.All(c => c.Pile?.Type == PileType.Hand);
            ulong? ownerId = AccessTools.Property(context.GetType(), "OwnerId")?.GetValue(context) as ulong?
                ?? (context as HookPlayerChoiceContext)?.Owner?.NetId;
            _correctOwner = _watch!.Owner.Player == player && ownerId == player.NetId;
            _prompt = prefs.Prompt.GetFormattedText();
            MainFile.Logger.Info($"CONTINUED_ORDER_CHOICE player={player.NetId} owner={ownerId} draws={_requestedDraws} completed={_drawComplete} actual={_drawn.Length} hand={_handAtChoice} min={_minSelect} max={_maxSelect} prompt={_prompt}");
        }
        private static void Postfix(AbstractModel source, ref Task<IEnumerable<CardModel>> __result)
        {
            if (_active && source == _watch) __result = Complete(__result);
        }
        private static async Task<IEnumerable<CardModel>> Complete(Task<IEnumerable<CardModel>> original)
        {
            _selected = (await original).ToArray();
            return _selected;
        }
    }

    private static IEnumerable<Node> Desc(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            yield return child;
            foreach (var node in Desc(child)) yield return node;
        }
    }

    internal static async Task Run(Player player, Func<Task> freshFight)
    {
        string output = System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT")
            ?? throw new InvalidOperationException("Continued order audit output required");
        Directory.CreateDirectory(output);
        int checks = 0, turns = 0, controls = 0, boundaryCases = 0;
        var evidence = new List<object>();
        var powerDescriptions = new List<object>();
        var describedAmounts = new HashSet<int>();
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("continued order: " + label);
            checks++; MainFile.Logger.Info("CONTINUED_ORDER_CHECK_PASS " + label);
        }
        async Task Frame() => await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
        async Task Wait(Func<bool> ready, string label)
        {
            for (int i = 0; !ready(); i++)
            {
                if (i >= 3600) throw new TimeoutException("continued order: " + label);
                await Frame();
            }
        }
        T Create<T>(bool upgraded = false) where T : CardModel
        {
            var card = player.Creature.CombatState!.CreateCard<T>(player);
            if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
            return card;
        }
        async Task Fill(int count, PileType pile)
        {
            for (int i = 0; i < count; i++) await CardPileCmd.Add(Create<LibrarianDefend>(), pile);
        }
        async Task Reset()
        {
            _watch = null;
            await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(3), SceneTreeTimer.SignalName.Timeout);
            await freshFight();
            await Wait(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play, "fresh Play phase");
            foreach (var relic in player.Relics.ToArray()) await RelicCmd.Remove(relic);
            foreach (var enemy in player.Creature.CombatState!.HittableEnemies) await CreatureCmd.SetMaxAndCurrentHp(enemy, 10000);
            await CreatureCmd.SetMaxAndCurrentHp(player.Creature, 10000);
            foreach (var pile in new[] { PileType.Hand, PileType.Draw, PileType.Discard, PileType.Exhaust })
                foreach (var card in pile.GetPile(player).Cards.ToArray()) await CardPileCmd.RemoveFromCombat(card);
        }
        async Task Layers(int layers, bool upgraded, bool mixed = false)
        {
            player.PlayerCombatState!.GainEnergy(layers);
            for (int i = 0; i < layers; i++)
            {
                bool up = mixed ? i % 2 == 1 : upgraded;
                var card = Create<ToBeContinued>(up);
                Check(card.EnergyCost.Canonical == 1 && card.Keywords.Contains(CardKeyword.Innate) == up,
                    $"unchanged cost and Innate layer={i} upgraded={up}");
                await CardPileCmd.Add(card, PileType.Hand);
                var action = new PlayCardAction(card, null);
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
                await Wait(() => action.CompletionTask.IsCompleted, "manual continued action");
                Check(action.Exception is null && action.CompletionTask.IsCompletedSuccessfully, "power card action completes");
            }
            _watch = player.Creature.GetPower<ToBeContinuedPower>();
            Check(_watch?.Amount == layers && player.Creature.Powers.OfType<ToBeContinuedPower>().Count() == 1,
                "native power stacks " + layers);
        }
        void Verify(int layers, int actualDraws, int returned, bool owned, string label)
        {
            Check(_finished && !_active, "hook completes " + label);
            Check(_drawCalls == 1 && _requestedDraws == layers && _drawn.Length == actualDraws,
                "one native batched draw " + label);
            Check(_requests == 1 && _drawBeforeChoice && _minSelect == layers && _maxSelect == layers,
                "all draws finish before one exact total choice " + label);
            Check(_handAtChoice == _handBeforeDraw + actualDraws, "all drawable cards available in choice " + label);
            Check(_selected.Length == returned && _selected.All(c => c.Owner == player && c.Pile?.Type == PileType.Draw),
                "selected cards return to owner draw pile " + label);
            Check(PileType.Draw.GetPile(player).Cards.TakeLast(returned).SequenceEqual(_selected), "selected cards at bottom " + label);
            Check(_watch!.Amount == layers, "power persists for later turns " + label);
            Check(!_prompt.Contains('{') && _prompt.Contains(layers.ToString()), "localized total prompt " + label);
            if (owned) Check(_correctOwner, "native choice context belongs to player " + label);
            if (layers is 2 or 3 && describedAmounts.Add(layers))
            {
                string previousLanguage = LibrarianLanguage.Selected;
                try
                {
                    foreach (string language in new[] { "zhs", "eng" })
                    {
                        LibrarianLanguage.Select(language);
                        string expected = language == "zhs"
                            ? $"回合开始时，抽{layers}张牌，再将{layers}张[gold]手牌[/gold]放到[gold]抽牌堆[/gold]底。"
                            : $"At the start of the turn, draw {layers} cards, then put {layers} cards from [gold]Hand[/gold] on the bottom of the [gold]Draw Pile[/gold].";
                        string description = _watch.GetDumbHoverTip().Description;
                        string smartDescription = _watch.HoverTips.OfType<HoverTip>().First().Description;
                        Check(description == expected, $"native power description draws then selects total Amount={layers} {language}");
                        Check(smartDescription == expected, $"live smart power description draws then selects total Amount={layers} {language}");
                        powerDescriptions.Add(new { language, amount = layers, description, smartDescription });
                        MainFile.Logger.Info($"CONTINUED_ORDER_POWER_TEXT language={language} Amount={layers} description={description} smartDescription={smartDescription}");
                    }
                }
                finally { LibrarianLanguage.Select(previousLanguage); }
            }
            evidence.Add(new { label, layers, actualDraws, returned, nativeTurn = owned, drawCalls = _drawCalls,
                selectionRequests = _requests, drawsFinishedBeforeChoice = _drawBeforeChoice,
                handBeforeDraw = _handBeforeDraw, handAtChoice = _handAtChoice, minSelect = _minSelect,
                maxSelect = _maxSelect, correctOwner = owned ? _correctOwner : (bool?)null, prompt = _prompt });
        }
        async Task Turn(int layers, string label)
        {
            _finished = false;
            int round = player.Creature.CombatState!.RoundNumber, beforeControls = controls;
            CombatManager.Instance.SetReadyToEndTurn(player, false);
            for (int frame = 0; !(_finished && player.Creature.CombatState!.RoundNumber > round && player.PlayerCombatState!.Phase == PlayerTurnPhase.Play); frame++)
            {
                if (frame >= 3600) throw new TimeoutException("continued native turn controls: " + label);
                var hand = NCombatRoom.Instance?.Ui.Hand;
                if (hand?.IsInCardSelection == true)
                {
                    Check(_drawBeforeChoice && _drawn.Length == layers, "combined native control shows completed draws " + label);
                    await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using (var image = NGame.Instance.GetViewport().GetTexture().GetImage())
                        Check(image.SavePng(Path.Combine(output, label + ".png")) == Error.Ok, "capture combined choice " + label);
                    var holders = Desc(hand).OfType<NHandCardHolder>().Where(h => h.IsVisibleInTree() && h.CardNode is not null).Take(layers).ToArray();
                    Check(holders.Length == layers, "enough native selection holders " + label);
                    foreach (var holder in holders) holder.EmitSignal(NCardHolder.SignalName.Pressed, holder);
                    if (hand.IsInCardSelection)
                    {
                        var confirm = hand.GetNode<NConfirmButton>("%SelectModeConfirmButton");
                        confirm.EmitSignal(NClickableControl.SignalName.Released, confirm);
                    }
                    controls++;
                    await Wait(() => !hand.IsInCardSelection, "combined control closes");
                }
                await Frame();
            }
            Verify(layers, layers, layers, true, label);
            Check(controls == beforeControls + 1 && CardSelectCmd.Selector is null, "one real synchronized hand control " + label);
            turns++;
        }
        try
        {
            Check(OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase), "isolated profile");
            Check(CardSelectCmd.Selector is null, "native selector active for real turns");
            foreach (var scenario in new[] { (1, false, false), (2, false, false), (3, false, true), (1, true, false), (2, true, false) })
            {
                await Reset(); await Layers(scenario.Item1, scenario.Item2, scenario.Item3); await Fill(25, PileType.Draw);
                string label = $"continued-{scenario.Item1}-{scenario.Item2}-{scenario.Item3}";
                await Turn(scenario.Item1, label);
                if (scenario.Item1 == 2 && !scenario.Item2) await Turn(2, label + "-next-turn");
            }
            // Fixed boundary fixtures still use native draw/hand commands. TestCardSelector
            // only supplies the choice; these cases do not claim real control coverage.
            foreach (var scenario in new[] { (10, 5, 2, 0), (9, 5, 3, 1), (4, 0, 3, 0), (4, 1, 3, 1), (1, 0, 3, 0), (0, 0, 3, 0) })
            {
                await Reset(); await Layers(scenario.Item3, false);
                await Fill(scenario.Item1, PileType.Hand); await Fill(scenario.Item2, PileType.Draw);
                int returned = Math.Min(scenario.Item3, scenario.Item1 + scenario.Item4);
                var selector = new TestCardSelector(); selector.PrepareToSelect(Enumerable.Range(0, returned));
                using (CardSelectCmd.PushSelector(selector))
                    await _watch!.AfterPlayerTurnStart(new ThrowingPlayerChoiceContext(), player);
                Verify(scenario.Item3, scenario.Item4, returned, false,
                    $"boundary-hand{scenario.Item1}-deck{scenario.Item2}-layers{scenario.Item3}");
                boundaryCases++;
            }
            _watch = null;
            await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(3), SceneTreeTimer.SignalName.Timeout);
            File.WriteAllText(Path.Combine(output, "continued-order-native-evidence.json"), JsonSerializer.Serialize(new
                { checks, turns, controls, boundaryCases, liveMulticlient = false, cases = evidence, powerDescriptions }, new JsonSerializerOptions { WriteIndented = true }));
            MainFile.Logger.Info($"CONTINUED_ORDER_AUDIT_PASS checks={checks} realTurns={turns} nativeControls={controls} boundaryCases={boundaryCases} liveMulticlient=False");
        }
        finally { _watch = null; _active = false; }
    }
}
