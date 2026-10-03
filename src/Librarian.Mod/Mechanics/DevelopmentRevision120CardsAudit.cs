using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.NativeBatch;
using Librarian.LibrarianCode.Cards.OrbAdvancedBasics;
using Librarian.LibrarianCode.Cards.OrbBasics;
using Librarian.LibrarianCode.Cards.OrbUtility;
using Librarian.LibrarianCode.Cards.Stateful;
using Librarian.LibrarianCode.Powers.Implemented;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace Librarian.Mechanics;

/// <summary>Opt-in first card-batch checks against native play, turn, formatter and save APIs.</summary>
internal static class DevelopmentRevision120CardsAudit
{
    private static readonly string[] Changed = ["TIDAL_GRAVITY", "SEEDBURIAL_STRIKE", "UNRETREATING_TIDE", "AFFORESTATION",
        "BUILD_CANAL", "COMBAT_NOTES", "RENEWAL", "IGNITE", "TIDAL_EROSION", "EMBER_RECKONING", "FIRE_INSCRIPTION",
        "STEAM_BLAST", "SPROUTING_BULWARK", "EVAPORATION", "OPENING_TIDE", "BURN_THE_RIVER", "SPROUTING_SEED",
        "RING_CURRICULUM", "SEDIMENTATION", "BOOK_BURNING", "READ_WIDELY", "IMMORTAL_SPARK", "DRAW_BRANCH",
        "LIFE_SYMPHONY", "ZERO_SEARCH", "TRANSCRIBE", "DEEP_SEA_BARRIER"];

