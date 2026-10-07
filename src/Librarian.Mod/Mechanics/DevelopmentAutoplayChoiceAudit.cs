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
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;

namespace Librarian.Mechanics;

/// <summary>Opt-in regression using real queues, end turns and selection controls. No selector bypass.</summary>
internal static class DevelopmentAutoplayChoiceAudit
{
    private static Player? _owner;
    private static int _requests;
    private static bool _correctOwner;

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromHand))]
    private static class ObserveHand
    {
        private static bool Prepare() => System.Environment.GetEnvironmentVariable("LIBRARIAN_041_FOCUS") == "autoplay-choice"
            && System.Environment.GetEnvironmentVariable("LIBRARIAN_RUNTIME_AUDIT") == "1";
        private static void Prefix(PlayerChoiceContext context, Player player) => Observe(context, player);
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromChooseACardScreen))]
    private static class ObserveGrid
    {
        private static bool Prepare() => System.Environment.GetEnvironmentVariable("LIBRARIAN_041_FOCUS") == "autoplay-choice"
            && System.Environment.GetEnvironmentVariable("LIBRARIAN_RUNTIME_AUDIT") == "1";
        private static void Prefix(PlayerChoiceContext context, Player player) => Observe(context, player);
    }

    private static void Observe(PlayerChoiceContext context, Player player)
    {
        if (_owner is null) return;
        _requests++;
        ulong? ownerId = AccessTools.Property(context.GetType(), "OwnerId")?.GetValue(context) as ulong?
            ?? (context as HookPlayerChoiceContext)?.Owner?.NetId;
        _correctOwner &= ownerId == player.NetId;
        MainFile.Logger.Info($"AUTOPLAY_CHOICE_REQUEST player={player.NetId} context={context.GetType().Name} owner={ownerId} source={context.LastInvolvedModel}");
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
        int checks = 0, handChoices = 0, gridChoices = 0, realTurns = 0;
        string output = System.Environment.GetEnvironmentVariable("LIBRARIAN_AUTOPLAY_CHOICE_OUTPUT")
            ?? throw new InvalidOperationException("Autoplay choice output required");
        Directory.CreateDirectory(output);
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("autoplay choice: " + label);
            checks++; MainFile.Logger.Info("AUTOPLAY_CHOICE_CHECK_PASS " + label);
        }
        async Task Frame() => await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
        async Task Wait(Func<bool> ready, string label, int max = 1800)
        {
            for (int i = 0; !ready(); i++)
            {
                if (i >= max) throw new TimeoutException("autoplay choice: " + label);
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
            // Choice/result-pile previews keep pooled native card nodes alive
            // briefly. Let them finish before replacing the combat room.
            await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(3), SceneTreeTimer.SignalName.Timeout);
            await freshFight();
            await Wait(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play, "fresh Play phase");
            foreach (var pile in new[] { PileType.Hand, PileType.Draw, PileType.Discard, PileType.Exhaust })
                foreach (var card in pile.GetPile(player).Cards.ToArray()) await CardPileCmd.RemoveFromCombat(card);
            _requests = 0; _correctOwner = true; _owner = player;
            for (int n = 0; n < 3; n++) await CardPileCmd.Add(Create<LibrarianDefend>(), PileType.Hand);
            for (int n = 0; n < 8; n++) await CardPileCmd.Add(Create<LibrarianDefend>(), PileType.Draw);
        }
        async Task Drive(Func<bool> complete, string label)
        {
            for (int i = 0; !complete(); i++)
            {
                if (i >= 1800) throw new TimeoutException("autoplay choice controls: " + label);
                var screen = Desc(NGame.Instance!).OfType<NChooseACardSelectionScreen>().FirstOrDefault(s => s.IsVisibleInTree());
                var hand = NCombatRoom.Instance?.Ui.Hand;
                if (screen is not null)
                {
                    await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);
                    await Shot(label + "-grid-" + gridChoices);
                    var holder = Desc(screen).OfType<NGridCardHolder>().First();
                    holder.EmitSignal(NCardHolder.SignalName.Pressed, holder);
                    gridChoices++;
                    await Wait(() => !GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree() || !screen.IsVisibleInTree(), "grid closed");
                }
                else if (hand?.IsInCardSelection == true)
                {
                    await Shot(label + "-hand-" + handChoices);
                    var holder = Desc(hand).OfType<NHandCardHolder>().First(h => h.IsVisibleInTree() && h.CardNode is not null);
                    holder.EmitSignal(NCardHolder.SignalName.Pressed, holder);
                    var confirm = hand.GetNode<NConfirmButton>("%SelectModeConfirmButton");
                    confirm.EmitSignal(NClickableControl.SignalName.Released, confirm);
                    handChoices++;
                    await Wait(() => !hand.IsInCardSelection, "hand choice completed");
                }
                await Frame();
            }
        }
        async Task Turn(string label)
        {
            int round = player.Creature.CombatState!.RoundNumber;
            CombatManager.Instance.SetReadyToEndTurn(player, false);
            await Drive(() => player.Creature.CombatState!.RoundNumber > round && player.PlayerCombatState!.Phase == PlayerTurnPhase.Play, label);
            Check(!LibrarianRuntime.Get(player).ResolvingEndTurn, "end-turn flag cleared " + label);
            Check(!NCombatRoom.Instance!.Ui.Hand.IsInCardSelection, "hand selection released " + label);
            realTurns++;
        }
        try
        {
            Check(OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase), "isolated profile");
            Check(CardSelectCmd.Selector is null, "native synchronized selection without selector");
            var originalHook = new HookPlayerChoiceContext(ModelDb.Singleton<LibrarianCombatHooks>(), LocalContext.NetId!.Value,
                player.Creature.CombatState!, GameActionType.Combat);
            MainFile.Logger.Info("AUTOPLAY_CHOICE_API originalGlobalHookOwner=" + originalHook.Owner?.NetId);
            foreach (bool upgraded in new[] { false, true })
            {
                foreach (bool orbChoice in new[] { false, true })
                {
                    await Reset();
                    var card = orbChoice ? (CardModel)Create<ThreefoldUnity>(upgraded) : Create<Nourish>(upgraded);
                    await CardPileCmd.Add(card, PileType.Draw, CardPilePosition.Bottom);
                    await PowerCmd.Apply<FuelTheFirePower>(new ThrowingPlayerChoiceContext(), player.Creature, 1, player.Creature, null);
                    int beforeHand = handChoices, beforeGrid = gridChoices;
                    await Turn($"fuel-{card.GetType().Name}-{upgraded}");
                    Check(_requests == 1 && _correctOwner, "one selection owned by card player " + card.Id);
                    Check(orbChoice ? gridChoices == beforeGrid + 1 : handChoices == beforeHand + 1, "real selection control used " + card.Id);
                    Check(card.Pile?.Type == (orbChoice ? PileType.Exhaust : PileType.Discard), "autoplay finishes result pile " + card.Id);
                }
            }
            await Reset();
            var first = Create<Nourish>(); var second = Create<ThreefoldUnity>();
            await CardPileCmd.Add(first, PileType.Draw, CardPilePosition.Bottom);
            // The hand choice returns this card to the bottom; the second Fuel layer must play that exact instance.
            foreach (var card in PileType.Hand.GetPile(player).Cards.ToArray()) await CardPileCmd.RemoveFromCombat(card);
            await CardPileCmd.Add(second, PileType.Hand);
            await CardPileCmd.Add(Create<LibrarianDefend>(), PileType.Hand);
            await PowerCmd.Apply<FuelTheFirePower>(new ThrowingPlayerChoiceContext(), player.Creature, 2, player.Creature, null);
            await Turn("fuel-two-sequential");
            Check(_requests == 2 && _correctOwner, "two Fuel layers resume and request separate choices");
            Check(first.Pile?.Type == PileType.Discard && second.Pile?.Type == PileType.Exhaust, "returned hand card played by second layer");
            await Reset();
            var nested = Create<ReadBackward>(); var nourish = Create<Nourish>();
            await CardPileCmd.Add(nourish, PileType.Draw, CardPilePosition.Bottom);
            await CardPileCmd.Add(nested, PileType.Draw, CardPilePosition.Bottom);
            await PowerCmd.Apply<FuelTheFirePower>(new ThrowingPlayerChoiceContext(), player.Creature, 1, player.Creature, null);
            await Turn("fuel-nested-read");
            Check(_requests == 1 && _correctOwner, "nested bottom play retains player context");
            Check(nourish.Pile?.Type == PileType.Discard, "nested selection card completes");
            // Card-origin autoplay must keep its already assigned action context.
            await Reset();
            var direct = Create<ReadBackward>(); var choiceCard = Create<ThreefoldUnity>();
            await CardPileCmd.Add(choiceCard, PileType.Draw, CardPilePosition.Bottom);
            var owned = new HookPlayerChoiceContext(direct, LocalContext.NetId!.Value, player.Creature.CombatState!, GameActionType.Combat);
            var task = CardCmd.AutoPlay(owned, direct, null);
            await owned.AssignTaskAndWaitForPauseOrCompletion(task);
            await Drive(() => task.IsCompleted, "card-origin-read");
            await owned.WaitForCompletion();
            Check(_requests == 1 && _correctOwner, "card-origin context preserved through nested selection");
            Check(choiceCard.Pile?.Type == PileType.Exhaust, "card-origin choice completes");
            await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(3), SceneTreeTimer.SignalName.Timeout);
            MainFile.Logger.Info($"AUTOPLAY_CHOICE_AUDIT_PASS checks={checks} handChoices={handChoices} gridChoices={gridChoices} realTurns={realTurns} native=True liveMulticlient=False");
        }
        finally { _owner = null; }
    }
}
