using Godot;
using Librarian.Core;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.TestSupport;

namespace Librarian.Mechanics;

internal enum SpellElement050 { Page, Fire, Water, Leaf, Prismatic }
internal enum SpellShape050 { Slash, Needle, Root, Ripple, Steam, Stone, Seal, Pages, Ring, Ember }
internal sealed record SpellProfile050(SpellElement050 Element, SpellShape050 Shape);

/// <summary>All events are cosmetic observations of native commands. No RNG, waits, targeting or rule mutations.</summary>
internal static class LibrarianCardVfx050
{
    internal static readonly IReadOnlyDictionary<string, SpellProfile050> Profiles = CreateProfiles();
    // Filled explicitly: no inference from localized descriptions, rarity or card numbers.
    private static Dictionary<string, SpellProfile050> CreateProfiles()
    {
        var result = new Dictionary<string, SpellProfile050>(StringComparer.Ordinal);
        void Add(SpellElement050 e, SpellShape050 s, string names)
        { foreach (string name in names.Split(' ', StringSplitOptions.RemoveEmptyEntries)) result.Add(name, new(e, s)); }
        Add(SpellElement050.Fire, SpellShape050.Ember, "Spark Reignite FlameStrike AshenBlow BurningPages BurnTheRiver EmberReckoning Rekindle OverloadBurn BookBurning ImmortalSpark AncientSpark");
        Add(SpellElement050.Fire, SpellShape050.Needle, "EmberPierce");
        Add(SpellElement050.Fire, SpellShape050.Root, "BurnRoots");
        Add(SpellElement050.Fire, SpellShape050.Ring, "ResidualWarmth EmberBookmark BlazingChapter MultiplayerPlaceholderA");
        Add(SpellElement050.Water, SpellShape050.Ripple, "Trickle WaveStrike Springwater ChannelFlow BuildCanal TidalErosion DamBreak OpeningTide TidalGravity WaveCurtain Cooldown RidgeWard TidalMark WaterSpirit UnretreatingTide EndlessTide");
        Add(SpellElement050.Water, SpellShape050.Steam, "SteamBlast Evaporation DeepSeaBarrier");
        Add(SpellElement050.Water, SpellShape050.Seal, "QuietEmbers");
        Add(SpellElement050.Leaf, SpellShape050.Root, "Renewal SproutingSeed VineShield Rootbind SeedburialStrike ThornBurst DrawBranch SproutingBulwark Afforestation LifeSymphony");
        Add(SpellElement050.Leaf, SpellShape050.Stone, "EarthCollapse DryBranchSearch");
        Add(SpellElement050.Leaf, SpellShape050.Ring, "CropRotation RingCurriculum Lifeline AncientCatalog");
        Add(SpellElement050.Page, SpellShape050.Needle, "NeedleFlurry GapNeedle");
        Add(SpellElement050.Page, SpellShape050.Slash, "LibrarianStrike CombatNotes");
        Add(SpellElement050.Page, SpellShape050.Seal, "LibrarianDefend ScatteredFlames WearyIncantation ArchiveBulwark Ignite SharedShelter");
        Add(SpellElement050.Page, SpellShape050.Pages, "FlyingPages ReadBackward Nourish Calibrate OutOfContext Bookworm ZeroSearch Transcribe FuelTheFire CirculationNotes ToBeContinued MultiplayerPlaceholderB");
        Add(SpellElement050.Prismatic, SpellShape050.Ring, "EternalGrimoire Overfishing Sedimentation ShiftingPages ThreefoldUnity NourishingLife FireInscription ReadWidely TreeRingBurst SongOfIceAndFire MultipleEruption SpacetimeTwist OverlimitForm PracticeMakesPerfect ReRead");
        return result;
    }
    internal static bool TryProfile(CardModel card, out SpellProfile050 profile)
    {
        profile = null!;
        return card is ILibrarianCard && Profiles.TryGetValue(card.GetType().Name, out profile);
    }
    internal static Color Tint(SpellElement050 e) => new(e switch
    { SpellElement050.Fire => "efa05e", SpellElement050.Water => "7bd6eb", SpellElement050.Leaf => "a3ce77", SpellElement050.Prismatic => "d1b7ed", _ => "ead4a4" });
    internal static Action<string, Creature, string>? Observer;
    internal static int Active { get; private set; }
    internal static int Peak { get; private set; }
    internal static int Dropped { get; private set; }
    internal static void Enter() { Active++; Peak = Math.Max(Peak, Active); }
    internal static void Exit() => Active = Math.Max(0, Active - 1);
    internal static void ResetCounters() { Peak = Active; Dropped = 0; }
    internal static bool Available(Player? owner, bool orb = false)
    {
        var p = LibrarianPreferences050.Current;
        return (orb ? p.OrbEffects : p.CardEffects) && !TestMode.IsOn && DisplayServer.GetName() != "headless"
            && SaveManager.Instance.PrefsSave.FastMode != FastModeType.Instant
            && CombatManager.Instance.IsInProgress && !CombatManager.Instance.IsOverOrEnding
            && (p.TeammateEffects || owner is null || MegaCrit.Sts2.Core.Context.LocalContext.IsMe(owner));
    }
    internal static void Cast(CardPlay play)
    {
        if (TryProfile(play.Card, out var profile))
            Emit(play.Card.Owner.Creature, profile, "cast", play.Card.GetType().Name, radius: 28);
    }
    internal static void Impact(CardModel source, Creature target)
    {
        if (TryProfile(source, out var profile))
            Emit(target, profile, "impact", source.GetType().Name, source.Owner);
    }
    internal static void Block(Creature target, CardModel? source)
    {
        if (source is not null && TryProfile(source, out var profile))
            Emit(target, profile with { Shape = SpellShape050.Seal }, "block", source.GetType().Name, source.Owner);
    }
    internal static void AllyBlock(Creature target, Player owner, SpellElement050 element, string source)
        => Emit(target, new(element, SpellShape050.Seal), "block", source, owner);
    internal static void Status(PowerModel power, CardModel? source)
    {
        if (source is not null && TryProfile(source, out var profile))
            Emit(power.Owner, profile with { Shape = SpellShape050.Ring }, "status", source.GetType().Name, source.Owner, 36);
    }
    internal static void PowerTriggered(PowerModel power)
    {
        if (power is Librarian.LibrarianCode.Powers.Implemented.OverlimitFormPower && power.Owner.Player is { } player)
            LibrarianOrbVfx.Safely(() =>
            {
                if (!Available(player)) return;
                var circle = LibrarianOrbPanel.GetDisplay(LibrarianRuntime.Get(player))?.MagicCircle;
                if (circle is not null) { circle.OverlimitActive = true; circle.BurstOverlimit(); }
            });
        string name = power.GetType().Name.Replace("Power", "");
        name = name switch { "SeedburialPending" => "SeedburialStrike", "DeepSeaPending" => "DeepSeaBarrier", "LifeSymphonyPending" => "LifeSymphony", "CrowdKindling" => "MultiplayerPlaceholderA", "Binding" => "MultiplayerPlaceholderB", _ => name };
        if (Profiles.TryGetValue(name, out var profile))
            Emit(power.Owner, profile with { Shape = SpellShape050.Ring }, "trigger", name, radius: 33);
    }
    internal static void Page(Player player, string stage, string source = "native")
    {
        if (player.Character is LibrarianCharacter)
        {
            Emit(player.Creature, new(SpellElement050.Page, SpellShape050.Pages), stage, source, radius: 33);
            if (stage is "bottom-read" or "bottom-return") LibrarianOrbVfx.Safely(() =>
            {
                if (!Available(player) || !MegaCrit.Sts2.Core.Context.LocalContext.IsMe(player)) return;
                var pile = NCombatRoom.Instance?.Ui.DrawPile;
                if (pile is null || !pile.IsVisibleInTree()) return;
                Spawn(player.Creature, new(SpellElement050.Page, SpellShape050.Pages), stage, source, player, 20,
                    pile.GetGlobalTransform() * (pile.Size / 2) + new Vector2(0,-20), false);
            });
        }
    }
    internal static void OrbChanged(LibrarianSession session, OrbOperationResult result)
    {
        if (result.Status != OrbOperationStatus.Applied) return;
        LibrarianOrbVfx.Safely(() =>
        {
            if (!Available(session.Player, true)) return;
            var display = LibrarianOrbPanel.GetDisplay(session);
            // The orb owns its feedback below the caption; combat-wide rings used to obscure numbers.
            display?.RefreshState();
        });
    }
    internal static void Emit(Creature target, SpellProfile050 profile, string stage, string source, Player? owner = null, float radius = 65)
        => LibrarianOrbVfx.Safely(() =>
        {
            owner ??= target.Player;
            if (!Available(owner)) return;
            var room = NCombatRoom.Instance;
            var node = room?.GetCreatureNode(target);
            if (node?.Hitbox is not { } box || !GodotObject.IsInstanceValid(box)) return;
            // Keep death-blow hits; status/cast success is never shown on a dead recipient.
            if (target.IsDead && stage != "impact") return;
            Vector2 point = box.GetGlobalTransform() * (box.Size * new Vector2(0.5f, 0.46f));
            if (stage == "cast") point += box.GetGlobalTransform().BasisXform(new Vector2(-box.Size.X * 0.22f, 8));
            float scale = box.GetGlobalTransform().Scale.Abs().X;
            Spawn(target, profile, stage, source, owner, Math.Clamp(radius * scale, 16, 95), point, false);
        });
    private static void Spawn(Creature target, SpellProfile050 profile, string stage, string source, Player? owner, float radius, Vector2 point, bool orb)
    {
        var room = NCombatRoom.Instance;
        if (room is null || !room.IsInsideTree()) return;
        if (Active >= LibrarianPreferences050.Current.EffectLimit) { Dropped++; return; }
        var layer = room.CombatVfxContainer;
        var effect = new LibrarianCardSpell050
        {
            Name = "LibrarianCardVfx050", Profile = profile, Stage = stage, Orb = orb, EffectOwner = owner,
            Radius = radius / Math.Max(0.01f, layer.GetGlobalTransform().Scale.Abs().X),
            Overlimit = source == "OverlimitForm",
            Duration = SaveManager.Instance.PrefsSave.FastMode == FastModeType.Fast ? 0.28f : 0.52f
        };
        layer.AddChild(effect);
        effect.GlobalPosition = point;
        if (owner is not null && target != owner.Creature && target.IsPlayer && room.GetCreatureNode(owner.Creature)?.Hitbox is { } sourceBox)
            effect.LinkStart = effect.ToLocal(sourceBox.GetGlobalTransform() * (sourceBox.Size / 2));
        Observer?.Invoke(stage, target, source);
    }
}