    internal static async Task Run(Player player, Func<Task> freshFight)
    {
        int checks = 0, realTurns = 0;
        var ctx = new ThrowingPlayerChoiceContext();
        string output = System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT")
            ?? throw new InvalidOperationException("LIBRARIAN_AUDIT_OUTPUT is required");
        if (!Path.IsPathFullyQualified(output)) throw new InvalidOperationException("Absolute audit output required");
        Directory.CreateDirectory(output);
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("120 cards: " + label);
            checks++; MainFile.Logger.Info("CARD120_CHECK_PASS " + label);
        }
        LibrarianSession S() => LibrarianRuntime.Get(player);
        Creature Enemy() => player.Creature.CombatState!.HittableEnemies.First();
        async Task Reset()
        {
            await freshFight();
            for (int i = 0; player.PlayerCombatState?.Phase != PlayerTurnPhase.Play; i++)
            {
                if (i > 1200) throw new TimeoutException("120 native Play phase");
                await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            foreach (var relic in player.Relics.ToArray()) await RelicCmd.Remove(relic);
        }
        T Card<T>(bool up = false) where T : CardModel
        {
            var card = player.Creature.CombatState!.CreateCard<T>(player);
            if (up) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
            return card;
        }
        Task Play(CardModel card, Creature? enemy = null) => CardCmd.AutoPlay(ctx, card, enemy, skipCardPileVisuals: true);
        Task Dispatch(OrbOperationResult operation) => LibrarianRuntime.Dispatch(S(), ctx, operation);
        async Task Turn()
        {
            int round = player.Creature.CombatState!.RoundNumber;
            CombatManager.Instance.SetReadyToEndTurn(player, false);
            for (int i = 0; player.Creature.CombatState!.RoundNumber == round || player.PlayerCombatState!.Phase != PlayerTurnPhase.Play; i++)
            {
                if (i > 3600) throw new TimeoutException("120 native end turn");
                await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            realTurns++;
        }

        await Reset();
        var formatted = new List<object>();
        var models = ModelDb.CardPool<LibrarianCardPool>().AllCards.ToArray();
        Check(models.Length == 91, "91 active models");
        string originalLanguage = LibrarianLanguage.Selected;
        try
        {
            foreach (string language in new[] { "zhs", "eng" })
            {
                LibrarianLanguage.Select(language);
                foreach (var model in models)
                {
                    var card = model.ToMutable(); card.Owner = player;
                    foreach (bool upgraded in new[] { false, true })
                    {
                        if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
                        string description = card.GetDescriptionForPile(PileType.None);
                        Check(!description.Contains('{') && !description.Contains('}'), "formatted " + language + " " + card.Id + " " + upgraded);
                        Check(Regex.Matches(description, @"\[gold\]").Count == Regex.Matches(description, @"\[/gold\]").Count,
                            "balanced gold " + language + " " + card.Id + " " + upgraded);
                        var tips = card.HoverTips.ToArray();
                        Check(IHoverTip.RemoveDupes(tips).SequenceEqual(tips), "native hover dedupe " + language + " " + card.Id + " " + upgraded);
                        formatted.Add(new { id = card.Id.Entry, language, upgraded, description });
                        if (Changed.Contains(card.Id.Entry["LIBRARIAN-".Length..]) && language == "zhs")
                        {
                            string plain = Regex.Replace(description, @"\[[^\]]*\]", "");
                            if (card is Transcribe) Check(plain.Contains("将2张复制品分别加入手牌和抽牌堆底部。"), "user Transcribe wording " + upgraded);
                            if (card is ReadWidely) Check(plain.IndexOf("生长", StringComparison.Ordinal) < plain.IndexOf("潮涌", StringComparison.Ordinal)
                                && plain.IndexOf("潮涌", StringComparison.Ordinal) < plain.IndexOf("燃火", StringComparison.Ordinal), "1/2/3/4 text order " + upgraded);
                        }
                    }
                }
            }
            File.WriteAllText(Path.Combine(output, "formatted-cards.json"), JsonSerializer.Serialize(formatted, new JsonSerializerOptions { WriteIndented = true }));

            foreach (bool up in new[] { false, true })
            {
                await Reset(); var gravity = Card<TidalGravity>(up); await Play(gravity);
                Check(S().Orbs.LockedTurns(OrbKind.Tide) == 2, "Gravity locks two turns " + up);
                Check(gravity.Pile?.Type == (up ? PileType.Discard : PileType.Exhaust), "Gravity upgrade retains exhaust removal " + up);

                await Reset(); await PowerCmd.Apply<ThornsPower>(ctx, Enemy(), 10, Enemy(), null);
                int hp = player.Creature.CurrentHp; await Play(Card<CombatNotes>(up));
                Check(hp - player.Creature.CurrentHp == Math.Max(10 - (up ? 10 : 8), 0), "CombatNotes block before native Thorns " + up);

                await Reset(); var positions = S().Orbs.Positions.ToArray(); int energy = player.PlayerCombatState!.Energy;
                await Play(Card<BuildCanal>(up));
                Check(S().Waves.Amount == 4 && S().Orbs.Value(OrbKind.Tide) == 0 && positions.SequenceEqual(S().Orbs.Positions)
                    && !S().Orbs.IsActivated(OrbKind.Tide), "BuildCanal gains Waves without orb channel " + up);
                Check(player.PlayerCombatState.Energy == energy + 2, "BuildCanal retains energy " + up);
                await Play(Card<OpeningTide>(up));
                Check(S().Waves.Amount == 4 + (up ? 9 : 6) && S().Orbs.Value(OrbKind.Tide) == 0
                    && positions.SequenceEqual(S().Orbs.Positions), "OpeningTide additive Waves " + up);

                await Reset(); await Dispatch(S().Orbs.Gain(OrbKind.Fire, 5));
                var second = await CreatureCmd.Add<MegaCrit.Sts2.Core.Models.Monsters.Tunneler>(player.Creature.CombatState!);
                await CreatureCmd.SetMaxAndCurrentHp(second, 10000);
                var foes = player.Creature.CombatState!.HittableEnemies.ToArray(); var hps = foes.Select(e => e.CurrentHp).ToArray();
                var burning = Card<BookBurning>(up); await Play(burning);
                Check(foes.Length == 2 && foes.Select((e, i) => hps[i] - e.CurrentHp).All(n => n == 5 * (up ? 4 : 3)), "BookBurning actual multi-hit all targets " + up);
                Check(burning.Pile?.Type == PileType.Exhaust && S().Orbs.Value(OrbKind.Fire) == 0, "BookBurning exhaust and one loss " + up);

                await Reset(); await Play(Card<ReadWidely>(up));
                Check(S().Orbs.Value(OrbKind.Growth) == 2 && S().Orbs.Value(OrbKind.Tide) == 3 && S().Orbs.Value(OrbKind.Fire) == 4
                    && S().Orbs.Foreground == OrbKind.Fire, "ReadWidely native gain order " + up);

                await Reset(); var immortal = Card<ImmortalSpark>(up); immortal.PermanentIncrease = 7;
                await Play(immortal); int increase = up ? 4 : 2;
                Check(immortal.PermanentIncrease == 7 + increase && S().Orbs.Value(OrbKind.Fire) == 8, "Immortal preserves old accumulation and changes future increment " + up);
                var loaded = (ImmortalSpark)CardModel.FromSerializable(immortal.ToSerializable());
                Check(loaded.PermanentIncrease == 7 + increase && loaded.DynamicVars["Increase"].IntValue == increase, "Immortal new state roundtrip " + up);

                await Reset(); var life = Card<LifeSymphony>(up); await Play(life);
                Check(life.Pile?.Type == PileType.Exhaust && player.Creature.GetPower<LifeSymphonyPendingPower>() is not null, "LifeSymphony native exhaust and pending effect " + up);

                await Reset(); await Play(Card<SeedburialStrike>(up), Enemy());
                Check(player.Creature.GetPower<SeedburialPendingPower>()?.Amount == (up ? 14 : 10), "Seedburial captures new pending amount " + up);
                await Turn();
                Check(S().Orbs.Value(OrbKind.Growth) == (up ? 14 : 10) && player.Creature.GetPower<SeedburialPendingPower>() is null,
                    "Seedburial real next turn " + up);

                await Reset(); foreach (var card in player.PlayerCombatState!.Hand.Cards.ToArray()) await CardPileCmd.Add(card, PileType.Discard);
                for (int i = 0; i < 2; i++) await CardPileCmd.AddGeneratedCardToCombat(Card<LibrarianDefend>(), PileType.Hand, player);
                await Play(Card<DeepSeaBarrier>(up));
                Check(player.Creature.GetPower<DeepSeaPendingPower>()?.Amount == 2 * (up ? 6 : 5) && S().Waves.Amount == 0, "DeepSea actual exhaust count and delay " + up);
                await Turn();
                Check(S().Waves.Amount == (up ? 6 : 5) && S().Orbs.Value(OrbKind.Tide) == 0
                    && player.Creature.GetPower<DeepSeaPendingPower>() is null, "DeepSea real frozen phase and decay " + up);
            }
            await Reset();
            await Capture(player, models, output, Check);
            var persistent = (ImmortalSpark)player.RunState.CreateCard(ModelDb.Card<ImmortalSpark>(), player);
            persistent.PermanentIncrease = 7; persistent.UpgradeInternal(); persistent.FinalizeUpgradeInternal();
            await CardPileCmd.Add(persistent, PileType.Deck);
            Check(persistent.Pile?.Type == PileType.Deck, "saved accumulated card in deck");
        }
        finally { LibrarianLanguage.Select(originalLanguage); }
        MainFile.Logger.Info($"CARD120_AUDIT_PASS checks={checks} formatted=364 realTurns={realTurns} batchCards=27 native=True liveMulticlient=False");
    }

    internal static void AfterReload(Player player)
    {
        var card = PileType.Deck.GetPile(player).Cards.OfType<ImmortalSpark>().Single(c => c.PermanentIncrease == 7);
        if (!card.IsUpgraded || card.DynamicVars["Fire"].IntValue != 8 || card.DynamicVars["Increase"].IntValue != 4)
            throw new InvalidOperationException("120 accumulated card after actual save/reload");
        MainFile.Logger.Info("CARD120_SAVE_RELOAD_PASS permanentIncrease=7 upgraded=True futureIncrease=4");
    }

    private static async Task Capture(Player player, CardModel[] models, string output, Action<bool, string> check)
    {
        check(DisplayServer.GetName() != "headless", "native card renderer");
        var originalSize = NGame.Instance!.GetWindow().Size;
        var layer = new CanvasLayer { Layer = 120 }; NGame.Instance.AddChild(layer);
        var panel = new Control(); layer.AddChild(panel);
        var background = new ColorRect { Color = new Color("20272e") }; panel.AddChild(background);
        int shots = 0;
        async Task Wait() => await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(.25), SceneTreeTimer.SignalName.Timeout);
        try
        {
            foreach (var resolution in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                DisplayServer.WindowSetSize(resolution); await Wait();
                var size = NGame.Instance.GetViewport().GetVisibleRect().Size; background.Size = size;
                foreach (string language in new[] { "zhs", "eng" })
                {
                    LibrarianLanguage.Select(language);
                    foreach (bool up in new[] { false, true })
                    {
                        for (int offset = 0; offset < Changed.Length; offset += 6)
                        {
                            var nodes = new List<NCard>();
                            try
                            {
                                foreach (string id in Changed.Skip(offset).Take(6))
                                {
                                    var card = models.Single(c => c.Id.Entry == "LIBRARIAN-" + id).ToMutable(); card.Owner = player;
                                    if (up) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
                                    var node = MegaCrit.Sts2.Core.Assets.PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>();
                                    panel.AddChild(node); node.Model = card;
                                    node.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
                                    int i = nodes.Count; node.Scale = Vector2.One * Math.Min(size.X / 1500, size.Y / 1100);
                                    node.Position = new(size.X * (i % 3 + .5f) / 3, size.Y * (i / 3 == 0 ? .27f : .75f));
                                    nodes.Add(node);
                                }
                                await Wait(); await NGame.Instance.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                                using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
                                check(image.SavePng(Path.Combine(output, $"cards-{language}-{resolution.X}-{up}-{offset / 6 + 1}.png")) == Error.Ok, "card screenshot");
                                shots++;
                            }
                            finally { foreach (var node in nodes) node.QueueFree(); }
                            await Wait();
                        }
                    }
                }
            }
        }
        finally { layer.QueueFree(); DisplayServer.WindowSetSize(originalSize); await Wait(); }
        MainFile.Logger.Info($"CARD120_VISUAL_CAPTURE_PASS screenshots={shots} cards=27 languages=2 resolutions=2 baseAndUpgrade=True");
    }
}
