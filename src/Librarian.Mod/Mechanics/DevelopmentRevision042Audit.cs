using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.OrbAdvancedBasics;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Cards.PowerCards;
using Librarian.LibrarianCode.Cards.Stateful;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;

namespace Librarian.Mechanics;

internal static class DevelopmentRevision042Audit
{
    internal static async Task Run(Player player, Func<Task> freshFight)
    {
        var ctx = new ThrowingPlayerChoiceContext();
        int checks = 0;
        LibrarianSession S() => LibrarianRuntime.Get(player);
        void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException("042: " + name);
            checks++; MainFile.Logger.Info("CARD042_CHECK_PASS " + name);
        }
        async Task Reset()
        {
            await freshFight();
            for (int frame = 0; player.PlayerCombatState?.Phase != PlayerTurnPhase.Play; frame++)
            {
                if (frame > 1200) throw new TimeoutException("042 did not reach native Play phase");
                await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            foreach (var enemy in player.Creature.CombatState!.HittableEnemies)
                await CreatureCmd.SetMaxAndCurrentHp(enemy, 10000);
            foreach (var card in player.PlayerCombatState!.Hand.Cards.ToArray())
                await CardPileCmd.Add(card, PileType.Discard);
        }
        T Create<T>(bool up = false) where T : CardModel
        {
            var card = player.Creature.CombatState!.CreateCard<T>(player);
            if (up) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
            return card;
        }
        Task Play(CardModel card) => CardCmd.AutoPlay(ctx, card, null, skipCardPileVisuals: true);
        Task Dispatch(OrbOperationResult result) => LibrarianRuntime.Dispatch(S(), ctx, result);
        async Task Capture(string name)
        {
            const string dir = @"D:\Slay The Spire_Mod Dev\outputs\revision-v1.0.0-stable\audit-history\revision-v0.4.2\screenshots";
            System.IO.Directory.CreateDirectory(dir);
            await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
            Check(image.SavePng(System.IO.Path.Combine(dir, name + ".png")) == Error.Ok, "capture " + name);
        }
        async Task Start()
        {
            S().Orbs.BeginOwnerTurn();
            await ModelDb.Singleton<LibrarianCombatHooks>().BeforeHandDraw(player, ctx, player.Creature.CombatState!);
        }
        foreach (bool up in new[] { false, true })
        {
            await Reset();
            Check(Create<FuelTheFire>(up).Rarity == CardRarity.Uncommon && Create<ArchiveBulwark>(up).Rarity == CardRarity.Rare, "rarity swap " + up);
            var bulwark = Create<ArchiveBulwark>(up); await Play(bulwark);
            Check(bulwark.Pile?.Type == PileType.Exhaust && bulwark.GetDescriptionForPile(PileType.None).Contains("消耗"), "Bulwark actual exhaust and text " + up);
            var rekindle = Create<Rekindle>(up);
            Check(rekindle.EnergyCost.GetWithModifiers(CostModifiers.None) == 0, "Rekindle zero cost " + up);
            await Play(rekindle);
            Check(S().Orbs.Value(OrbKind.Fire) == (up ? 5 : 3), "Rekindle actual Fire " + up);
            Check(player.PlayerCombatState!.DrawPile.Cards.Last() is Rekindle copy && copy.IsUpgraded == up
                && copy.EnergyCost.GetWithModifiers(CostModifiers.None) == 0, "Rekindle bottom copy cost and upgrade " + up);

            for (int kinds = 0; kinds <= 3; kinds++)
            {
                await Reset();
                foreach (var kind in Enum.GetValues<OrbKind>().Take(kinds))
                { await Dispatch(S().Orbs.Lock(kind)); await Dispatch(S().Orbs.Lock(kind)); }
                for (int i = 0; i < 10; i++) await CardPileCmd.AddGeneratedCardToCombat(Create<LibrarianDefend>(), PileType.Draw, player, CardPilePosition.Top);
                var zero = Create<ZeroSearch>(up); int before = player.PlayerCombatState!.Hand.Cards.Count;
                int hp = player.Creature.CombatState!.HittableEnemies.Sum(e => e.CurrentHp);
                await Play(zero);
                Check(player.PlayerCombatState.Hand.Cards.Count - before == (up ? 4 : 3) + kinds, $"Zero native draw up={up} kinds={kinds}");
                Check(zero.Type == CardType.Skill && hp == player.Creature.CombatState.HittableEnemies.Sum(e => e.CurrentHp), "Zero skill no damage " + up);
            }

            await Reset();
            var spark = Create<ImmortalSpark>(up); await Play(spark);
            Check(spark.EnergyCost.GetWithModifiers(CostModifiers.None) == 1 && spark.Pile?.Type == PileType.Exhaust, "Spark cost and exhaust " + up);
            Check(S().Orbs.Value(OrbKind.Fire) == 1 && spark.PermanentIncrease == (up ? 7 : 5), "Spark gain before permanent increase " + up);
            var loaded = (ImmortalSpark)CardModel.FromSerializable(spark.ToSerializable());
            Check(loaded.PermanentIncrease == (up ? 7 : 5) && loaded.DynamicVars["Fire"].IntValue == (up ? 8 : 6), "Spark saved future value " + up);
            var clone = (ImmortalSpark)spark.CreateClone(); clone.PermanentIncrease += 9;
            Check(spark.PermanentIncrease == (up ? 7 : 5), "Spark clone independent " + up);
            await Play(spark);
            Check(S().Orbs.Value(OrbKind.Fire) == (up ? 9 : 7), "Spark second actual gain " + up);
            if (up) { loaded.DowngradeInternal(); Check(loaded.PermanentIncrease == 7 && loaded.DynamicVars["Increase"].IntValue == 5, "Spark downgrade preserves history"); }

            await Reset(); await Play(Create<Sedimentation>(up));
            var sediment = player.Creature.GetPower<SedimentationPower>()!;
            var background = S().Orbs.Select(OrbScope.Background).ToArray(); var front = S().Orbs.Foreground;
            await Dispatch(S().Orbs.ActivateWithoutSwitch(background[0], OrbScope.All));
            // Activation is in place; foreground remains excluded even when unimbued.
            await Dispatch(S().Orbs.Lock(background[1]));
            var positions = S().Orbs.Positions.ToArray();
            await sediment.BeforeHandDraw(player, ctx, player.Creature.CombatState!);
            Check(S().Orbs.Positions.Sum(S().Orbs.Value) == 0, "Sediment no turn-start effect " + up);
            await sediment.BeforeOrbSettlements(S(), ctx);
            Check(S().Orbs.Value(background[1]) == (up ? 8 : 6) && S().Orbs.Value(background[0]) == 0 && S().Orbs.Value(front) == 0,
                "Sediment only unimbued background including locked " + up);
            Check(positions.SequenceEqual(S().Orbs.Positions) && !S().Orbs.IsActivated(background[1]), "Sediment no move or imbue " + up);
            await Reset(); await Play(Create<Sedimentation>(up));
            await ModelDb.Singleton<LibrarianCombatHooks>().BeforeSideTurnEnd(ctx, MegaCrit.Sts2.Core.Combat.CombatSide.Player, [player.Creature]);
            Check(S().Orbs.Select(OrbScope.Background).All(k => S().Orbs.Value(k) == (up ? 8 : 6)), "Sediment shared end-turn integration " + up);

            await Reset(); await Play(Create<CropRotation>(up));
            var crop = player.Creature.GetPower<CropRotationPower>();
            Check(crop is not null && crop.Amount == (up ? 3 : 2) && ResourceLoader.Exists(crop.CustomPackedIconPath), "Crop native power icon and amount " + up);
            await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(0.7), SceneTreeTimer.SignalName.Timeout);
            var node = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance!.GetCreatureNode(player.Creature)!;
            var powers = node.FindChildren("*", "", true, false).OfType<MegaCrit.Sts2.Core.Nodes.Combat.NPower>();
            var cropNode = powers.Single(p => ReferenceEquals(p.Model, crop));
            Check(cropNode.IsVisibleInTree() && cropNode.GetNode<TextureRect>("%Icon").Texture is not null,
                "Crop actual native status-bar node visible " + up);
            cropNode.EmitSignal(Control.SignalName.MouseEntered);
            await NGame.Instance.ToSignal(NGame.Instance.GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
            await Capture("crop-status-" + up);
            cropNode.EmitSignal(Control.SignalName.MouseExited);
            await Start();
            Check(S().Orbs.Positions.Count(S().Orbs.IsActivated) == 1 && crop!.Amount == (up ? 2 : 1), "Crop activates and decrements " + up);
            foreach (var kind in S().Orbs.Positions.ToArray()) await Dispatch(S().Orbs.Lock(kind));
            for (int i = 0; i < (up ? 2 : 1); i++) await Start();
            Check(player.Creature.GetPower<CropRotationPower>() is null && S().StartTasks.Count == 0, "Crop unavailable targets still expire " + up);
        }
        await Reset(); await Play(Create<CropRotation>()); await Play(Create<CropRotation>(true));
        Check(player.Creature.GetPower<CropRotationPower>()!.Amount == 5 && S().StartTasks.Count == 2, "Crop mixed durations separate");
        await Start(); Check(player.Creature.GetPower<CropRotationPower>()!.Amount == 3, "Crop both chains first start");
        await Start(); Check(player.Creature.GetPower<CropRotationPower>()!.Amount == 1 && S().StartTasks.Count == 1, "Crop base chain expires independently");
        await Start(); Check(player.Creature.GetPower<CropRotationPower>() is null, "Crop upgraded chain expires");
        await Play(Create<CropRotation>()); await PowerCmd.Remove(player.Creature.GetPower<CropRotationPower>()!);
        foreach (var kind in S().Orbs.Positions.ToArray()) await Dispatch(S().Orbs.Extinguish(kind, OrbScope.All));
        await Start(); Check(S().Orbs.Positions.All(k => !S().Orbs.IsActivated(k)) && S().StartTasks.Count == 0, "Crop removed state cannot trigger invisibly");
        await Reset();
        await Play(Create<TidalMark>()); await Play(Create<TidalMark>());
        await Dispatch(S().Orbs.Gain(OrbKind.Tide, 3));
        var tidal = player.Creature.GetPower<TidalMarkPower>()!;
        Check(S().Waves.Amount == 6 && tidal.Amount == 2, "TidalMark stacked actual gain");
        var tooltip = tidal.HoverTips.OfType<MegaCrit.Sts2.Core.HoverTips.HoverTip>().First().Description;
        Check(tooltip.Contains("2") && tooltip.Contains("倍") && !tooltip.Contains('{'), "TidalMark tooltip reflects actual stacks");
        await Play(Create<BlazingChapter>(true));
        var blazing = player.Creature.GetPower<BlazingChapterPower>()!;
        Check(blazing.Amount == 2 && blazing.HoverTips.OfType<MegaCrit.Sts2.Core.HoverTips.HoverTip>().First().Description.Contains("2"), "BlazingChapter upgraded tooltip count");
        Check(!blazing.Description.GetFormattedText().Contains("一次"), "BlazingChapter generic tooltip no fixed count");
        await Reset();
        var layer = new CanvasLayer { Layer = 120 }; NGame.Instance!.AddChild(layer);
        var view = NGame.Instance.GetViewport().GetVisibleRect().Size;
        var panel = new Control(); layer.AddChild(panel);
        panel.AddChild(new ColorRect { Color = new Color("20272e"), Size = view });
        CardModel[] models = [Create<FuelTheFire>(), Create<ArchiveBulwark>(), Create<Sedimentation>(),
            Create<Librarian.LibrarianCode.Cards.OrbBasics.BurnTheRiver>(), Create<ZeroSearch>(), Create<Rekindle>(), Create<ImmortalSpark>(), Create<CropRotation>()];
        try
        {
            for (int page = 0; page < 2; page++)
            {
                var nodes = new List<MegaCrit.Sts2.Core.Nodes.Cards.NCard>();
                for (int i = 0; i < 4; i++)
                {
                    var card = MegaCrit.Sts2.Core.Assets.PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<MegaCrit.Sts2.Core.Nodes.Cards.NCard>();
                    panel.AddChild(card); card.Model = models[page * 4 + i];
                    card.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
                    card.Scale = Vector2.One * Math.Min(view.X / 1360f, view.Y / 530f);
                    card.Position = new Vector2(view.X * (i + 0.5f) / 4, view.Y * 0.5f); nodes.Add(card);
                }
                await Capture("cards-base-" + page);
                foreach (var card in nodes) { card.Model.UpgradeInternal(); card.Model.FinalizeUpgradeInternal(); card.UpdateVisuals(PileType.None, CardPreviewMode.Normal); }
                await Capture("cards-upgraded-" + page);
                foreach (var card in nodes) { panel.RemoveChild(card); card.QueueFree(); }
            }
        }
        finally { layer.QueueFree(); }
        MainFile.Logger.Info($"CARD042_NATIVE_AUDIT_PASS checks={checks}");
    }
}