/// <summary>Small deterministic paper/element primitives, bounded lifetime and draw count.</summary>
public partial class LibrarianCardSpell050 : Node2D
{
    internal SpellProfile050 Profile { get; init; } = new(SpellElement050.Page, SpellShape050.Pages);
    internal string Stage { get; init; } = "impact";
    internal bool Overlimit { get; init; }
    private Texture2D? _overlimitSigil;
    internal Player? EffectOwner { get; init; }
    internal bool Orb { get; init; }
    internal float Radius { get; init; } = 60;
    internal float Duration { get; init; } = 0.52f;
    internal float Age { get; private set; }
    internal Vector2? LinkStart { get; set; }
    public override void _EnterTree() => LibrarianCardVfx050.Enter();
    public override void _ExitTree() => LibrarianCardVfx050.Exit();
    public override void _Process(double delta)
    {
        Age += (float)delta;
        if (Age >= Duration || !LibrarianCardVfx050.Available(EffectOwner, Orb)) { QueueFree(); return; }
        QueueRedraw();
    }
    public override void _Draw()
    {
        var preferences = LibrarianPreferences050.Current;
        float t = Math.Clamp(Age / Duration, 0, 1);
        float fade = Mathf.Sin(Mathf.Pi * t) * preferences.EffectOpacity / 100f;
        float progress = preferences.ReducedMotion ? 0.6f : 1 - Mathf.Pow(1 - t, 3);
        Color color = LibrarianCardVfx050.Tint(Profile.Element); color.A = fade;
        Color light = color.Lightened(0.45f); light.A = fade * 0.65f;
        float r = Radius;
        if (Overlimit)
        {
            _overlimitSigil ??= GD.Load<Texture2D>("res://Librarian/images/vfx/v1.0-beta2/overlimit_sigil.svg");
            float size = r * (2.1f + progress * 0.35f);
            DrawTextureRect(_overlimitSigil, new Rect2(new Vector2(-size / 2, -size / 2), new Vector2(size, size)), false, new Color(1, 1, 1, fade));
            return;
        }
        if (LinkStart is { } origin && !preferences.ReducedMotion)
        {
            var points = new Vector2[18];
            for (int i = 0; i < points.Length; i++)
            { float p = i / 17f; points[i] = origin.Lerp(Vector2.Zero, p) + new Vector2(0, -Mathf.Sin(p * Mathf.Pi) * 25); }
            DrawPolyline(points, new Color(color, fade * 0.24f), 1.5f, true);
            Paper(origin.Lerp(Vector2.Zero, progress) + new Vector2(0, -Mathf.Sin(progress * Mathf.Pi) * 25), 5, 0.2f, color);
        }
        if (Stage is "cast" or "status" or "trigger" or "orb")
        {
            DrawArc(Vector2.Zero, r * (0.78f + progress * 0.22f), -2.8f, 0.1f, 24, color, 1.8f, true);
            DrawArc(Vector2.Zero, r * 0.83f, 0.35f, 2.65f, 24, light, 1, true);
            int count = Stage == "cast" ? 3 : 5;
            for (int i = 0; i < count; i++)
            {
                var point = Vector2.FromAngle(i * Mathf.Tau / count - 0.5f) * r;
                if (Profile.Element == SpellElement050.Page) Paper(point, 4, i * 0.3f, color);
                else if (Profile.Element == SpellElement050.Water) DrawArc(point, 4, -2.6f, 0.6f, 12, color, 1.5f, true);
                else if (Profile.Element == SpellElement050.Fire) DrawColoredPolygon([point+new Vector2(0,-6),point+new Vector2(3,3),point+new Vector2(-3,3)],color);
                else Glyph(point, 4, i * 0.6f, Profile.Element==SpellElement050.Prismatic ? new Color(LibrarianCardVfx050.Tint((SpellElement050)(1+i%3)),fade) : color);
            }
            return;
        }
        if (Stage == "block")
        {
            Vector2[] shield = [new(-r * 0.65f, -r * 0.52f), new(0, -r * 0.72f), new(r * 0.65f, -r * 0.52f), new(r * 0.48f, r * 0.3f), new(0, r * 0.73f), new(-r * 0.48f, r * 0.3f)];
            DrawColoredPolygon(shield, new Color(color, fade * 0.10f));
            DrawPolyline([.. shield, shield[0]], color, 2, true);
            DrawLine(new(0, -r * 0.4f), new(0, r * 0.48f), light, 1, true);
            if (Profile.Element == SpellElement050.Leaf)
                for (int i = 0; i < 4; i++) Glyph(new((i % 2 == 0 ? -1 : 1) * r * 0.5f, (i / 2f - 0.4f) * r * 0.5f), 8, i, color);
            return;
        }
        if (Stage is "draw" or "bottom-read" or "bottom-return" or "generated" or "exhaust")
        {
            float sign = Stage == "bottom-return" ? -1 : 1;
            for (int i = 0; i < 3; i++)
                Paper(new((i - 1) * 15 + sign * (progress - 0.5f) * 30, -progress * 22 + i * 6), 7, (i - 1) * 0.18f, color);
            return;
        }
        switch (Profile.Shape)
        {
            case SpellShape050.Needle:
                for (int i = 0; i < 3; i++) { float y = (i - 1) * 13; DrawLine(new(-r, y + 16), new(r * progress, y - 16), color, 1.8f, true); }
                break;
            case SpellShape050.Root:
                for (int branch = 0; branch < 4; branch++)
                {
                    var points = new Vector2[13];
                    for (int i = 0; i < points.Length; i++) { float p = i / 12f * progress; points[i] = new((branch - 1.5f) * r * 0.32f + Mathf.Sin(p * 7 + branch) * r * 0.16f, r * 0.65f - p * r * 1.35f); }
                    DrawPolyline(points, color, 2.5f, true);
                    Glyph(points[9], 7, branch, light);
                }
                break;
            case SpellShape050.Ripple:
                for (int i = 0; i < 3; i++)
                { DrawSetTransform(new(0, i * 9), 0, new(1, 0.38f)); DrawArc(Vector2.Zero, r * (0.45f + i * 0.19f + progress * 0.18f), 0.1f, 5.9f, 40, color, 2, true); }
                DrawSetTransform(Vector2.Zero);
                break;
            case SpellShape050.Steam:
                for (int i = 0; i < 5; i++)
                { var p = new Vector2((i - 2) * r * 0.3f, -progress * r * 0.5f + (i % 2) * 10); DrawArc(p, r * (0.18f + progress * 0.16f), -2.8f, 0.4f, 20, new Color(light, fade * 0.6f), 5, true); }
                break;
            case SpellShape050.Stone:
                for (int i = 0; i < 7; i++) Glyph(Vector2.FromAngle(i * 2.4f) * r * progress * 0.7f, 5 + i % 3 * 2, i, color);
                break;
            case SpellShape050.Seal:
                Vector2[] diamond = [new(0, -r * 0.7f), new(r * 0.5f, 0), new(0, r * 0.7f), new(-r * 0.5f, 0), new(0, -r * 0.7f)];
                DrawPolyline(diamond, color, 2, true); DrawLine(diamond[0], diamond[2], light, 1.5f, true);
                break;
            case SpellShape050.Pages:
                for (int i = 0; i < 5; i++) Paper(new((i - 2) * r * 0.25f * progress, -Mathf.Sin(i + 1) * r * 0.3f), 8, (i - 2) * 0.28f, color);
                break;
            case SpellShape050.Ring:
                for (int i = 0; i < 3; i++) DrawArc(Vector2.Zero, r * (0.45f + i * 0.18f + progress * 0.1f), i * 2, i * 2 + 1.7f, 24, color, 2, true);
                break;
            default:
                var slash = new Vector2[18];
                for (int i = 0; i < slash.Length; i++) { float p = i / 17f; slash[i] = new((p * 2 - 1) * r, Mathf.Sin(p * Mathf.Pi) * -r * 0.4f + (p - 0.5f) * r * 0.8f); }
                DrawPolyline(slash, new Color(color, fade * 0.18f), 10, true);
                DrawPolyline(slash, color, Profile.Shape == SpellShape050.Ember ? 4 : 2.5f, true);
                break;
        }
        if (preferences.Particles && !preferences.ReducedMotion)
            for (int i = 0; i < 6; i++)
            {
                var direction = Vector2.FromAngle(i * 2.4f);
                var p = direction * r * (0.25f + progress * 0.65f);
                if (Profile.Element == SpellElement050.Page) Paper(p, 3, i, light);
                else DrawLine(p, p + direction * (4 + (1 - t) * 6), light, 1.5f, true);
            }
    }
    private void Glyph(Vector2 p, float size, float angle, Color c)
    {
        Vector2 a = Vector2.FromAngle(angle) * size, b = a.Orthogonal() * 0.5f;
        DrawColoredPolygon([p - a, p + b, p + a, p - b], c);
    }
    private void Paper(Vector2 p, float size, float angle, Color c)
    {
        var a = Vector2.FromAngle(angle) * size; var b = a.Orthogonal() * 1.35f;
        Vector2[] page = [p - a - b, p + a - b, p + a + b, p - a + b, p - a - b];
        DrawColoredPolygon([page[0],page[1],page[2],page[3]], new Color(c,c.A*0.12f));
        DrawPolyline(page, c, 1.4f, true);
        DrawLine(p - a * 0.5f, p + a * 0.5f, new Color(c, c.A * 0.5f), 1, true);
    }
}
