using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.OrbBasics;
using Librarian.LibrarianCode.Cards.PowerCards;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;

namespace Librarian.Mechanics;

/// <summary>Opt-in native text, preview and movable-lock checks in either isolated release channel.</summary>
internal static class DevelopmentTextRevision111Audit
{
    internal static async Task Run(Player player, Func<Task> freshFight)
    {
        int checks = 0, phases = 0, screenshots = 0, realTurns = 0;
        var formattedCards = new List<object>();
        var extraSettlementPhases = new Dictionary<string, bool>();
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("111 text: " + label);
            checks++; MainFile.Logger.Info("V111_TEXT_CHECK_PASS " + label);
        }
        Check(OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase), "isolated validation profile");
        string output = System.Environment.GetEnvironmentVariable("LIBRARIAN_TEXT_AUDIT_OUTPUT")
            ?? throw new InvalidOperationException("LIBRARIAN_TEXT_AUDIT_OUTPUT is required");
        Check(Path.IsPathFullyQualified(output), "absolute channel output path");
        Directory.CreateDirectory(output);
        string originalLanguage = LibrarianLanguage.Selected;
        var ctx = new ThrowingPlayerChoiceContext();
        LibrarianSession S() => LibrarianRuntime.Get(player);
        Task Dispatch(OrbOperationResult op) => LibrarianRuntime.Dispatch(S(), ctx, op);
        async Task Reset()
        {
            await freshFight();
            for (int i = 0; player.PlayerCombatState?.Phase != PlayerTurnPhase.Play; i++)
            {
                if (i > 1200) throw new TimeoutException("111 text native Play phase");
                await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            await CreatureCmd.SetMaxAndCurrentHp(player.Creature, 10000);
            foreach (var enemy in player.Creature.CombatState!.HittableEnemies)
                await CreatureCmd.SetMaxAndCurrentHp(enemy, 10000);
        }
        T Create<T>(bool upgraded = false) where T : CardModel
        {
            var card = player.Creature.CombatState!.CreateCard<T>(player);
            if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
            return card;
        }
        Task Play(CardModel card) => CardCmd.AutoPlay(ctx, card, null, skipCardPileVisuals: true);
        async Task Turn()
        {
            int round = player.Creature.CombatState!.RoundNumber;
            CombatManager.Instance.SetReadyToEndTurn(player, false);
            for (int i = 0; player.Creature.CombatState!.RoundNumber == round || player.PlayerCombatState!.Phase != PlayerTurnPhase.Play; i++)
            {
                if (i > 3600) throw new TimeoutException("111 text native end turn");
                await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            realTurns++;
        }
        bool Has(IEnumerable<IHoverTip> tips, string key) => tips.Any(t => t.Id == LibrarianHoverTips.Tip(key).Id);
        try
        {
            var models = ModelDb.CardPool<LibrarianCardPool>().AllCards.ToArray();
            Check(models.Length == 91, "91 active cards");
            foreach (string lang in new[] { "zhs", "eng" })
            {
                LibrarianLanguage.Select(lang);
                foreach (var canonical in models)
                {
                    var card = canonical.ToMutable(); card.Owner = player;
                    foreach (bool upgraded in new[] { false, true })
                    {
                        if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
                        string text = card.GetDescriptionForPile(PileType.None);
                        var tips = card.HoverTips.ToArray();
                        string label = $"{lang} {card.Id.Entry} upgraded={upgraded}";
                        Check(!text.Contains('{') && !text.Contains('}'), "formatted card " + label);
                        Check(Regex.Matches(text, @"\[gold\]").Count == Regex.Matches(text, @"\[/gold\]").Count, "balanced gold " + label);
                        Check(IHoverTip.RemoveDupes(tips).SequenceEqual(tips), "native dedupe " + label);
                        Check(tips.OfType<HoverTip>().All(t => !t.Description.Contains('{') && !t.Description.Contains('}')), "formatted tips " + label);
                        Check(!Has(tips, "HAND") && !Has(tips, "DRAW_PILE"), "simple-term tips absent " + label);
                        Check(!Has(tips, "STRENGTHEN"), "retired Strengthen tip absent " + label);
                        Check(!text.Contains("强化", StringComparison.Ordinal) && !Regex.IsMatch(text, @"\bstrengthen\w*\b", RegexOptions.IgnoreCase), "plain value vocabulary " + label);
                        Check(LibrarianHoverTips.ForText(text).All(t => tips.Any(actual => actual.Id == t.Id)), "description references present " + label);
                        string phaseId = card.Id.Entry + "/" + upgraded;
                        bool hasExtra = Has(LibrarianHoverTips.ForText(text), "EXTRA_SETTLE");
                        if (lang == "zhs") extraSettlementPhases[phaseId] = hasExtra;
                        else Check(hasExtra == extraSettlementPhases[phaseId], "bilingual extra settlement meaning " + label);
                        formattedCards.Add(new { language = lang, id = card.Id.Entry, upgraded, text, tips = tips.Select(t => t.Id).ToArray() });
                        phases++;
                    }
                }
                // Generic Power tips receive Amount/icons, not live instance variables.
                foreach (var power in ModelDb.AllPowers.OfType<Librarian.LibrarianCode.Powers.LibrarianPower>())
                {
                    var tip = power.GetDumbHoverTip();
                    Check(!tip.Description.Contains('{') && !tip.Description.Contains('}'), "generic power formatted " + lang + " " + power.Id);
                }
                string[] nameSamples = lang == "zhs"
                    ? ["[gold]烈焰[/gold]法球", "[gold]波涛[/gold]法球", "[gold]翠叶[/gold]法球", "[gold]烈焰法球[/gold]", "[gold]波涛法球[/gold]", "[gold]翠叶法球[/gold]"]
                    : ["[gold]Fire[/gold] Orb", "[gold]Tide[/gold] Orb", "[gold]Growth[/gold] Orb", "[gold]Fire Orb[/gold]", "[gold]Tide Orb[/gold]", "[gold]Growth Orb[/gold]",
                       "[gold]Fire[/gold] Orbs", "[gold]Tide[/gold] Orbs", "[gold]Growth[/gold] Orbs",
                       "[gold]Fire[/gold] and [gold]Tide[/gold] Orbs", "[gold]Fire[/gold], [gold]Tide[/gold] and [gold]Growth[/gold] Orbs"];
                foreach (string sample in nameSamples)
                {
                    var tips = LibrarianHoverTips.ForText(sample).ToArray();
                    Check(!Has(tips, "FIRE") && !Has(tips, "TIDE") && !Has(tips, "GROWTH"), "orb-name-only gain tips absent " + lang + " " + sample);
                }
                foreach (bool up in new[] { false, true })
                {
                    var trickle = Create<Trickle>(up);
                    foreach (string key in new[] { "TIDE", "ACTIVATE" })
                        Check(Has(trickle.HoverTips, key), "Trickle reference chain " + lang + " " + up + " " + key);
                    var symphony = models.Single(c => c.Id.Entry == "LIBRARIAN-LIFE_SYMPHONY").ToMutable(); symphony.Owner = player;
                    if (up) { symphony.UpgradeInternal(); symphony.FinalizeUpgradeInternal(); }
                    Check(Has(symphony.HoverTips, "EXTRA_SETTLE"), "Life Symphony extra settlement reference " + lang + " " + up);
                    foreach (string key in new[] { "SETTLE", "FOREGROUND", "EXTINGUISH" })
                        Check(Has(symphony.HoverTips, key), "Life Symphony extra settlement nested reference " + lang + " " + up + " " + key);
                }
                foreach (var source in new CardModel[] { Create<Trickle>(), Create<ReRead>() })
                {
                    var previews = LibrarianOrbChoice.CreatePreviews(source, S().Orbs.Positions.ToArray());
                    Check(previews.Length == 3, "three choice previews " + lang + " " + source.Id);
                    foreach (var preview in previews)
                    {
                        Check(preview.EnergyCost.GetWithModifiers(CostModifiers.None) == -1 && !preview.EnergyCost.CostsX,
                            "negative non-X preview cost " + lang + " " + source.Id + " " + preview.Title);
                        Check(LibrarianOrbChoice.Choices.TryGetValue(preview, out var choice) && choice.RestorePreview == (source is ReRead),
                            "preview identity/restoration " + lang + " " + source.Id + " " + preview.Title);
                        string text = preview.GetDescriptionForPile(PileType.None);
                        Check(!text.Contains('{') && !text.Contains('}'), "formatted choice " + lang + " " + source.Id + " " + preview.Title);
                    }
                }
                foreach (bool up in new[] { false, true })
                {
                    Check(Create<Spark>(up).EnergyCost.GetWithModifiers(CostModifiers.None) == (up ? 0 : 1), "real Spark cost unchanged " + up);
                    Check(Create<Trickle>(up).EnergyCost.GetWithModifiers(CostModifiers.None) == 1, "real Trickle cost unchanged " + up);
                    Check(Create<Renewal>(up).EnergyCost.GetWithModifiers(CostModifiers.None) == 1, "real Renewal cost unchanged " + up);
                }
            }
            Check(phases == 364, "all bilingual base/upgraded card phases");
            File.WriteAllText(Path.Combine(output, "formatted-card-phases.json"),
                JsonSerializer.Serialize(formattedCards, new JsonSerializerOptions { WriteIndented = true }));

            // These are the engine's HoverTip values and real instanced PowerModel types.
            // Compare the expansion with the native algorithm, rather than imposing a stricter ID-only dedupe.
            var emptyOne = new HoverTip(ModelDb.Power<StrengthPower>(), "empty-one", false) { Id = "" };
            var emptyTwo = new HoverTip(ModelDb.Power<StrengthPower>(), "empty-two", false) { Id = "" };
            var instanceOne = new HoverTip(ModelDb.Power<AutomationPower>().ToMutable(), "instance-one", true);
            var instanceTwo = new HoverTip(ModelDb.Power<AutomationPower>().ToMutable(), "instance-two", true);
            var plain = new HoverTip(ModelDb.Power<StrengthPower>(), "plain", false);
            var smart = new HoverTip(ModelDb.Power<StrengthPower>().ToMutable(), "smart", true);
            IHoverTip[] roots = [emptyOne, emptyTwo, instanceOne, instanceTwo, plain, smart, plain];
            var native = IHoverTip.RemoveDupes(roots).ToArray();
            var expanded = LibrarianHoverTips.Expand(roots).ToArray();
            Check(instanceOne.IsInstanced && instanceTwo.IsInstanced, "real native instanced power tips");
            Check(native.SequenceEqual(expanded), "expansion preserves native empty-ID/instanced/smart semantics");
            Check(expanded.Count(t => string.IsNullOrEmpty(t.Id)) == 2, "two empty-ID native tips retained");
            Check(expanded.Count(t => t.Id == instanceOne.Id) == 2, "two real native instanced tips retained");
            Check(expanded.Single(t => t.Id == smart.Id).IsSmart, "native smart tip preferred");

            foreach (var relic in player.Relics.ToArray()) await RelicCmd.Remove(relic);
            await Reset();
            await Play(Create<Renewal>());
            Check(S().Orbs.IsLocked(OrbKind.Fire), "native Renewal locks Fire");
            await Play(Create<Spark>());
            Check(S().Orbs.Foreground == OrbKind.Fire && S().Orbs.Value(OrbKind.Fire) == 5 && !S().Orbs.IsActivated(OrbKind.Fire),
                "native locked Spark gain moves Fire forward without activation");
            await Play(Create<Trickle>());
            Check(S().Orbs.Positions.SequenceEqual(new[] { OrbKind.Tide, OrbKind.Fire, OrbKind.Growth }),
                "native Trickle pushes locked Fire backward");
            await Dispatch(S().Orbs.SwapPositions(OrbKind.Fire, OrbKind.Tide));
            Check(S().Orbs.Foreground == OrbKind.Fire && S().Orbs.IsLocked(OrbKind.Fire), "locked Fire can explicitly swap forward");
            var activation = S().Orbs.Activate(OrbKind.Fire, OrbScope.All);
            await Dispatch(activation);
            Check(activation.Status == OrbOperationStatus.Blocked && !S().Orbs.IsActivated(OrbKind.Fire), "moved lock still blocks activation");
            var noSwitch = S().Orbs.ActivateWithoutSwitch(OrbKind.Fire);
            await Dispatch(noSwitch);
            Check(noSwitch.Status == OrbOperationStatus.Blocked, "moved lock still blocks activation without movement");
            int immediate = await S().Orbs.SettleImmediatelyAsync(OrbSelector.Named(OrbKind.Fire, OrbScope.All), 2,
                r => LibrarianRuntime.Settle(S(), ctx, r), "111-text-locked-immediate");
            Check(immediate == 0, "moved lock blocks real runtime immediate settlement");
            S().Orbs.QueueExtraSettlement(OrbSelector.Named(OrbKind.Fire, OrbScope.All), 2, "111-text-locked-extra");
            var enemy = player.Creature.CombatState!.HittableEnemies.Single();
            int hp = enemy.CurrentHp;
            await Turn();
            Check(enemy.CurrentHp == hp && S().Orbs.SettlementsThisCombat == 2, "native turn skips locked Fire natural/extra; only Tide and Growth settle");
            Check(!S().Orbs.IsLocked(OrbKind.Fire) && !S().Orbs.IsActivated(OrbKind.Fire), "finite lock expires on native owner turn, stays inactive");

            await Reset();
            await Play(Create<Overfishing>());
            Check(S().Orbs.SwitchLocked && S().Orbs.Foreground == OrbKind.Fire, "native Overfishing retains independent front lock");
            await Dispatch(S().Orbs.Lock(OrbKind.Tide));
            await Play(Create<Renewal>());
            await Play(Create<Trickle>());
            Check(S().Orbs.Positions.SequenceEqual(new[] { OrbKind.Fire, OrbKind.Tide, OrbKind.Growth }) && !S().Orbs.IsActivated(OrbKind.Tide),
                "native locked Tide gain reorders backgrounds under SwitchLocked");
            var frontSwap = S().Orbs.SwapPositions(OrbKind.Fire, OrbKind.Tide);
            await Dispatch(frontSwap);
            Check(frontSwap.Status == OrbOperationStatus.Blocked, "SwitchLocked still rejects front swaps");
            var backgroundSwap = S().Orbs.SwapPositions(OrbKind.Tide, OrbKind.Growth);
            await Dispatch(backgroundSwap);
            Check(backgroundSwap.Status == OrbOperationStatus.Applied && S().Orbs.Foreground == OrbKind.Fire, "SwitchLocked allows locked-background swaps");
            await Turn();
            Check(S().Orbs.SwitchLocked && S().Orbs.Foreground == OrbKind.Fire && S().Orbs.IsLocked(OrbKind.Tide) && !S().Orbs.IsActivated(OrbKind.Tide),
                "native owner turn preserves independent front lock and permanent orb lock");

            await Reset();
            screenshots = await Capture(player, models, output, Check);
            if (System.Environment.GetEnvironmentVariable("LIBRARIAN_BETA6_AUDIT") == "1"
                || System.Environment.GetEnvironmentVariable("LIBRARIAN_ORB_VALUE_TEXT_AUDIT") == "1")
                screenshots += await DevelopmentHoverBeta6Audit.Run(output, Check);
            File.WriteAllText(Path.Combine(output, "native-text-audit.json"),
                JsonSerializer.Serialize(new { cards = 91, phases, checks, screenshots, realTurns, native = true,
                    lockPositionChanged = true, liveMulticlient = false }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { LibrarianLanguage.Select(originalLanguage); }
        MainFile.Logger.Info($"V111_TEXT_AUDIT_PASS cards=91 phases={phases} checks={checks} screenshots={screenshots} realTurns={realTurns} native=True liveMulticlient=False");
    }

    private static IEnumerable<Node> Desc(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            yield return child;
            foreach (var descendant in Desc(child)) yield return descendant;
        }
    }

    private static async Task<int> Capture(Player player, CardModel[] models, string output, Action<bool, string> check)
    {
        check(DisplayServer.GetName() != "headless", "native screenshot renderer");
        int screenshots = 0;
        var originalSize = NGame.Instance!.GetWindow().Size;
        var layer = new CanvasLayer { Layer = 120 }; NGame.Instance.AddChild(layer);
        var panel = new Control(); layer.AddChild(panel);
        var background = new ColorRect { Color = new Color("20272e") }; panel.AddChild(background);
        string[] ids = ["TRICKLE", "LIFELINE", "EARTH_COLLAPSE", "EMBER_RECKONING", "ANCIENT_CATALOG"];
        var nodes = new List<NCard>();
        foreach (string id in ids)
        {
            var card = models.Single(c => c.Id.Entry == "LIBRARIAN-" + id).ToMutable(); card.Owner = player;
            var node = MegaCrit.Sts2.Core.Assets.PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>();
            panel.AddChild(node); node.Model = card; nodes.Add(node);
        }
        async Task Wait(double seconds = .25) => await NGame.Instance.ToSignal(NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        async Task Shot(string name)
        {
            await NGame.Instance.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
            check(image.SavePng(Path.Combine(output, name + ".png")) == Error.Ok, "capture " + name);
            screenshots++;
        }
        async Task Tips(IEnumerable<IHoverTip> tips, Vector2 size, string name)
        {
            var owner = new Control { Position = new(size.X * .32f, size.Y * .15f), Size = new(20, 20) }; panel.AddChild(owner);
            var set = NHoverTipSet.CreateAndShow(owner, tips, HoverTipAlignment.Right);
            check(set is not null, "native hover set " + name);
            set!.GetParent().RemoveChild(set); panel.AddChild(set);
            try { await Wait(); await Shot(name); }
            finally { NHoverTipSet.Remove(owner); owner.QueueFree(); }
        }
        try
        {
            foreach (var resolution in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                DisplayServer.WindowSetSize(resolution); await Wait();
                var size = NGame.Instance.GetViewport().GetVisibleRect().Size; background.Size = size;
                foreach (string lang in new[] { "zhs", "eng" })
                {
                    LibrarianLanguage.Select(lang);
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        nodes[i].Visible = true;
                        nodes[i].UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
                        nodes[i].Scale = Vector2.One * Math.Min(size.X / 1500, size.Y / 1100);
                        nodes[i].Position = i < 3 ? new(size.X * (i + .5f) / 3, size.Y * .28f)
                            : new(size.X * (i - 2.5f) / 2, size.Y * .74f);
                    }
                    await Wait(); await Shot($"cards-{lang}-{resolution.X}");
                    foreach (var node in nodes) node.Visible = false;
                    if (resolution.X == 1280)
                    {
                        nodes[0].Visible = true; nodes[0].Position = new(size.X * .15f, size.Y / 2);
                        await Tips(nodes[0].Model.HoverTips, size, $"trickle-chain-{lang}-{resolution.X}");
                        nodes[0].Visible = false;
                    }
                    else
                    {
                        var roots = new List<IHoverTip>();
                        var orbState = LibrarianRuntime.Get(player).Orbs;
                        foreach (var kind in orbState.Positions)
                        {
                            roots.Add(LibrarianOrbDisplay.BuildHoverTip(new OrbView(kind, kind == OrbKind.Fire ? 84 : 106,
                                true, kind == orbState.Foreground)));
                        }
                        roots.Add(LibrarianHoverTips.Tip("WAVES"));
                        await Tips(LibrarianHoverTips.Expand(roots), size, $"orbs-waves-{lang}-{resolution.X}");
                    }
                    // Use the engine's real three-card choice screen; selecting a preview here is UI-only.
                    // It does not claim that a ReRead or other gameplay card was played.
                    panel.Visible = false;
                    var source = models.Single(c => c.Id.Entry == (resolution.X == 1280 ? "LIBRARIAN-TRICKLE" : "LIBRARIAN-RE_READ")).ToMutable();
                    source.Owner = player;
                    var previews = LibrarianOrbChoice.CreatePreviews(source, LibrarianRuntime.Get(player).Orbs.Positions.ToArray());
                    var screen = NChooseACardSelectionScreen.ShowScreen(previews, false);
                    check(screen is not null, "native negative-cost choice screen " + lang + " " + resolution.X);
                    try
                    {
                        var selected = screen!.CardsSelected();
                        await Wait(1);
                        check(Desc(screen!).OfType<NCard>().Count() == 3, "three visible native choice cards");
                        await Shot($"choice-{lang}-{resolution.X}-" + (resolution.X == 1280 ? "orbs" : "reread"));
                        var holder = Desc(screen!).OfType<NGridCardHolder>().First();
                        holder.EmitSignal(NCardHolder.SignalName.Pressed, holder);
                        for (int i = 0; !selected.IsCompleted; i++)
                        {
                            if (i > 1200) throw new TimeoutException("111 text native choice completion");
                            await NGame.Instance.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
                        }
                        check((await selected).Count() == 1, "native choice interaction completes");
                    }
                    finally
                    {
                        if (screen is not null && GodotObject.IsInstanceValid(screen) && screen.GetParent() is not null)
                            NOverlayStack.Instance!.Remove(screen);
                        panel.Visible = true;
                    }
                }
            }
        }
        finally { layer.QueueFree(); DisplayServer.WindowSetSize(originalSize); await Wait(); }
        check(screenshots == 12, "12 bilingual native screenshots");
        return screenshots;
    }
}
