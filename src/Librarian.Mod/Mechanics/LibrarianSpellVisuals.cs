using Godot;
using Librarian.Core;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.TestSupport;

namespace Librarian.Mechanics;

/// <summary>Presentation receives actual results; it never chooses gameplay targets or consumes RNG.</summary>
internal static class LibrarianSpellVisuals
{
    private sealed class HitCooldown { internal ulong Last; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Creature, HitCooldown> ShieldHits = new();
    internal static void CardPlayed(Player player, CardType type) => LibrarianOrbVfx.Safely(() =>
    {
        if (TestMode.IsOn || DisplayServer.GetName() == "headless" || SaveManager.Instance.PrefsSave.FastMode == FastModeType.Instant) return;
        var creature = NCombatRoom.Instance?.GetCreatureNode(player.Creature);
        if (creature is null || player.Creature.IsDead) return;
        if (!LibrarianPreferences050.Current.ReducedMotion) FindMotion(creature)?.PlayCardGesture(type);
        if (type == CardType.Power && LibrarianRuntime.TryGet(player, out var session) && session is not null)
            LibrarianOrbPanel.GetDisplay(session)?.MagicCircle.CastRevolution();
        // Per-card preparation is supplied by LibrarianCardVfx050.
    });

    private static LibrarianCharacterMotion? FindMotion(Node root)
    {
        if (root is LibrarianCharacterMotion motion) return motion;
        foreach (var child in root.GetChildren()) if (FindMotion(child) is { } found) return found;
        return null;
    }

    // Attack cards already request the game's native hit effect. A second
    // detached hand pasted onto the target made the gesture look disconnected.
    internal static void AttackImpact(Creature target) { }
    internal static void PerfectBlock(Creature target, DamageResult result)
    {
        if (result.BlockedDamage > 0 && result.UnblockedDamage == 0 && result.WasFullyBlocked)
        {
            var cooldown = ShieldHits.GetValue(target, _ => new HitCooldown());
            ulong now = Time.GetTicksMsec();
            if (cooldown.Last != 0 && now - cooldown.Last < 120) return;
            cooldown.Last = now;
            AddEffect(target, LibrarianSpellEffect.Kind.ShieldHit);
        }
    }
    internal static void Settlement(Creature target, OrbKind kind) => AddEffect(target, kind switch
    {
        OrbKind.Fire => LibrarianSpellEffect.Kind.Fire,
        OrbKind.Tide => LibrarianSpellEffect.Kind.Shield,
        _ => LibrarianSpellEffect.Kind.Vine
    });

    internal static void SettlementAt(Vector2 point, OrbKind kind, float radius)
    {
        var room = NCombatRoom.Instance;
        if (room is null || !room.IsInsideTree()) return;
        if (LibrarianCardVfx050.Active >= LibrarianPreferences050.Current.EffectLimit) return;
        var layer = room.CombatVfxContainer;
        var effect = new LibrarianSpellEffect
        {
            Effect = kind == OrbKind.Fire ? LibrarianSpellEffect.Kind.Fire : kind == OrbKind.Tide ? LibrarianSpellEffect.Kind.Shield : LibrarianSpellEffect.Kind.Vine,
            Radius = radius / Math.Max(.01f, layer.GetGlobalTransform().Scale.Abs().X),
            Modulate = new Color(1, 1, 1, LibrarianPreferences050.Current.EffectOpacity / 100f)
        };
        layer.AddChild(effect);
        effect.GlobalPosition = point;
    }

