using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Cards.OrbAdvancedBasics;
using Librarian.LibrarianCode.Powers;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.TestSupport;

namespace Librarian.Mechanics;

internal static class DevelopmentStatus051Audit
{
    internal static async Task Run(Player player, Func<Task> freshFight)
    {
        int checks = 0;
        var context = new ThrowingPlayerChoiceContext();
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException("051 status: " + label); checks++; MainFile.Logger.Info("STATUS051_CHECK_PASS " + label); }
        async Task Wait(double seconds = 0.2) => await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        LibrarianSession S() => LibrarianRuntime.Get(player);
        async Task Reset()
        {
            await freshFight();
            for (int i = 0; player.PlayerCombatState?.Phase != PlayerTurnPhase.Play; i++)
            { if (i > 1000) throw new TimeoutException("status play phase"); await Wait(0.01); }
            foreach (var e in player.Creature.CombatState!.HittableEnemies) await CreatureCmd.SetMaxAndCurrentHp(e, 10000);
        }
        T Create<T>(bool upgraded = false) where T : CardModel
        { var c = player.Creature.CombatState!.CreateCard<T>(player); if (upgraded) { c.UpgradeInternal(); c.FinalizeUpgradeInternal(); } return c; }
        Task Play(CardModel c) => CardCmd.AutoPlay(context, c, null, skipCardPileVisuals: true);
        Task Dispatch(OrbOperationResult result) => LibrarianRuntime.Dispatch(S(), context, result);
        async Task Capture(string name)
        {
            await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var im = NGame.Instance.GetViewport().GetTexture().GetImage();
            string path = System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT") ?? @"D:\Slay The Spire_Mod Dev\outputs\revision-v0.5.1\screenshots";
            System.IO.Directory.CreateDirectory(path);
            Check(im.SavePng(System.IO.Path.Combine(path, name + ".png")) == Error.Ok, "screenshot " + name);
        }
        async Task Cards(string name, CardModel[] models)
        {
            var layer = new CanvasLayer { Layer = 120 }; NGame.Instance!.AddChild(layer);
            try
            {
                var size = NGame.Instance.GetViewport().GetVisibleRect().Size;
                layer.AddChild(new ColorRect { Size = size, Color = new Color("20272e") });
                for (int i = 0; i < models.Length; i++)
                {
                    var node = MegaCrit.Sts2.Core.Assets.PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>();
                    layer.AddChild(node); node.Model = models[i]; node.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
                    node.Scale = Vector2.One * Math.Min(size.X / (models.Length * 380), size.Y / 600);
                    node.Position = new(size.X * (i + 0.5f) / models.Length, size.Y / 2);
                }
                await Wait(); await Capture(name);
            }
            finally { layer.QueueFree(); }
            await Wait();
        }

        await Reset();
        var powers = ModelDb.AllPowers.OfType<LibrarianPower>().ToArray();
        Check(powers.Length == 27, "all 27 custom power models");
        Check(powers.Count(p => p.NativeIconName is not null) == 11, "11 native icon routes");
        foreach (var canonical in powers)
        {
            Check(canonical.Icon is not null && canonical.BigIcon is not null, "native loader small and big icons " + canonical.Id);
            Check(canonical.Icon.ResourcePath == canonical.CustomIconPath, "actual packed icon route " + canonical.Id);
            Check(canonical.ResolvedBigIconPath == canonical.CustomBigIconPath, "actual big icon route " + canonical.Id);
            var p = canonical.ToMutable();
            await PowerCmd.Apply(context, p, player.Creature, 2, player.Creature, null);
            foreach (string lang in new[] { "zhs", "eng" })
            {
                LocManager.Instance.SetLanguage(lang);
                string text = p.HoverTips.OfType<HoverTip>().First().Description;
                Check(!string.IsNullOrWhiteSpace(text) && !text.Contains('{') && !text.Contains('}'), "resolved combat tooltip " + canonical.Id + " " + lang);
            }
            LocManager.Instance.SetLanguage("zhs");
            await PowerCmd.Remove(p);
        }
        Check(ModelDb.Power<SeedburialPendingPower>().CustomIconPath.Contains("/power.png") &&
            ModelDb.Power<DeepSeaPendingPower>().CustomIconPath.Contains("/power.png"), "user retained both ambiguous icons");

