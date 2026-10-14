using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.TestSupport;

namespace Librarian.Mechanics;

/// <summary>Consumes already selected targets only. Visual failures cannot fault the combat core.</summary>
internal static class LibrarianOrbVfx
{
    public static string TexturePath(OrbKind kind) => $"res://Librarian/images/orbs/{kind.ToString().ToLowerInvariant()}.png";

    public static void Safely(Action presentation)
    {
        try { presentation(); }
        catch (Exception e) { MainFile.Logger.Warn($"ORB_VFX_SKIPPED {e.GetType().Name}: {e.Message}"); }
    }

    public static async Task Travel(LibrarianSession session, OrbKind source, Creature? creature = null, OrbKind? receivingOrb = null, int settlementValue = 10)
    {
        Node2D? root = null;
        Tween? tween = null;
        bool selfOwnedImpact = false;
        using var cancellation = new System.Threading.CancellationTokenSource();
        try
        {
            if (!LibrarianCardVfx050.Available(session.Player, true)) return;
            var room = NCombatRoom.Instance;
            if (TestMode.IsOn || DisplayServer.GetName() == "headless" || CombatManager.Instance.IsOverOrEnding
                || session.Player.Creature.IsDead || room is null || !GodotObject.IsInstanceValid(room) || !room.IsInsideTree()
                || SaveManager.Instance.PrefsSave.FastMode == FastModeType.Instant) return;
            var display = LibrarianOrbPanel.GetDisplay(session);
            if (display is null || !display.TryGetOrbCenter(source, out var start)) return;
            Vector2 destination;
            if (receivingOrb is { } recipient)
            {
                if (!display.TryGetOrbCenter(recipient, out destination)) return;
            }
            else
            {
                if (creature is null || creature.IsDead) return;
                var hitbox = room.GetCreatureNode(creature)?.Hitbox;
                if (hitbox is null || !GodotObject.IsInstanceValid(hitbox)) return;
                destination = hitbox.GetGlobalTransform() * (hitbox.Size / 2);
            }
            float duration = SaveManager.Instance.PrefsSave.FastMode == FastModeType.Fast ? 0.12f : 0.3f;
            LibrarianFireballVfx? fireball = null;
            bool fireSuppressed = false;
            if (source == OrbKind.Fire && receivingOrb is null)
            {
                fireSuppressed = LibrarianCardVfx050.Active >= LibrarianPreferences050.Current.EffectLimit;
                if (fireSuppressed)
                {
                    // Keep the original wait/cancellation contract even when the visual
                    // budget is full. This invisible sentinel owns no drawn effect.
                    root = new Node2D { Name = "LibrarianOrbSettlementFireSuppressed", Visible = false };
                }
                else
                {
                    fireball = new LibrarianFireballVfx
                    {
                        Name = "LibrarianOrbSettlementFire", EffectOwner = session.Player, TravelDuration = duration,
                        SettlementValue = settlementValue,
                        Modulate = new Color(1, 1, 1, LibrarianPreferences050.Current.EffectOpacity / 100f)
                    };
                    root = fireball;
                }
            }
            else root = new Node2D { Name = "LibrarianOrbSettlement", Modulate = new Color(1, 1, 1, LibrarianPreferences050.Current.EffectOpacity / 100f) };
            room.CombatVfxContainer.AddChild(root);
            root.TreeExiting += cancellation.Cancel;
            root.GlobalPosition = start;
            root.Scale = Vector2.One / Math.Max(.01f, room.CombatVfxContainer.GetGlobalTransform().Scale.Abs().X);
            if (fireball is not null)
            {
                if (LibrarianSpineRuntime.TryGetSocket(session.Player.Creature, LibrarianSpineSocket.Book, out var socket))
                    root.GlobalPosition = socket.GlobalPosition;
                // Retain both endpoint snapshots. The ribbon and impact share one geometry/lifetime.
                fireball.CaptureDestination(destination);
                tween = root.CreateTween();
                tween.TweenMethod(Callable.From<float>(t =>
                {
                    if (GodotObject.IsInstanceValid(fireball)) fireball.Advance(t);
                }), 0f, 1f, duration).SetTrans(Tween.TransitionType.Linear);
            }
            else if (!fireSuppressed)
            {
                // Tide/growth retain their existing compact settlement packet.
                var packet = new LibrarianOrbPacket060 { Kind = source };
                root.AddChild(packet);
                var moving = root;
                tween = root.CreateTween();
                tween.TweenMethod(Callable.From<float>(t =>
                {
                    if (GodotObject.IsInstanceValid(moving))
                    {
                        moving.GlobalPosition = start.Lerp(destination, t) + new Vector2(0, -28 * Mathf.Sin(Mathf.Pi * t));
                        packet.Direction = (destination - start + new Vector2(0, -28 * Mathf.Pi * Mathf.Cos(Mathf.Pi * t))).Normalized();
                        packet.Progress = t;
                        packet.QueueRedraw();
                    }
                }), 0f, 1f, duration).SetTrans(Tween.TransitionType.Sine);
            }
            await Cmd.CustomScaledWait(0.12f, 0.3f, cancellationToken: cancellation.Token);
            if (CombatManager.Instance.IsOverOrEnding || !GodotObject.IsInstanceValid(room) || !room.IsInsideTree()) return;
            if (fireSuppressed) return;
            if (fireball is not null)
            {
                if (!GodotObject.IsInstanceValid(fireball) || fireball.IsQueuedForDeletion()
                    || creature is null || !creature.IsHittable || session.Player.Creature.IsDead) return;
                fireball.Impact();
                // The damage command still follows the original wait. Only the cosmetic
                // impact remains for .32 s and frees itself; finally must not erase it.
                selfOwnedImpact = true;
                return;
            }
            // One impact layer per settlement. Growth's impact belongs to the receiving orb.
            if (receivingOrb is { } targetOrb)
            {
                if (display.TryGetOrbCenter(targetOrb, out var actualTarget))
                    LibrarianSpellVisuals.SettlementAt(actualTarget, source, 32);
            }
            else if (creature is not null) LibrarianSpellVisuals.Settlement(creature, source);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { MainFile.Logger.Warn($"ORB_VFX_SKIPPED {e.GetType().Name}: {e.Message}"); }
        finally
        {
            if (tween is not null && GodotObject.IsInstanceValid(tween)) tween.Kill();
            if (root is not null && GodotObject.IsInstanceValid(root))
            {
                root.TreeExiting -= cancellation.Cancel;
                if (!selfOwnedImpact) root.QueueFree();
            }
        }
    }
}