    private static void AddEffect(Creature target, LibrarianSpellEffect.Kind kind) => LibrarianOrbVfx.Safely(() =>
    {
        if (TestMode.IsOn || DisplayServer.GetName() == "headless" || SaveManager.Instance.PrefsSave.FastMode == FastModeType.Instant) return;
        var room = NCombatRoom.Instance;
        var creature = room?.GetCreatureNode(target);
        if (room is null || creature?.Hitbox is not { } box
            || (target.IsDead && kind is not LibrarianSpellEffect.Kind.Hand and not LibrarianSpellEffect.Kind.Fire)) return;
        if (LibrarianCardVfx050.Active >= LibrarianPreferences050.Current.EffectLimit) return;
        var effect = new LibrarianSpellEffect { Effect = kind,
            Radius = Math.Min(box.Size.X, box.Size.Y) * 0.55f * box.GetGlobalTransform().Scale.Abs().X
                / Math.Max(.01f, room.CombatVfxContainer.GetGlobalTransform().Scale.Abs().X),
            Modulate = new Color(1, 1, 1, LibrarianPreferences050.Current.EffectOpacity / 100f) };
        room.CombatVfxContainer.AddChild(effect);
        effect.GlobalPosition = box.GetGlobalTransform() * (box.Size / 2);
    });
}

public partial class LibrarianSpellEffect : Node2D
{
    internal enum Kind { Fire, Shield, ShieldHit, Vine, Skill, Hand }
    internal Kind Effect { get; init; }
    internal float Radius { get; init; } = 80;
    private float _age;
    public override void _EnterTree() => LibrarianCardVfx050.Enter();
    public override void _ExitTree() => LibrarianCardVfx050.Exit();
    public override void _Process(double delta)
    {
        _age += (float)delta;
        if (_age >= 0.65f) { QueueFree(); return; }
        QueueRedraw();
    }
    public override void _Draw()
    {
        float age = _age / 0.65f;
        float fade = Mathf.Sin(age * Mathf.Pi);
        float t = LibrarianPreferences050.Current.ReducedMotion ? .5f : age;
        if (Effect is Kind.Shield or Kind.ShieldHit)
        {
            float strength = Effect == Kind.ShieldHit ? 0.8f : 0.45f;
            DrawArc(Vector2.Zero, Radius * (1 + t * 0.12f), 0, Mathf.Tau, 80, new Color(0.45f, 0.85f, 1, fade * strength), 3, true);
            DrawArc(Vector2.Zero, Radius * 0.94f, 0, Mathf.Tau, 80, new Color(0.65f, 0.95f, 1, fade * 0.2f), 8, true);
            if (Effect == Kind.ShieldHit)
            {
                for (int i = 0; i < 12; i++)
                {
                    var direction = Vector2.FromAngle(i * Mathf.Tau / 12);
                    var droplet = direction * Radius * (0.85f + t * 0.45f);
                    DrawLine(droplet, droplet + direction * (3 + 8 * (1 - t)),
                        new Color(0.65f, 0.95f, 1, fade * 0.8f), 2, true);
                }
            }
        }
        else if (Effect == Kind.Fire)
        {
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.Tau / 16;
                var position = Vector2.FromAngle(a) * (8 + t * Radius * (0.4f + i % 3 * 0.15f));
                DrawCircle(position, (1 - t) * (3 + i % 4), new Color(1, 0.36f + i % 3 * 0.12f, 0.12f, fade));
            }
        }
        else if (Effect == Kind.Vine)
        {
            for (int branch = 0; branch < 3; branch++)
            {
                var points = new Vector2[20];
                for (int i = 0; i < points.Length; i++)
                {
                    float p = i / 19f * Math.Min(1, t * 2);
                    points[i] = new Vector2((branch - 1) * Radius * 0.45f + Mathf.Sin(p * 8 + branch) * 12, Radius - p * Radius * 2);
                }
                DrawPolyline(points, new Color(0.4f, 0.85f, 0.35f, fade * 0.75f), 3, true);
                for (int i = 4; i < points.Length; i += 5)
                    DrawColoredPolygon([points[i], points[i] + new Vector2(13, -8), points[i] + new Vector2(5, 2)], new Color(0.65f, 0.92f, 0.42f, fade));
            }
        }
        else if (Effect == Kind.Skill)
        {
            for (int i = 0; i < 7; i++)
            {
                float x = (i - 3) * 16;
                var start = new Vector2(x, Radius * 0.5f - t * Radius);
                DrawLine(start, start + new Vector2(0, -25 - i % 3 * 10), new Color(0.72f, 0.91f, 1, fade * 0.8f), 2, true);
            }
        }
        else if (Effect == Kind.Hand)
        {
            const string path = "res://Librarian/images/character/v0.4.0/hand_right.png";
            if (ResourceLoader.Exists(path))
            {
                var texture = GD.Load<Texture2D>(path);
                DrawTextureRect(texture, new Rect2(new Vector2(-42, -42 - t * 18), new Vector2(84, 84)), false, new Color(1, 0.81f, 0.42f, fade * 0.7f));
            }
        }
    }
}
