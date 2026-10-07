using System.IO;
using Godot;
using HarmonyLib;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.OrbBasics;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;

namespace Librarian.Mechanics;

/// <summary>Opt-in native end-turn regression for Fuel -> Threefold and recursive choice cards.</summary>
internal static class DevelopmentFuelFreezeAudit
{
    private static Player? _owner;
    private static int _requests, _hookEntries;
    private static bool _correctOwner, _duringEndTurn, _previousComplete;
    private static CardModel? _previousRequired;
    private static readonly List<PlayerChoiceContext> Contexts = [];
    private static readonly List<CardModel> Completed = [];
    private static readonly List<(CardModel Card, int Settlements, OrbKind? Orb)> ThreefoldResults = [];
    private static readonly List<decimal> EffectDraws = [];

    private static bool Enabled() => System.Environment.GetEnvironmentVariable("LIBRARIAN_041_FOCUS") is "fuel-freeze" or "pending-fixes-20261006"
        && System.Environment.GetEnvironmentVariable("LIBRARIAN_RUNTIME_AUDIT") == "1";

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromHand))]
    private static class ObserveHand
    {
        private static bool Prepare() => Enabled();
        private static void Prefix(PlayerChoiceContext context, Player player) => Observe(context, player);
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromChooseACardScreen))]
    private static class ObserveGrid
    {
        private static bool Prepare() => Enabled();
        private static void Prefix(PlayerChoiceContext context, Player player) => Observe(context, player);
    }

    [HarmonyPatch(typeof(LibrarianCombatHooks), nameof(LibrarianCombatHooks.BeforeSideTurnEnd))]
    private static class ObserveEndHook
    {
        private static bool Prepare() => Enabled();
        private static void Prefix(CombatSide side)
        {
            if (_owner is not null && side == CombatSide.Player) _hookEntries++;
        }
    }

    [HarmonyPatch(typeof(LibrarianCombatHooks), nameof(LibrarianCombatHooks.AfterCardPlayed))]
    private static class ObservePlayed
    {
        private static bool Prepare() => Enabled();
        private static void Prefix(CardPlay cardPlay)
        {
            if (_owner is null || !ReferenceEquals(cardPlay.Card.Owner, _owner)) return;
            Completed.Add(cardPlay.Card);
            if (cardPlay.Card is ThreefoldUnity)
            {
                var session = LibrarianRuntime.Get(_owner);
                ThreefoldResults.Add((cardPlay.Card, session.Orbs.SettlementsThisCombat, session.LastSettledOrb));
            }
        }
    }

    [HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Draw),
        new[] { typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool) })]
    private static class ObserveDraw
    {
        private static bool Prepare() => Enabled();
        private static void Prefix(decimal count, Player player, bool fromHandDraw)
        {
            if (_owner is not null && ReferenceEquals(_owner, player) && !fromHandDraw)
                EffectDraws.Add(count);
        }
    }

    private static void Observe(PlayerChoiceContext context, Player player)
    {
        if (_owner is null) return;
        _requests++;
        ulong? ownerId = AccessTools.Property(context.GetType(), "OwnerId")?.GetValue(context) as ulong?
            ?? (context as HookPlayerChoiceContext)?.Owner?.NetId;
        _correctOwner &= ownerId == player.NetId;
        _duringEndTurn &= LibrarianRuntime.Get(player).ResolvingEndTurn;
        if (_previousRequired is { } previous && context.LastInvolvedModel is ThreefoldUnity)
            _previousComplete &= Completed.Contains(previous) && previous.Pile?.Type != PileType.Play;
        Contexts.Add(context);
        MainFile.Logger.Info($"FUEL_FREEZE_CHOICE player={player.NetId} owner={ownerId} context={context.GetType().Name} source={context.LastInvolvedModel} endTurn={LibrarianRuntime.Get(player).ResolvingEndTurn}");
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
        int checks = 0, gridChoices = 0, handChoices = 0, realTurns = 0;
        string output = System.Environment.GetEnvironmentVariable("LIBRARIAN_FUEL_FREEZE_OUTPUT")
            ?? throw new InvalidOperationException("Fuel freeze output required");
        Directory.CreateDirectory(output);
        LibrarianSession Session() => LibrarianRuntime.Get(player);
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("fuel freeze: " + label);
            checks++;
            MainFile.Logger.Info("FUEL_FREEZE_CHECK_PASS " + label);
        }
        async Task Frame() => await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
        async Task Wait(Func<bool> ready, string label, int max = 1800)
        {
            for (int frame = 0; !ready(); frame++)
            {
                if (frame >= max) throw new TimeoutException("fuel freeze: " + label);
                await Frame();
            }
        }
        async Task Shot(string name)
        {
            await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
            Check(image.SavePng(Path.Combine(output, name + ".png")) == Error.Ok, "screenshot " + name);
        }
        T Create<T>(bool upgraded = false) where T : CardModel
        {
            var card = player.Creature.CombatState!.CreateCard<T>(player);
            if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
            return card;
        }
        async Task Reset()
        {
            _owner = null;
            await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(3), SceneTreeTimer.SignalName.Timeout);
            await freshFight();
            await Wait(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play, "fresh Play phase");
            foreach (var pile in new[] { PileType.Hand, PileType.Draw, PileType.Discard, PileType.Exhaust })
                foreach (var card in pile.GetPile(player).Cards.ToArray()) await CardPileCmd.RemoveFromCombat(card);
            for (int n = 0; n < 3; n++) await CardPileCmd.Add(Create<LibrarianDefend>(), PileType.Hand);
            for (int n = 0; n < 12; n++) await CardPileCmd.Add(Create<LibrarianDefend>(), PileType.Draw);
            foreach (var kind in Session().Orbs.Positions.ToArray())
                await LibrarianRuntime.Dispatch(Session(), new ThrowingPlayerChoiceContext(),
                    Session().Orbs.Strengthen(kind, (int)kind + 1, OrbScope.All, new("fuel-freeze-fixture")));
            Contexts.Clear(); Completed.Clear(); ThreefoldResults.Clear(); EffectDraws.Clear();
            _requests = _hookEntries = 0;
            _correctOwner = _duringEndTurn = _previousComplete = true;
            _previousRequired = null;
            _owner = player;
        }
        async Task Fuel(bool upgraded, int layers = 1)
        {
            for (int layer = 0; layer < layers; layer++)
            {
                var fuel = Create<FuelTheFire>(upgraded);
                var context = new HookPlayerChoiceContext(fuel, LocalContext.NetId!.Value,
                    player.Creature.CombatState!, GameActionType.Combat);
                await context.AssignTaskAndWaitForPauseOrCompletion(CardCmd.AutoPlay(context, fuel, null));
                await context.WaitForCompletion();
                Check(Completed.Contains(fuel), "Fuel native OnPlay/AfterCardPlayed completed");
            }
            Check(player.Creature.GetPower<FuelTheFirePower>()?.Amount == layers, "Fuel native stack count " + layers);
        }
        async Task Turn(string label, IReadOnlyList<OrbKind> targets, CardModel? handTarget = null)
        {
            int round = player.Creature.CombatState!.RoundNumber, gridIndex = 0;
            CombatManager.Instance.SetReadyToEndTurn(player, false);
            for (int frame = 0; !(player.Creature.CombatState!.RoundNumber > round
                && player.PlayerCombatState!.Phase == PlayerTurnPhase.Play); frame++)
            {
                if (frame >= 2400) throw new TimeoutException("fuel freeze native turn: " + label);
                var screen = Desc(NGame.Instance!).OfType<NChooseACardSelectionScreen>().FirstOrDefault(s => s.IsVisibleInTree());
                var hand = NCombatRoom.Instance?.Ui.Hand;
                if (screen is not null)
                {
                    Check(gridIndex < targets.Count, "no unexpected grid choice " + label);
                    await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);
                    await Shot(label + "-grid-" + gridIndex);
                    var target = targets[gridIndex++];
                    var holder = Desc(screen).OfType<NGridCardHolder>().Single(h => h.CardModel is { } model
                        && LibrarianOrbChoice.Choices.TryGetValue(model, out var choice) && choice.Kind == target);
                    holder.EmitSignal(NCardHolder.SignalName.Pressed, holder);
                    gridChoices++;
                    await Wait(() => !GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree() || !screen.IsVisibleInTree(), "grid closed");
                }
                else if (hand?.IsInCardSelection == true)
                {
                    Check(handTarget is not null, "expected hand choice " + label);
                    await Shot(label + "-hand");
                    var holder = Desc(hand).OfType<NHandCardHolder>().Single(h => ReferenceEquals(h.CardModel, handTarget));
                    holder.EmitSignal(NCardHolder.SignalName.Pressed, holder);
                    hand.GetNode<NConfirmButton>("%SelectModeConfirmButton").EmitSignal(NClickableControl.SignalName.Released,
                        hand.GetNode<NConfirmButton>("%SelectModeConfirmButton"));
                    handChoices++;
                    await Wait(() => !hand.IsInCardSelection, "hand choice completed");
                }
                await Frame();
            }
            Check(gridIndex == targets.Count, "all expected orb choices completed " + label);
            Check(_hookEntries == 1, "one native player EndTurn hook " + label);
            Check(_correctOwner && _duringEndTurn && _previousComplete, "owner/end-turn/sequential result-pile invariant " + label);
            Check(!Session().ResolvingEndTurn && Session().EndTurnBottomSnapshot is null, "bottom stage and end-turn flag released " + label);
            Check(Session().AfterHandCleanup.Count == 0 && !NCombatRoom.Instance!.Ui.Hand.IsInCardSelection,
                "deferred draws and native selection released " + label);
            Check(!Session().Orbs.IsFaulted && PileType.Play.GetPile(player).Cards.Count == 0, "no faulted orb resolution or stranded Play card " + label);
            realTurns++;
        }
        try
        {
            Check(OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase), "isolated validation profile");
            Check(CardSelectCmd.Selector is null, "native synchronized selectors only");
            foreach (bool upgraded in new[] { false, true })
                foreach (var target in new[] { OrbKind.Fire, OrbKind.Tide, OrbKind.Growth })
                {
                    await Reset();
                    await Fuel(upgraded);
                    var unity = Create<ThreefoldUnity>(upgraded);
                    int beforeSettlements = Session().Orbs.SettlementsThisCombat;
                    await CardPileCmd.Add(unity, PileType.Draw, CardPilePosition.Bottom);
                    await Turn($"fuel-unity-{upgraded}-{target}", [target]);
                    Check(_requests == 1 && unity.Pile?.Type == PileType.Exhaust, "Threefold single choice and Exhaust " + target);
                    Check(ThreefoldResults.Single().Card == unity
                        && ThreefoldResults.Single().Settlements == beforeSettlements + 3, "Threefold settles exactly three times before natural stage " + target);
                    Check(ThreefoldResults.Single().Orb == target, "actual settlement matches clicked orb " + target);
                }
            foreach (bool upgraded in new[] { false, true })
            {
                await Reset();
                await Fuel(upgraded, 2);
                var nourish = Create<Nourish>(upgraded);
                var unity = Create<ThreefoldUnity>(upgraded);
                await CardPileCmd.Add(unity, PileType.Hand);
                await CardPileCmd.Add(nourish, PileType.Draw, CardPilePosition.Bottom);
                _previousRequired = nourish;
                await Turn("fuel-two-sequential-" + upgraded, [OrbKind.Tide], unity);
                Check(_requests == 2 && unity.Pile?.Type == PileType.Exhaust && nourish.Pile?.Type == PileType.Discard,
                    "two Fuel layers finish hand selection then selected Threefold");
                Check(EffectDraws.SequenceEqual(new[] { upgraded ? 4m : 3m }), "Nourish deferred native draw occurs exactly once");

                await Reset();
                await Fuel(upgraded);
                var nested = Create<ReadBackward>(upgraded);
                unity = Create<ThreefoldUnity>(upgraded);
                await CardPileCmd.Add(unity, PileType.Draw, CardPilePosition.Bottom);
                await CardPileCmd.Add(nested, PileType.Draw, CardPilePosition.Bottom);
                await Turn("fuel-nested-unity-" + upgraded, [OrbKind.Fire]);
                Check(_requests == 1 && unity.Pile?.Type == PileType.Exhaust && Completed.Contains(nested),
                    "Fuel -> ReadBackward -> Threefold completes without self-queue wait");
            }
            await Reset();
            await Fuel(false);
            var leaf = Create<ThreefoldUnity>();
            var inner = Create<ReadBackward>();
            var outer = Create<ReadBackward>();
            await CardPileCmd.Add(leaf, PileType.Draw, CardPilePosition.Bottom);
            await CardPileCmd.Add(inner, PileType.Draw, CardPilePosition.Bottom);
            await CardPileCmd.Add(outer, PileType.Draw, CardPilePosition.Bottom);
            await Turn("fuel-two-recursive-reads", [OrbKind.Growth]);
            Check(_requests == 1 && leaf.Pile?.Type == PileType.Exhaust && Completed.Contains(inner) && Completed.Contains(outer),
                "two recursive ReadBackward calls share completing choice task");
            Check(CardSelectCmd.Selector is null, "selector bypass remained disabled");
            _owner = null;
            await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(3), SceneTreeTimer.SignalName.Timeout);
            MainFile.Logger.Info($"FUEL_FREEZE_AUDIT_PASS checks={checks} gridChoices={gridChoices} handChoices={handChoices} realTurns={realTurns} native=True liveMulticlient=False");
        }
        finally { _owner = null; }
    }
}