        foreach (bool up in new[] { false, true })
        {
            await Reset();
            // Keep native hand capacity out of the draw-count assertion.
            foreach (var c in player.PlayerCombatState!.Hand.Cards.ToArray()) await CardPileCmd.Add(c, PileType.Discard);
            var zero = Create<ZeroSearch>(up);
            Check(zero.GetDescriptionForPile(PileType.Hand).Contains("当前额外抽[blue]0[/blue]"), "zero initial ledger preview " + up);
            await Dispatch(S().Orbs.Lock(OrbKind.Fire));
            await Dispatch(S().Orbs.Lock(OrbKind.Tide));
            Check(zero.GetDescriptionForPile(PileType.Hand).Contains("当前额外抽[blue]2[/blue]"), "zero updated ledger preview " + up);
            await Cards(up ? "051-zero-upgraded" : "051-zero-base", [zero]);
            int hand = player.PlayerCombatState!.Hand.Cards.Count;
            await Play(zero);
            Check(player.PlayerCombatState.Hand.Cards.Count - hand == (up ? 6 : 5), "zero actual draw matches base plus ledger " + up);
            Check(!ModelDb.Card<ZeroSearch>().GetDescriptionForPile(PileType.None).Contains("当前额外"), "zero canonical encyclopedia hides combat preview");
        }
        await Reset();
        await Dispatch(S().Orbs.Strengthen(OrbKind.Fire, 20, OrbScope.All));
        await Dispatch(S().Orbs.Lose(OrbKind.Fire, 7, OrbScope.All));
        var reread = Create<ReRead>();
        var previews = LibrarianOrbChoice.CreatePreviews(reread, S().Orbs.Positions.ToArray());
        var fire = previews.Single(c => LibrarianOrbChoice.Choices.GetValue(c, _ => throw new InvalidOperationException()).Kind == OrbKind.Fire);
        var info = LibrarianOrbChoice.Choices.GetValue(fire, _ => throw new InvalidOperationException());
        Check(info.Current == 13 && info.Highest == 20 && fire.GetDescriptionForPile(PileType.None).Contains("[blue]7[/blue]"), "reread current maximum and restore preview");
        await Cards("051-reread-previews", previews);
        var selector = new TestCardSelector(); selector.PrepareToSelect([Array.IndexOf(S().Orbs.Positions.ToArray(), OrbKind.Fire)]);
        using (CardSelectCmd.PushSelector(selector)) await Play(reread);
        Check(S().Orbs.Value(OrbKind.Fire) == 20, "reread selected result matches preview");
        var other = LibrarianOrbChoice.CreatePreviews(Create<ThreefoldUnity>(), S().Orbs.Positions.ToArray());
        Check(other.All(c => c.GetDescriptionForPile(PileType.None).Contains("选择这个法球")), "other choice descriptions unchanged");

        await Reset();
        await Play(Create<CropRotation>()); await Play(Create<CropRotation>(true));
        var crop = player.Creature.GetPower<CropRotationPower>()!;
        Check(crop.Amount == 5 && ((StringVar)crop.DynamicVars["Durations"]).StringValue == "2、3", "independent mixed durations displayed");
        await Wait(0.5);
        var nodePower = NCombatRoom.Instance!.GetCreatureNode(player.Creature)!.FindChildren("*", "", true, false).OfType<NPower>().Single(p => p.Model == crop);
        nodePower.EmitSignal(Control.SignalName.MouseEntered); await Wait(); await Capture("051-crop-tooltip"); nodePower.EmitSignal(Control.SignalName.MouseExited);
        foreach (var kind in S().Orbs.Positions.ToArray()) await Dispatch(S().Orbs.Lock(kind));
        async Task Tick()
        {
            var tasks = S().StartTasks.ToArray(); S().StartTasks.Clear();
            foreach (var task in tasks) await task.Action(context);
        }
        await Tick();
        Check(crop.Amount == 3 && ((StringVar)crop.DynamicVars["Durations"]).StringValue == "1、2", "no target still consumes independent durations");
        await Tick();
        Check(crop.Amount == 1 && ((StringVar)crop.DynamicVars["Durations"]).StringValue == "1", "expired cast removed from display");
        await Tick();
        Check(player.Creature.GetPower<CropRotationPower>() is null && S().StartTasks.Count == 0, "final expiry removes status and tasks");
        await Play(Create<CropRotation>());
        await PowerCmd.Remove(player.Creature.GetPower<CropRotationPower>()!);
        await Tick();
        Check(S().StartTasks.Count == 0, "removed power cannot reschedule invisible effects");
        MainFile.Logger.Info($"STATUS051_NATIVE_AUDIT_PASS checks={checks}");
        await freshFight();
    }
}
