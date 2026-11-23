using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using HarmonyLib;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Cards.Stateful;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;

namespace Librarian.Mechanics;

/// <summary>Separate opt-in entry. No legacy runtime/V130 audit or external input automation is enabled.</summary>
[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class DevelopmentCardRemasterAudit
{
    private static bool _ran;
    [HarmonyPostfix]
    private static void Postfix()
    {
        if (_ran || System.Environment.GetEnvironmentVariable("LIBRARIAN_CARD_REMASTER_AUDIT") != "1") return;
        _ran = true;
        Callable.From((Action)(() => { _ = DevelopmentRitsuCardSweepAudit.RunCardRemaster(); })).CallDeferred();
    }
}

/// <summary>Reuse the already exercised native queue/choice/reset fixture, not a second rules implementation.</summary>
internal static partial class DevelopmentRitsuCardSweepAudit
{
    private static readonly List<object> RemasterCases = [];
    private static readonly List<RemasterEvent> RemasterEvents = [];
    private static readonly List<object> RemasterResources = [];
    private static readonly List<object> RemasterViews = [];
    private static readonly List<object> RemasterSpecial = [];
    private static readonly List<LibrarianBindingObservation> BindingObservations = [];
    private static string _remasterCase = "initialization";
    private static int _remasterSequence;
    private sealed record RemasterEvent(string Case, int Sequence, string CardClass, string Phase,
        string Target, int TargetIdentity, ulong Frame, bool EffectValid, string Backend,
        ulong EffectIdentity = 0, bool? HasContact = null, long HitIdentity = 0, int OriginalTargetIdentity = 0);
    private static string TargetName(Creature target) => target.Monster?.Id.Entry ?? "player:" + target.Player?.NetId;
    private static CardModel[] RemasterCards() => ModelDb.CardPool<LibrarianCardPool>().AllCards
        .Append(ModelDb.Card<StudyNotes>()).OrderBy(c => c.Id.Entry, StringComparer.Ordinal).ToArray();
    private static void RemasterOldEvent(string phase, Creature target, string source)
    {
        RemasterEvents.Add(new(_remasterCase, ++_remasterSequence, source, phase, TargetName(target),
            RuntimeHelpers.GetHashCode(target), Engine.GetProcessFrames(), true, "command"));
        DevelopmentCardRemasterCapture.Observe(_remasterCase, phase, source);
    }
    private static void RemasterNewEvent(LibrarianCardRemasterObservation e)
    {
        RemasterEvents.Add(new(_remasterCase, ++_remasterSequence, e.CardClass, e.Phase, TargetName(e.Target),
            RuntimeHelpers.GetHashCode(e.Target), Engine.GetProcessFrames(), e.Effect is not null && GodotObject.IsInstanceValid(e.Effect),
            e.Phase == "native-result" ? "native-result" : "remaster",e.EffectIdentity,e.HasContact,e.HitIdentity,
            RuntimeHelpers.GetHashCode(e.OriginalTarget ?? e.Target)));
        DevelopmentCardRemasterCapture.Observe(_remasterCase, e);
    }
    private static void RemasterSpineEvent(LibrarianSpineVfxObservation e)
        => RemasterEvents.Add(new(_remasterCase, ++_remasterSequence, "spine", e.Stage, e.Socket.ToString(),
            0, e.Frame, true, "spine"));
    private static void RemasterBindingEvent(LibrarianBindingObservation e)
    {
        BindingObservations.Add(e);
        DevelopmentCardRemasterCapture.Observe(_remasterCase,"binding-"+e.Phase,"MultiplayerPlaceholderB");
    }
    private static void WriteRemaster(string name, object value)
        => File.WriteAllText(Path.Combine(Output, name + ".json"), JsonSerializer.Serialize(value, Json));
    private static IEnumerable<Node> RemasterOwnedNodes() => NCombatRoom.Instance is { } room
        ? Desc(room.CombatVfxContainer).Where(n => n.GetType().Assembly == typeof(MainFile).Assembly).ToArray() : [];
    private static async Task RemasterFreshFight(bool settle = true)
    {
        // Two or more genuine enemies exercise native random/AOE targeting without injecting targets.
        if (settle) await Settle(2.6);
        RemoveAlly();
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<SlimesWeak>().ToMutable());
        await Wait(() => CombatManager.Instance.IsInProgress && _player.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
            "remaster combat play phase");
        Check(_player.Creature.CombatState!.HittableEnemies.Count() >= 2, "native multi-enemy encounter");
    }
    private static void RemasterEventOrder(IEnumerable<RemasterEvent> events)
    {
        // FIFO per recipient: multi-hit commands can reuse a CardPlay and an effect node.
        foreach (var group in events.Where(e => e.Backend is "remaster" or "native-result").GroupBy(e => (e.CardClass, e.TargetIdentity)))
        {
            if (!group.Any(e => e.Phase == "flight")) continue; // Direct unpowered/delayed results have no AttackCommand flight.
            int flights = 0, contacts = 0, results = 0;
            foreach (var e in group.OrderBy(e => e.Sequence))
            {
                if (e.Phase == "flight") flights++;
                if (e.Phase == "contact") { contacts++; Check(contacts <= flights, "contact follows flight " + group.Key); }
                if (e.Phase == "native-result") { results++; Check(contacts > 0, "result follows contact " + group.Key); }
            }
            Check(flights == contacts && contacts <= results, "all native hit presentations complete " + group.Key);
        }
    }
    private static async Task RemasterResourcesAndViews(CardModel[] cards)
    {
        foreach (var card in cards)
        {
            Check(LibrarianCardVfx050.TryProfile(card, out _), "explicit VFX profile " + card.GetType().Name);
            string small = card.PortraitPath;
            string big = small.Replace("/card_portraits/", "/card_portraits/big/", StringComparison.Ordinal);
            bool ancient = card.Rarity == CardRarity.Ancient;
            foreach (var pair in new[] { (Path: small, Size: ancient ? new Vector2(303, 426) : new Vector2(250, 190)),
                (Path: big, Size: ancient ? new Vector2(606, 852) : new Vector2(1000, 760)) })
            {
                var texture = ResourceLoader.Load<Texture2D>(pair.Path);
                Check(texture is not null && texture.GetSize().IsEqualApprox(pair.Size), "native portrait dimensions " + pair.Path);
                using var image = texture!.GetImage();
                Check(image is not null && !image.IsEmpty(), "native decoded pixels " + pair.Path);
                if (image!.IsCompressed()) Check(image.Decompress() == Error.Ok, "native portrait decompression " + pair.Path);
                image.Convert(Image.Format.Rgba8);
                RemasterResources.Add(new { card_id = card.Id.Entry, card_class = card.GetType().Name, path = pair.Path,
                    width = image.GetWidth(), height = image.GetHeight(), rgba_sha256 = Convert.ToHexString(SHA256.HashData(image.GetData())).ToLowerInvariant() });
            }
        }
        WriteRemaster("resources", RemasterResources);
        MainFile.Logger.Info("CARD_REMASTER_RESOURCES_PASS cards=92 resources=184");
        var game = NGame.Instance!;
        var inspect = game.GetInspectCardScreen();
        var original = game.GetWindow().Size;
        try
        {
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                DisplayServer.WindowSetSize(size); await Settle(.5);
                foreach (bool upgraded in new[] { false, true }) foreach (var batch in cards.Chunk(8))
                {
                    var generated = new List<CardModel>();
                    try
                    {
                        foreach (var model in batch)
                        {
                            var card = Create(model, upgraded); generated.Add(card);
                            await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, _player);
                        }
                        await Settle(.35);
                        foreach (var card in generated)
                        {
                            var holder = NCombatRoom.Instance!.Ui.Hand.GetCardHolder(card);
                            Check(holder is NHandCardHolder && ReferenceEquals(holder.CardModel, card), "genuine hand holder " + card.Id);
                            holder!.CardNode.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
                            RemasterView(holder.CardNode, card, "hand", size);
                            inspect.Open([card], 0, viewAllUpgraded: upgraded); await Settle(.15);
                            Check(inspect.IsVisibleInTree(), "native inspected card " + card.Id);
                            RemasterView(inspect.GetNode<NCard>("Card"), card, "inspect", size);
                            inspect.Close(); await Frame();
                        }
                    }
                    finally
                    {
                        inspect.Close();
                        await CardPileCmd.Add(generated.Where(c => c.Pile?.Type == PileType.Hand).ToArray(), PileType.Discard);
                        await Settle(.5);
                    }
                }
            }
        }
        finally { inspect.Close(); DisplayServer.WindowSetSize(original); }
        Check(RemasterViews.Count == 736, "368 hand and 368 inspect views");
        WriteRemaster("views", RemasterViews);
        MainFile.Logger.Info("CARD_REMASTER_VIEWS_PASS cards=92 hand=368 inspect=368 screenshots=0 resolutions=2 upgrades=2");
    }
    private static void RemasterView(NCard node, CardModel card, string view, Vector2I size)
    {
        bool ancient = card.Rarity == CardRarity.Ancient;
        var portrait = node.GetNode<TextureRect>(ancient ? "%AncientPortrait" : "%Portrait");
        Check(node.IsVisibleInTree() && node.Model?.Id == card.Id && node.Model.IsUpgraded == card.IsUpgraded, "visible native card and upgrade " + card.Id);
        Check(node.GetNode<TextureRect>("%AncientPortrait").Visible == ancient
            && node.GetNode<TextureRect>("%Portrait").Visible != ancient, "native frame family " + card.Id);
        Check(portrait.Visible && portrait.Texture?.ResourcePath == card.PortraitPath
            && portrait.Texture.GetRid() == card.Portrait.GetRid(), "native portrait route " + card.Id);
        var rect = portrait.GetGlobalRect();
        float aspect = ancient ? 303f / 426f : 250f / 190f;
        Check(rect.Size.X > 0 && rect.Size.Y > 0 && Math.Abs(rect.Size.X / rect.Size.Y - aspect) < .03f, "portrait rectangle " + card.Id);
        if (view == "inspect")
        {
            var bounds = node.GetViewport().GetVisibleRect();
            Check(rect.Position.X >= bounds.Position.X - 3 && rect.Position.Y >= bounds.Position.Y - 3
                && rect.End.X <= bounds.End.X + 3 && rect.End.Y <= bounds.End.Y + 3, "inspect portrait inside viewport " + card.Id);
        }
        RemasterViews.Add(new { card_id = card.Id.Entry, card_class = card.GetType().Name, upgraded = card.IsUpgraded,
            view, resolution = size.X + "x" + size.Y, portrait_path = portrait.Texture!.ResourcePath,
            rect = new { x = rect.Position.X, y = rect.Position.Y, width = rect.Size.X, height = rect.Size.Y } });
    }
    private static async Task RemasterPlayCase(CardModel model, bool upgraded)
    {
        _remasterCase = "manual:" + model.GetType().Name + ":" + upgraded;
        MainFile.Logger.Info("CARD_REMASTER_CASE_BEGIN " + _remasterCase);
        await Reset(model);
        if (model is StudyNotes)
            foreach (var kind in Session.Orbs.Positions.ToArray()) await LibrarianRuntime.Dispatch(Session, Context, Session.Orbs.Lock(kind));
        var card = Create(model, upgraded);
        if (card.EnergyCost.CostsX) { _player.PlayerCombatState!.LoseEnergy(_player.PlayerCombatState.Energy); _player.PlayerCombatState.GainEnergy(3); }
        int start = RemasterEvents.Count;
        var before = Snapshot();
        await Play(card);
        Check(RemasterEvents.Skip(start).Any(e => e.CardClass == card.GetType().Name || e.Backend == "spine"),
            "presentation observed before stimulus plays " + card.Id);
        if (model.Type == CardType.Power || model.GetType().Name == "TreeRingBurst")
        {
            _player.PlayerCombatState!.GainEnergy(10);
            foreach (string source in new[] { "LibrarianStrike", "Spark", "Trickle", "LibrarianStrike", "LibrarianStrike", "LibrarianStrike" })
                await Play(Create(RemasterCards().Single(c => c.GetType().Name == source)));
            if (model.GetType().Name == "PracticeMakesPerfect")
                for (int i = 0; i < 2; i++) await Play(Create(RemasterCards().Single(c => c.GetType().Name == "Spark")));
        }
        var immediate = Snapshot();
        await Turn();
        if (model.GetType().Name == "TreeRingBurst") await Turn();
        await Settle(1);
        var events = RemasterEvents.Skip(start).ToArray();
        RemasterEventOrder(events);
        Check(events.Any(e => e.CardClass == card.GetType().Name || e.Backend == "spine"), "observed native presentation " + card.Id);
        if (card.Type == CardType.Attack && card.GetType().Name is not ("TreeRingBurst" or "EarthCollapse"))
            Check(events.Any(e => e.CardClass == card.GetType().Name && e.Phase == "contact"), "actual attack recipient " + card.Id);
        if (card.GetType().Name == "EarthCollapse")
            Check(events.Any(e => e.CardClass == "EarthCollapse" && e.Phase == "native-result")
                && events.Any(e => e.CardClass == "EarthCollapse" && e.Phase == "impact"), "direct AttackContext native result and stone residue");
        if (card.GetType().Name is "SeedburialStrike" or "LifeSymphony" or "ToBeContinued" or "TreeRingBurst" or "PracticeMakesPerfect")
            Check(events.Any(e => e.CardClass == card.GetType().Name && e.Phase == "trigger"), "real delayed ability trigger " + card.Id);
        Check(LibrarianCardVfx050.Active == 0, "expired owned effects " + card.Id);
        Check(LibrarianNativeCardLayersVfx.Emitters == 0 && LibrarianNativeCardLayersVfx.Capacity == 0,
            "expired runtime native layers " + card.Id);
        RemasterCases.Add(new { card_id = card.Id.Entry, card_class = card.GetType().Name, upgraded, before, immediate,
            next_turn = Snapshot(), events = events.Length, native_action_queue = true, real_end_turn = true });
        WriteRemaster("cases", RemasterCases); WriteRemaster("events", RemasterEvents);
        MainFile.Logger.Info("CARD_REMASTER_CASE_PASS " + _remasterCase);
    }
    private static async Task RemasterAutoPlay(CardModel card)
    {
        // Preserve native synchronized context through nested bottom play and choice screens.
        var owned = new HookPlayerChoiceContext(card, LocalContext.NetId!.Value, _player.Creature.CombatState!, GameActionType.Combat);
        Creature? target = card.TargetType == TargetType.AnyEnemy ? _player.Creature.CombatState!.HittableEnemies.First() : null;
        async Task Execute()
        {
            await owned.AssignTaskAndWaitForPauseOrCompletion(CardCmd.AutoPlay(owned, card, target));
            await owned.WaitForCompletion();
        }
        var task = Execute(); await Drive(() => task.IsCompleted, "remaster autoplay " + card.Id); await task;
        await Settle(1.5);
        Check(card.Pile?.Type is not (PileType.Hand or PileType.Play), "autoplay native completion " + card.Id);
    }
    private static async Task RemasterSpecialCases(CardModel[] cards, LibrarianRemasterFrameProbe probe)
    {
        CardModel Model(string name) => cards.Single(c => c.GetType().Name == name);
        foreach (string name in new[] { "NeedleFlurry", "ScatteredFlames", "FlyingPages", "TidalErosion", "CombatNotes" })
        {
            await Reset(Model(name)); _remasterCase = "special:" + name;
            int start = RemasterEvents.Count; var before = Snapshot();
            probe.Stage = "native-stress";
            await Play(Create(Model(name)));
            probe.Stage = "paused";
            var events = RemasterEvents.Skip(start).Where(e => e.CardClass == name).ToArray();
            RemasterEventOrder(events);
            Check(events.Any(e => e.Phase == "contact"), "special actual target contact " + name);
            if (name is "NeedleFlurry" or "ScatteredFlames" or "FlyingPages")
                Check(events.Count(e => e.Phase == "contact") >= 2, "each native multi-hit has contact " + name);
            RemasterSpecial.Add(new { scenario = _remasterCase, before, after = Snapshot(), events });
        }
        foreach (string name in new[] { "WaveStrike", "FlyingPages", "Nourish", "ThreefoldUnity" })
        {
            await Reset(Model(name)); _remasterCase = "autoplay:" + name;
            var nested = Create(Model(name));
            await CardPileCmd.Add(nested, PileType.Draw, CardPilePosition.Bottom);
            int start = RemasterEvents.Count;
            probe.Stage = "native-stress";
            await RemasterAutoPlay(Create(Model("ReadBackward")));
            probe.Stage = "paused";
            Check(nested.Pile?.Type is not (PileType.Draw or PileType.Play), "bottom autoplay used exact card " + name);
            var events = RemasterEvents.Skip(start).ToArray(); RemasterEventOrder(events);
            RemasterSpecial.Add(new { scenario = _remasterCase, events, after = Snapshot() });
        }
        WriteRemaster("special", RemasterSpecial);
        MainFile.Logger.Info("CARD_REMASTER_SPECIAL_PASS multihit=True random=True aoe=True bottomAutoplay=True nativeChoices=True");
    }
    private static async Task RemasterSettings(CardModel[] cards, string[]? modes = null)
    {
        CardModel model = cards.Single(c => c.GetType().Name == "FlyingPages");
        var results = new List<object>();
        decimal? normalDamage = null;
        foreach (string setting in modes ?? ["Normal", "Fast", "Instant", "CardEffectsOff", "ReducedMotion", "ParticlesOff", "Opacity10", "Limit4", "Limit40", "TeammateEffectsOff"])
        {
            LibrarianPreferences050.Reset();
            SaveManager.Instance.PrefsSave.FastMode = setting == "Fast" ? FastModeType.Fast : setting == "Instant" ? FastModeType.Instant : FastModeType.Normal;
            var p = LibrarianPreferences050.Current;
            if (setting == "CardEffectsOff") p.CardEffects = false;
            if (setting == "ReducedMotion") p.ReducedMotion = true;
            if (setting == "ParticlesOff") p.Particles = false;
            if (setting == "Opacity10") p.EffectOpacity = 10;
            if (setting == "Limit4") p.EffectLimit = 4;
            if (setting == "Limit40") p.EffectLimit = 40;
            if (setting == "TeammateEffectsOff") p.TeammateEffects = false;
            LibrarianPreferences050.Save(); var expected = JsonSerializer.Serialize(p);
            LibrarianPreferences050.Load(); Check(expected == JsonSerializer.Serialize(LibrarianPreferences050.Current), "presentation settings persistence " + setting);
            await Reset(model); _remasterCase = "settings:" + setting;
            LibrarianCardVfx050.ResetCounters(); int start = RemasterEvents.Count;
            var before = Snapshot();
            decimal hpBefore = _player.Creature.CombatState!.HittableEnemies.Sum(e => e.CurrentHp);
            await Play(Create(model));
            decimal damage = hpBefore - _player.Creature.CombatState!.HittableEnemies.Sum(e => e.CurrentHp);
            normalDamage ??= damage;
            Check(damage == normalDamage, "settings preserve actual random attack damage " + setting);
            var events = RemasterEvents.Skip(start).ToArray();
            if (setting is "Instant" or "CardEffectsOff") Check(!events.Any(e => e.Backend is "spine" or "command"
                || e.Phase is "flight" or "contact"), "suppressed mode emits no owned effects " + setting);
            else RemasterEventOrder(events);
            Check(LibrarianCardVfx050.Peak <= LibrarianPreferences050.Current.EffectLimit, "shared effect budget " + setting);
            Check(LibrarianCardVfx050.Active == 0, "settings effects reclaimed " + setting);
            Check(LibrarianNativeCardLayersVfx.PeakEmitters <= 64 && LibrarianNativeCardLayersVfx.PeakCapacity <= 256,
                "runtime native layer capacity bounded " + setting);
            Check(LibrarianNativeCardLayersVfx.Emitters == 0 && LibrarianNativeCardLayersVfx.Capacity == 0,
                "runtime native layers reclaimed after setting " + setting);
            results.Add(new { setting, before, after = Snapshot(), events, actual_damage = damage,
                peak = LibrarianCardVfx050.Peak, dropped = LibrarianCardVfx050.Dropped,
                native_emitters_peak = LibrarianNativeCardLayersVfx.PeakEmitters,
                native_capacity_peak = LibrarianNativeCardLayersVfx.PeakCapacity });
        }
        // A transient native ally exercises the local/remote presentation predicate only.
        // It is explicitly not a network client and does not count as real multiplayer.
        await Reset(cards.Single(c => c.GetType().Name == "MultiplayerPlaceholderA"));
        Check(_ally is not null, "native transient ally for settings predicate");
        LibrarianPreferences050.Current.TeammateEffects = false;
        Check(!LibrarianCardVfx050.Available(_ally), "teammate effects disabled for remote owner");
        LibrarianPreferences050.Current.TeammateEffects = true;
        Check(LibrarianCardVfx050.Available(_ally), "teammate effects enabled for remote owner");
        RemoveAlly();
        await Settle(2);
        LibrarianPreferences050.Current.EffectLimit = 4; LibrarianCardVfx050.ResetCounters();
        var enemy = _player.Creature.CombatState!.HittableEnemies.First();
        for (int i = 0; i < 100; i++) LibrarianCardVfx050.Emit(enemy, new(SpellElement050.Page, SpellShape050.Pages), "impact", "remaster-budget", _player);
        Check(LibrarianCardVfx050.Peak <= 4 && LibrarianCardVfx050.Dropped >= 96, "hundred event cosmetic burst bounded to four");
        await Settle(2); Check(LibrarianCardVfx050.Active == 0, "burst effects reclaimed");
        LibrarianPreferences050.Reset(); SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
        WriteRemaster("settings", results);
        MainFile.Logger.Info($"CARD_REMASTER_SETTINGS_PASS modes={results.Count} persistence=True nativeResolution=True");
    }
    private static async Task RemasterBindings(CardModel[] cards)
    {
        var model=cards.Single(c=>c.GetType().Name=="MultiplayerPlaceholderB");
        var checks=new List<object>();
        LibrarianPreferences050.Reset(); SaveManager.Instance.PrefsSave.FastMode=FastModeType.Normal;
        await Reset(model); _remasterCase="binding:ally-payout";
        await CreatureCmd.GainBlock(_ally!.Creature,7,MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered,null);
        int start=BindingObservations.Count;
        await Play(Create(model)); await Play(Create(model));
        Check(Session.Bindings.Count==2 && LibrarianBindingVfx.Live.Count==2,"two real pending binding occurrences");
        LibrarianPreferences050.Current.CardEffects=false; await Settle(.10);
        Check(LibrarianBindingVfx.Live.Values.All(n=>!n.Visible) && Session.Bindings.Count==2,"binding toggle hides presentation without deleting rules");
        LibrarianPreferences050.Current.CardEffects=true; LibrarianPreferences050.Current.ReducedMotion=true; await Settle(.10);
        Check(LibrarianBindingVfx.Live.Values.Any(n=>n.Visible),"binding reduced mode keeps local clasp");
        DevelopmentCardRemasterCapture.Observe("binding:reduced-motion","binding-registered","MultiplayerPlaceholderB");
        await Settle(.10); LibrarianPreferences050.Current.ReducedMotion=false;
        decimal before=_player.Creature.Block;
        // A transient ally has no network client. Invoke the actual late-turn hook, not
        // a duplicate payout algorithm, and label this fixture separately from real multiplayer.
        await LibrarianGameApi.RegisteredCombatHooks.AfterSideTurnEndLate(Context,_player.Creature.Side,[_player.Creature]);
        await Settle(.6);
        var paid=BindingObservations.Skip(start).ToArray();
        Check(paid.Count(e=>e.Phase=="payout-complete" && e.ActualGranted==7)==2
            && _player.Creature.Block-before==14,"binding native return values consumed exactly twice");
        Check(LibrarianBindingVfx.Live.Count==0,"binding payout unbinds every occurrence");
        checks.Add(new{scenario=_remasterCase,native_late_hook_fixture=true,real_multiclient=false,
            payouts=paid.Where(e=>e.Phase=="payout-complete").Select(e=>new{e.Token,e.ActualGranted})});

        await Reset(model); RemoveAlly(); _remasterCase="binding:self-fallback";
        start=BindingObservations.Count; await Play(Create(model));
        Check(LibrarianBindingVfx.Live.Values.All(n=>n.OwnerPlayer==n.TargetPlayer),"single-player binding uses local self clasp");
        await Turn(); await Settle(.4);
        var self=BindingObservations.Skip(start).ToArray();
        Check(self.Any(e=>e.Phase=="payout-complete" && e.ActualGranted>0 && e.Owner==e.Target)
            && LibrarianBindingVfx.Live.Count==0,"real self turn pays and releases binding");
        checks.Add(new{scenario=_remasterCase,real_end_turn=true,self=true});

        foreach(string boundary in new[]{"target-exit","room-exit"})
        {
            await Reset(model); _remasterCase="binding:"+boundary;
            await Play(Create(model)); var nodes=LibrarianBindingVfx.Live.Values.ToArray();
            Check(nodes.Length>0,"binding fixture has live node before "+boundary);
            if(boundary=="target-exit") {RemoveAlly();await Settle(.3);}
            else
            {
                // Native hand/exhaust card tweens own their pool return. Drain those before
                // the forced fixture room transition; the real binding remains pending.
                await CardPileCmd.Add(PileType.Hand.GetPile(_player).Cards.ToArray(),PileType.Discard);
                await Settle(2.6);
                Check(NCombatRoom.Instance!.Ui.Hand.ActiveHolders.Count==0,"native hand drained before binding room exit");
                Check(nodes.All(n=>GodotObject.IsInstanceValid(n)&&n.IsInsideTree()&&n.Pending),"pending bindings retained while native card UI drains");
                await RemasterFreshFight(settle:false);
            }
            Check(LibrarianBindingVfx.Live.Count==0 && nodes.All(n=>!GodotObject.IsInstanceValid(n)),"binding cleanup "+boundary);
            checks.Add(new{scenario=_remasterCase,live_before=nodes.Length,reclaimed=true});
        }
        WriteRemaster("binding-boundaries",checks);
        WriteRemaster("binding-events",BindingObservations.Select(e=>new{e.Token,e.Phase,owner=e.Owner.NetId,target=e.Target.NetId,e.ActualGranted,e.Visible,e.Frame}));
        MainFile.Logger.Info("CARD_REMASTER_BINDING_PASS selected=True payout=True self=True toggle=True targetExit=True roomExit=True realMulticlient=False");
    }
    private static async Task RemasterRestart(CardModel[] cards)
    {
        var saved = SaveManager.Instance.LoadRunSave(); Check(saved.Success && saved.SaveData is not null, "second-process isolated save");
        var run = RunState.FromSerializable(saved.SaveData!);
        await RunManager.Instance.SetUpSavedSingleplayer(run, saved.SaveData!);
        await NGame.Instance!.LoadRun(run, saved.SaveData!.PreFinishedRoom); await NGame.Instance.Transition.FadeIn();
        _player = run.Players.Single();
        var deck = PileType.Deck.GetPile(_player).Cards;
        Check(deck.Count == 184 && cards.All(c => deck.Count(d => d.Id == c.Id) == 2), "second-process all92 base and upgrade identities");
        Check(deck.OfType<ImmortalSpark>().All(c => c.PermanentIncrease == 7), "second-process permanent state");
        LibrarianPreferences050.Load(); Check(LibrarianPreferences050.Current.EffectOpacity == 10 && LibrarianPreferences050.Current.ReducedMotion
            && !LibrarianPreferences050.Current.Particles, "second-process presentation settings");
        LibrarianPreferences050.Reset(); SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
        await RemasterFreshFight(); await Reset(cards[0]);
        await Play(Create(cards.Single(c => c.GetType().Name == "LibrarianStrike")));
        Check(LibrarianCardVfx050.Active == 0, "second-process effects expire");
        WriteRemaster("restart", new { passed = true, cards = 92, deck = 184, continuation = true, user_data = OS.GetUserDataDir() });
        MainFile.Logger.Info("CARD_REMASTER_RESTART_PASS cards=92 deck=184 settings=True continuation=True");
    }
    private static async Task RemasterFailureBoundaries(CardModel[] cards)
    {
        CardModel Model(string name) => cards.Single(c => c.GetType().Name == name);
        var boundaries = new List<object>();
        // Native AOE factory: the first visual succeeds, then fill its cosmetic budget.
        // The original attack command, its recipient list and damage remain untouched.
        foreach (string mode in new[] { "budget", "settings" })
        {
            LibrarianPreferences050.Reset(); SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
            string name = mode == "budget" ? "TidalErosion" : "FlyingPages";
            await Reset(Model(name)); _remasterCase = "boundary:" + mode + "-after-first-flight";
            int start = RemasterEvents.Count;
            bool injected = false;
            var previous = LibrarianCardRemasterVfx.Observer;
            try
            {
                LibrarianCardRemasterVfx.Observer = e =>
                {
                    previous?.Invoke(e);
                    if (injected || e.CardClass != name || e.Phase != "flight") return;
                    injected = true;
                    if (mode == "settings") LibrarianPreferences050.Current.CardEffects = false;
                    else
                    {
                        LibrarianPreferences050.Current.EffectLimit = 4;
                        for (int i = 0; i < 4 && LibrarianCardVfx050.Active < 4; i++)
                            LibrarianCardVfx050.Emit(e.Target,new(SpellElement050.Page,SpellShape050.Pages),
                                "impact","remaster-budget-fill",_player);
                    }
                };
                await Play(Create(Model(name)));
            }
            finally { LibrarianCardRemasterVfx.Observer = previous; LibrarianPreferences050.Reset(); }
            var observed = RemasterEvents.Skip(start).Where(e => e.CardClass == name).ToArray();
            Check(injected && observed.Any(e => e.Phase == "flight"), "boundary begins with successful flight " + mode);
            Check(observed.Any(e => e.Phase == "native-fallback"), "rejected same-command visual retains native contact " + mode);
            if (mode == "settings")
                Check(observed.Any(e => e.Phase == "native-pass-through"), "later native hit restores original HitVfx after toggle");
            else
                Check(observed.Any(e => e.Phase == "contact"), "first AOE recipient keeps its successful contact");
            Check(observed.Count(e => e.Phase == "native-result") >= 2, "same native command still damages all native hits " + mode);
            boundaries.Add(new { scenario = _remasterCase, injected, events = observed });
        }

        // Real Escape removes only the original target while another enemy keeps combat alive.
        // The presentation task must cancel; it neither retargets nor performs any damage.
        await Reset(Model("TreeRingBurst")); _remasterCase = "boundary:delayed-target-escape";
        var victim = _player.Creature.CombatState!.HittableEnemies.First();
        var previousObserver = LibrarianCardRemasterVfx.Observer;
        Task? escaping = null;
        int escapeStart = RemasterEvents.Count;
        try
        {
            LibrarianCardRemasterVfx.Observer = e =>
            {
                previousObserver?.Invoke(e);
                if (e.CardClass == "TreeRingBurst" && e.Phase == "flight" && escaping is null)
                    escaping = CreatureCmd.Escape(victim);
            };
            await LibrarianCardRemasterVfx.Delayed(Create(Model("TreeRingBurst")),victim);
            if (escaping is not null) await escaping;
            await Settle(1);
        }
        finally { LibrarianCardRemasterVfx.Observer = previousObserver; }
        var escapedEvents = RemasterEvents.Skip(escapeStart).Where(e => e.CardClass == "TreeRingBurst").ToArray();
        Check(escaping is not null && CombatManager.Instance.IsInProgress && !CombatManager.Instance.IsOverOrEnding,
            "native target escape occurs while another enemy keeps combat alive");
        Check(escapedEvents.Any(e => e.Phase == "flight") && escapedEvents.Any(e => e.Phase == "cancel")
            && !escapedEvents.Any(e => e.Phase == "contact"), "escaped delayed target has cancellation and no ghost contact");
        Check(LibrarianCardVfx050.Active == 0 && LibrarianNativeCardLayersVfx.Emitters == 0, "escaped delayed target releases layers");
        boundaries.Add(new { scenario = _remasterCase, events = escapedEvents, damage_issued = false });

        await RemasterFreshFight(); await Reset(Model("EarthCollapse"));
        _remasterCase = "boundary:fatal-earth-collapse";
        var doomed = _player.Creature.CombatState!.HittableEnemies.ToArray();
        foreach (var enemy in doomed) await CreatureCmd.SetMaxAndCurrentHp(enemy,1);
        int fatalStart = RemasterEvents.Count;
        var renderedRemains = new List<Task<bool>>();
        var oldFeedback = LibrarianCardVfx050.Observer;
        async Task<bool> RemainsAfterRender(Node2D effect)
        {
            await NGame.Instance!.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            return GodotObject.IsInstanceValid(effect) && effect.IsInsideTree() && effect.IsVisibleInTree();
        }
        try
        {
            LibrarianCardVfx050.Observer = (phase,target,source) =>
            {
                oldFeedback?.Invoke(phase,target,source);
                if (phase != "impact" || source != "EarthCollapse" || !target.IsDead) return;
                var stone = Desc(NCombatRoom.Instance!.CombatVfxContainer).OfType<LibrarianSpineSpellPulse>()
                    .LastOrDefault(n => n.Stage == LibrarianSpineSpellStage.Impact && n.FollowCreature == target);
                Check(stone is not null,"fatal stone residue exists before native Kill removal");
                renderedRemains.Add(RemainsAfterRender(stone!));
            };
            await Play(Create(Model("EarthCollapse")));
        }
        finally { LibrarianCardVfx050.Observer = oldFeedback; }
        var fatalEvents = RemasterEvents.Skip(fatalStart).Where(e => e.CardClass == "EarthCollapse").ToArray();
        foreach (var enemy in doomed)
        {
            int id = RuntimeHelpers.GetHashCode(enemy);
            Check(enemy.IsDead && fatalEvents.Count(e => e.TargetIdentity == id && e.Phase == "native-result") == 1,
                "lethal direct damage reports exactly one native result");
            Check(fatalEvents.Count(e => e.TargetIdentity == id && e.Phase == "impact") == 1,
                "lethal direct damage reports exactly one stone contact");
        }
        var rendered = await Task.WhenAll(renderedRemains);
        Check(rendered.Length == doomed.Length && rendered.All(x => x),"confirmed fatal stone residues survive to actual rendered frame");
        boundaries.Add(new { scenario = _remasterCase, targets = doomed.Length, rendered, events = fatalEvents });
        await RemasterFreshFight();
        WriteRemaster("failure-boundaries",boundaries);
        MainFile.Logger.Info("CARD_REMASTER_BOUNDARIES_PASS fatalEarthCollapse=True fallback=True targetExit=True");
    }
    internal static async Task RunCardRemaster()
    {
        string phase = System.Environment.GetEnvironmentVariable("LIBRARIAN_CARD_REMASTER_PHASE") ?? "main";
        string root = System.Environment.GetEnvironmentVariable("LIBRARIAN_CARD_REMASTER_PROFILE") ?? "";
        string profile = Path.GetFullPath(OS.GetUserDataDir());
        var game = NGame.Instance!;
        string? originalPreferences = null;
        var fast = SaveManager.Instance.PrefsSave.FastMode;
        double timeScale = Engine.TimeScale;
        var oldObserver = LibrarianCardVfx050.Observer;
        var newObserver = LibrarianCardRemasterVfx.Observer;
        var spineObserver = LibrarianSpineCastVfx.Observer;
        var bindingObserver=LibrarianBindingVfx.Observer;
        LibrarianRemasterFrameProbe? probe = null;
        bool success = false;
        bool capturedPreferences = false;
        try
        {
            Check(Path.IsPathFullyQualified(root) && profile.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && profile.Contains("card-remaster-20261029", StringComparison.OrdinalIgnoreCase), "explicit remaster isolated profile");
            Check(Path.IsPathFullyQualified(Output) && DisplayServer.GetName() != "headless", "rendered native audit and absolute output");
            Check(System.Environment.GetEnvironmentVariable("LIBRARIAN_RUNTIME_AUDIT") != "1" && System.Environment.GetEnvironmentVariable("LIBRARIAN_V130_AUDIT") != "1", "legacy audit entries disabled");
            Check(LibrarianUpdateNotice051.CurrentVersion == System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_VERSION"), "frozen loaded mod version");
            Directory.CreateDirectory(Output);
            DevelopmentCardRemasterCapture.Configure(Output);
            Check(game.MainMenu!.IsVisibleInTree(), "native rendered main menu");
            var startupFence=await DevelopmentCardRemasterStartupFence.WaitForCompletion();
            WriteRemaster("startup-fence",startupFence);
            MainFile.Logger.Info($"CARD_REMASTER_STARTUP_FENCE_PASS name=Common task={startupFence.TaskStatus} frame={startupFence.CompletedFrame}");
            originalPreferences = File.Exists(LibrarianPreferences050.FilePath) ? File.ReadAllText(LibrarianPreferences050.FilePath) : null;
            capturedPreferences = true;
            if (phase is "main" or "preview") File.WriteAllText(Path.Combine(Output, "original-presentation.json"), originalPreferences ?? "null");
            var cards = RemasterCards(); Check(cards.Length == 92 && cards.Select(c => c.Id).Distinct().Count() == 92, "91 active plus generated StudyNotes");
            Engine.TimeScale = 1;
            LibrarianCardVfx050.Observer = RemasterOldEvent;
            LibrarianCardRemasterVfx.Observer = RemasterNewEvent;
            LibrarianSpineCastVfx.Observer = RemasterSpineEvent;
            LibrarianBindingVfx.Observer=RemasterBindingEvent;
            if (phase == "restart") { LibrarianPreferences050.Load(); await RemasterRestart(cards); success = true; return; }
            Check(phase is "main" or "preview", "explicit main, preview or restart phase");
            LibrarianPreferences050.Reset(); SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
            LibrarianUnlocks040.ApplyChoice(SaveManager.Instance.Progress, true);
            var run = await game.StartNewSingleplayerRun(ModelDb.Character<LibrarianCharacter>(), true, ActModel.GetDefaultList(), [], "CARDREMASTER", GameMode.Standard);
            _player = run.Players.Single();
            await RemasterFreshFight();
            foreach (var relic in _player.Relics.ToArray()) await RelicCmd.Remove(relic);
            await CardPileCmd.Add(PileType.Hand.GetPile(_player).Cards.ToArray(), PileType.Discard);
            probe=new LibrarianRemasterFrameProbe();
            game.AddChild(probe); probe.Stage = "idle";
            await Settle(3); probe.Stage = "paused";
            string requestedResolution=System.Environment.GetEnvironmentVariable("LIBRARIAN_CARD_REMASTER_RESOLUTION") ?? "1280x720";
            Check(requestedResolution is "1280x720" or "1920x1080","supported requested physical window size");
            Vector2I requestedWindow=requestedResolution=="1920x1080"?new(1920,1080):new(1280,720);
            game.GetWindow().Mode=Window.ModeEnum.Windowed;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            game.GetWindow().Size=requestedWindow;
            DisplayServer.WindowSetSize(requestedWindow);
            await Settle(.5);
            Check(DisplayServer.WindowGetSize()==requestedWindow && game.GetWindow().Size==requestedWindow,
                "physical window matches requested size after stable combat");
            WriteRemaster("window",new{requested=requestedResolution,mode=game.GetWindow().Mode.ToString(),
                display_server=new{x=DisplayServer.WindowGetSize().X,y=DisplayServer.WindowGetSize().Y},
                window=new{x=game.GetWindow().Size.X,y=game.GetWindow().Size.Y},
                content_scale=new{x=game.GetWindow().ContentScaleSize.X,y=game.GetWindow().ContentScaleSize.Y},
                viewport_texture=new{x=game.GetViewport().GetTexture().GetWidth(),y=game.GetViewport().GetTexture().GetHeight()}});
            if (phase == "preview")
            {
                // Actual native cards/choices/turns, bounded representatives only. This is
                // a development visual review pass, never the all92 or recovery acceptance.
                string[] defaults=["LibrarianStrike", "NeedleFlurry", "FlameStrike", "TidalErosion",
                    "Rootbind", "SteamBlast", "Bookworm", "WearyIncantation", "EarthCollapse", "TreeRingBurst",
                    "LibrarianDefend", "StudyNotes", "SeedburialStrike", "LifeSymphony", "ToBeContinued"];
                string? requested=System.Environment.GetEnvironmentVariable("LIBRARIAN_CARD_REMASTER_CARDS");
                bool focused=!string.IsNullOrWhiteSpace(requested);
                string[] selected=focused?requested!.Split(',',StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray():defaults;
                Check(selected.Length>0 && selected.All(n=>defaults.Contains(n)||n=="MultiplayerPlaceholderB"),"bounded preview card selection");
                foreach (string name in selected.Where(n=>n!="MultiplayerPlaceholderB"))
                    await RemasterPlayCase(cards.Single(c => c.GetType().Name == name), false);
                await RemasterSettings(cards,focused?["ReducedMotion","ParticlesOff"]:null);
                if(!focused) await RemasterFailureBoundaries(cards);
                bool binding=!focused || selected.Contains("MultiplayerPlaceholderB");
                if(binding) await RemasterBindings(cards);
                if(focused) MainFile.Logger.Info($"CARD_REMASTER_FOCUS_PASS cards={string.Join(',',selected)} binding={binding} allFailureBoundaries=False");
                await DevelopmentCardRemasterCapture.Drain();
                WriteRemaster("preview-summary", new { passed = true, scope = "representative-visual-development",
                    cases = RemasterCases.Count, screenshots = DevelopmentCardRemasterCapture.Saved,
                    selected_cards=selected,focused,binding_boundaries=binding,all_failure_boundaries=!focused,
                    capture_overhead = DevelopmentCardRemasterCapture.Enabled, full_matrix_passed = false,
                    visual_review_pending = true, save_reload_tested = false });
                MainFile.Logger.Info($"CARD_REMASTER_PREVIEW_PASS cases={RemasterCases.Count} screenshots={DevelopmentCardRemasterCapture.Saved} fullMatrix=False");
                success = true;
                return;
            }
            await RemasterResourcesAndViews(cards);
            foreach (var model in cards) foreach (bool upgraded in new[] { false, true }) await RemasterPlayCase(model, upgraded);
            Check(RemasterCases.Count == 184, "all92 native base and upgrade manual actions");
            MainFile.Logger.Info("CARD_REMASTER_PLAYS_PASS cards=92 cases=184 realTurns=True");
            await RemasterSpecialCases(cards, probe);
            await RemasterSettings(cards);
            await RemasterFailureBoundaries(cards);
            await RemasterBindings(cards);
            // Capture genuinely live owned nodes before replacing the room; an empty array is not cleanup evidence.
            var cleanupEnemy = _player.Creature.CombatState!.HittableEnemies.First();
            LibrarianCardVfx050.Emit(cleanupEnemy, new(SpellElement050.Page, SpellShape050.Pages), "impact", "remaster-room-cleanup", _player);
            var oldNodes = RemasterOwnedNodes().ToArray();
            Check(oldNodes.Length > 0, "live owned nodes captured before room transition");
            await RemasterFreshFight(settle: false); await Settle(3);
            Check(oldNodes.All(n => !GodotObject.IsInstanceValid(n)), "room transition invalidates owned VFX");
            Check(LibrarianCardVfx050.Active == 0, "no stale owned effect counters");
            MainFile.Logger.Info("CARD_REMASTER_CLEANUP_PASS active=0 roomTransition=True");
            await SaveReload(cards);
            MainFile.Logger.Info("SAVE_RELOAD_AUDIT_PASS revision=card-remaster cards=184 continuation=True");
            // Leave deliberate settings for the mandatory second-process check in this disposable profile.
            LibrarianPreferences050.Current.EffectOpacity = 10; LibrarianPreferences050.Current.ReducedMotion = true;
            LibrarianPreferences050.Current.Particles = false; LibrarianPreferences050.Save();
            WriteRemaster("frame-times", new { samples = probe.Samples, time_scale = 1, max_fps = 60,
                capture_overhead = DevelopmentCardRemasterCapture.Enabled,
                note = "Native process frame observations; independent performance acceptance is separate." });
            WriteRemaster("events", RemasterEvents);
            await DevelopmentCardRemasterCapture.Drain();
            WriteRemaster("summary", new { passed = true, cards = 92, cases = 184, resources = 184, views = RemasterViews.Count,
                checks = _checks, phase, user_data = profile, screenshots = DevelopmentCardRemasterCapture.Saved, real_multiclient = false, user_visual_acceptance = false });
            success = true;
            MainFile.Logger.Info($"CARD_REMASTER_AUDIT_PASS cards=92 cases=184 resources=184 views=736 checks={_checks} screenshots={DevelopmentCardRemasterCapture.Saved}");
        }
        catch (Exception error)
        {
            MainFile.Logger.Error("CARD_REMASTER_AUDIT_FAIL case=" + _remasterCase + " " + error);
            if (Directory.Exists(Output)) WriteRemaster("failure", new { phase, current_case = _remasterCase, error = error.ToString(), checks = _checks });
        }
        finally
        {
            await DevelopmentCardRemasterCapture.Stop();
            LibrarianCardVfx050.Observer = oldObserver; LibrarianCardRemasterVfx.Observer = newObserver;
            LibrarianSpineCastVfx.Observer = spineObserver;
            LibrarianBindingVfx.Observer=bindingObserver;
            if (probe is not null && GodotObject.IsInstanceValid(probe)) probe.QueueFree();
            Engine.TimeScale = timeScale; SaveManager.Instance.PrefsSave.FastMode = fast;
            // Main success persists the fixture setting only until its second-process check.
            if (capturedPreferences && (!success || phase is "restart" or "preview"))
            {
                string backup = Path.Combine(Output, "original-presentation.json");
                string? content = phase == "restart" && File.Exists(backup) ? File.ReadAllText(backup) : originalPreferences;
                if (content is not null && content != "null") File.WriteAllText(LibrarianPreferences050.FilePath, content);
                else if (File.Exists(LibrarianPreferences050.FilePath)) File.Delete(LibrarianPreferences050.FilePath);
                LibrarianPreferences050.Load();
            }
            if (RunManager.Instance.IsInProgress) await game.ReturnToMainMenu();
            await Settle(2); game.Quit();
        }
    }
}

/// <summary>Bounded native frame telemetry, without viewport readbacks or a screenshot stream.</summary>
public partial class LibrarianRemasterFrameProbe : Node
{
    internal string Stage = "paused";
    internal readonly List<object> Samples = [];
    public override void _Process(double delta)
    {
        if (Stage == "paused" || Samples.Count >= 30000) return;
        Samples.Add(new { stage = Stage, frame = Engine.GetProcessFrames(), delta_ms = delta * 1000,
            cpu_ms = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000,
            owned_active = LibrarianCardVfx050.Active, native_emitters = LibrarianNativeCardLayersVfx.Emitters,
            native_capacity = LibrarianNativeCardLayersVfx.Capacity });
    }
}
