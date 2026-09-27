using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Powers;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.Combat;
using System.Text.Json;
using STS2RitsuLib.Settings;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace Librarian.Mechanics;

internal static class DevelopmentRevision060Audit
{
    internal static async Task Settings()
    {
        var binding = LibrarianPreferences050.Bind("tide_block_feedback", p => p.TideBlockFeedback, (p,v) => p.TideBlockFeedback = v, false);
        string path = LibrarianPreferences050.FilePath;
        string? original = System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : null;
        try
        {
            if (new LibrarianPreferences050().TideBlockFeedback || JsonSerializer.Deserialize<LibrarianPreferences050>("{}")!.TideBlockFeedback)
                throw new InvalidOperationException("060 tide feedback must default off for new and legacy settings");
            foreach (bool value in new[] { true, false })
            {
                binding.Write(value); binding.Save(); LibrarianPreferences050.Load();
                if (binding.Read() != value) throw new InvalidOperationException("060 tide feedback setting persistence");
            }
            var opened = await ModSettingsNavigator.OpenByIdsAsync("Librarian", LibrarianSettings041.PageId, sectionId:"display",
                options:new ModSettingsOpenOptions { Highlight=false, Focus=true });
            if (!opened.Success) throw new InvalidOperationException("060 display settings navigation");
            await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(.4), SceneTreeTimer.SignalName.Timeout);
            var submenu = NGame.Instance.FindChildren("*","",true,false).OfType<RitsuModSettingsSubmenu>().Last(n => n.Visible);
            string output = System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT")!;
            System.IO.Directory.CreateDirectory(output);
            await NGame.Instance.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
            if (image.SavePng(System.IO.Path.Combine(output,"settings-display.png")) != Error.Ok) throw new InvalidOperationException("060 settings capture");
            if (submenu.GetParent() is NSubmenuStack stack && ReferenceEquals(stack.Peek(),submenu)) stack.Pop();
            MainFile.Logger.Info("VISUAL060_SETTINGS_PASS defaultOff=True legacyDefaultOff=True persistedOnOff=True displayPage=True");
        }
        finally
        {
            if (original is null) System.IO.File.Delete(path); else System.IO.File.WriteAllText(path, original);
            LibrarianPreferences050.Load();
        }
    }

    internal static async Task Run(Player player)
    {
        int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException("060: " + label); checks++; MainFile.Logger.Info("VISUAL060_CHECK_PASS " + label); }
        async Task Wait(double seconds = .2) => await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        string output = System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT") ?? @"D:\Slay The Spire_Mod Dev\outputs\revision-v1.0.0-stable\audit-history\revision-v0.6.0\screenshots";
        System.IO.Directory.CreateDirectory(output);
        async Task Capture(string name)
        {
            await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
            Check(image.SavePng(System.IO.Path.Combine(output, name + ".png")) == Error.Ok, "capture " + name);
        }
        Check(DisplayServer.GetName() != "headless", "native rendering enabled");
        var powers = ModelDb.AllPowers.OfType<LibrarianPower>().Where(p => p.CustomPackedIconPath.Contains("/v0.6.0/")).ToArray();
        Check(powers.Length == 26, "26 current status mappings");
        foreach (var power in powers)
        {
            Check(power.Icon.ResourcePath == power.CustomPackedIconPath, "native icon route " + power.Id);
            Check(GD.Load<Texture2D>(power.CustomPackedIconPath).GetSize() == new Vector2(64,64), "small icon " + power.Id);
            Check(GD.Load<Texture2D>(power.CustomBigIconPath).GetSize() == new Vector2(256,256), "big icon " + power.Id);
            Check(power.BigIcon is not null && power.ResolvedBigIconPath == power.CustomBigIconPath, "native big route " + power.Id);
        }
        var session = LibrarianRuntime.Get(player);
        var context = new ThrowingPlayerChoiceContext();
        Task Dispatch(OrbOperationResult result) => LibrarianRuntime.Dispatch(session, context, result);
        var window = NGame.Instance!.GetWindow();
        var size = window.Size;
        var prefs = LibrarianPreferences050.Current;
        bool idle = prefs.OrbIdle, particles = prefs.Particles, reduced = prefs.ReducedMotion, effects = prefs.OrbEffects, tideFeedback = prefs.TideBlockFeedback;
        var fast = SaveManager.Instance.PrefsSave.FastMode;
        try
        {
            prefs.OrbIdle = prefs.Particles = prefs.OrbEffects = true; prefs.ReducedMotion = false;
            SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
            foreach (var kind in Enum.GetValues<OrbKind>()) await Dispatch(session.Orbs.Gain(kind, 12));
            LibrarianOrbPanel.Refresh(session);
            await Wait(.6);
            var display = LibrarianOrbPanel.GetDisplay(session)!;
            var tideLabel = display.GetNode<Label>("TideBlockFeedback");
            prefs.TideBlockFeedback = false;
            display.ShowTideChange(7, false); await Wait(.05);
            Check(!tideLabel.Visible, "tide block popup disabled by default");
            prefs.TideBlockFeedback = true;
            display.ShowTideChange(7, false); await Wait(.05);
            Check(tideLabel.Visible && tideLabel.Text.EndsWith("+7"), "tide block popup opt-in gain");
            await Capture("tide-feedback-enabled");
            display.ShowTideChange(7, true); await Wait(.05);
            Check(tideLabel.Visible && tideLabel.Text.EndsWith("−7"), "tide block popup opt-in expiry");
            prefs.TideBlockFeedback = false; await Wait(.05);
            Check(!tideLabel.Visible, "tide feedback switch hides active popup immediately");
            foreach (var resolution in new[] { new Vector2I(1280,720), new Vector2I(1920,1080) })
            {
                window.Size = resolution; await Wait(.6);
                string suffix = resolution.X + "x" + resolution.Y;
                foreach (var kind in Enum.GetValues<OrbKind>())
                {
                    var slot = display.GetNode<Control>(kind.ToString());
                    var sprite = slot.GetChildren().OfType<TextureRect>().First();
                    var label = slot.GetChildren().OfType<Label>().First();
                    var anchor = sprite.Position;
                    Check(slot.GetChildren().OfType<LibrarianOrbIdleAura>().Count() == 2, "rear/front layers " + kind);
                    await Wait(.15);
                    Check(sprite.Position.IsEqualApprox(anchor) && label.Position == Vector2.Zero, "fixed numeric core " + kind);
                    await Dispatch(session.Orbs.Gain(kind, 3)); await Wait(.10);
                    Check(label.Scale.X > 1, "gain numeral animation " + kind);
                    await Capture("gain-" + kind + "-" + suffix);
                    await Wait(.7);
                    await Dispatch(session.Orbs.Lose(kind, 2, OrbScope.All)); await Wait(.1);
                    Check(label.Scale.X > 1, "loss numeral animation " + kind);
                    await Capture("loss-" + kind + "-" + suffix);
                    await Wait(.7);
                    display.Pulse(kind); await Wait(.1);
                    Check(label.Scale.X > 1, "settlement numeral animation " + kind);
                    Check(!slot.GetChildren().OfType<Label>().Last().Visible && slot.GetChildren().OfType<Label>().Last().Text == "", "settlement feedback has no words " + kind);
                    Check(slot.Scale.X <= LibrarianOrbDisplay.ForegroundScale + .001f, "no whole-orb pulse " + kind);
                    await Capture("resolve-" + kind + "-" + suffix);
                    await Wait(.7);
                    Check(label.Scale.IsEqualApprox(Vector2.One), "numeral settles " + kind);
                }
                for (int frame = 0; frame < 12; frame++) { await Capture("idle-" + suffix + "-" + frame.ToString("D2")); await Wait(.08); }
                await Dispatch(session.Orbs.Lock(OrbKind.Fire, 2));
                await Dispatch(session.Orbs.Lock(OrbKind.Tide, 100));
                await Dispatch(session.Orbs.Lock(OrbKind.Growth));
                await Wait(.7); await Capture("locked-" + suffix);
                var fireSlot = display.GetNode<Control>("Fire");
                var lockNumber = fireSlot.GetChildren().OfType<Label>().ElementAt(1);
                var lockImage = fireSlot.GetChildren().OfType<TextureRect>().Last();
                var lockGlow = fireSlot.GetNode<LibrarianOrbLockAura060>("LockGlow");
                Check(lockImage.Texture.ResourcePath == LibrarianOrbLockAura060.TexturePath, "selected B loaded " + suffix);
                Check(lockGlow.Visible && lockGlow.ParticlesVisible, "lock glow and particles enabled " + suffix);
                Check(lockGlow.GetIndex() < lockImage.GetIndex() && lockImage.GetIndex() < lockNumber.GetIndex(), "glow behind X behind counter " + suffix);
                foreach (var kind in Enum.GetValues<OrbKind>())
                {
                    var slot = display.GetNode<Control>(kind.ToString());
                    var normal = slot.GetChildren().OfType<Label>().First();
                    var counter = slot.GetChildren().OfType<Label>().ElementAt(1);
                    var image = slot.GetChildren().OfType<TextureRect>().Last();
                    Check(counter.Position == normal.Position && counter.Size == normal.Size && counter.PivotOffset == normal.PivotOffset
                        && counter.GetThemeFontSize("font_size") == normal.GetThemeFontSize("font_size"), "lock shares normal numeral bounds " + kind + " " + suffix);
                    Check(image.Position.IsEqualApprox(Vector2.Zero) && image.Size.IsEqualApprox(new Vector2(LibrarianOrbDisplay.OrbSize, LibrarianOrbDisplay.OrbSize)) && image.Material is ShaderMaterial,
                        "thorn overlay fits orb body " + kind + " " + suffix + " position=" + image.Position + " size=" + image.Size + " slot=" + slot.Size);
                    Check(slot.GetChildren().OfType<Label>().Last().Text == "", "lock entry has no words " + kind + " " + suffix);
                }
                var fixedIcon = lockImage.Position;
                var fixedCounter = lockNumber.Position;
                for (int frame = 0; frame < 12; frame++) { await Capture("lock-idle-" + suffix + "-" + frame.ToString("D2")); await Wait(.08); }
                Check(lockImage.Position == fixedIcon && lockNumber.Position == fixedCounter, "lock anchors stable " + suffix);
                await Dispatch(session.Orbs.Lock(OrbKind.Fire, 3)); await Wait(.1);
                Check(lockNumber.Text == "5" && lockNumber.Scale.X > 1, "lock stacking animates number " + suffix);
                await Capture("lock-increase-" + suffix); await Wait(.7);
                await Dispatch(session.Orbs.Gain(OrbKind.Fire, 2)); await Wait(.1);
                Check(lockNumber.Text == "5" && lockNumber.Scale.IsEqualApprox(Vector2.One), "stored orb gain does not animate unchanged lock count " + suffix);
                Check(fireSlot.GetChildren().OfType<Label>().Last().Text == "+2", "stored orb delta is numeric only " + suffix);
                await Wait(.7);
                int previousTurn = player.Creature.CombatState!.RoundNumber;
                CombatManager.Instance.SetReadyToEndTurn(player, false);
                for (int tick = 0; session.Orbs.LockedTurns(OrbKind.Fire) == 5; tick++)
                {
                    if (tick > 3600) throw new TimeoutException("060 native lock countdown");
                    await NGame.Instance.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                await Wait(.08);
                Check(lockNumber.Text == "4" && lockNumber.Scale.X > 1, "native owner-turn countdown animates " + suffix);
                Check(session.Orbs.LockedTurns(OrbKind.Growth) == OrbView.PermanentLock, "permanent lock unchanged at owner turn " + suffix);
                await Capture("lock-decrease-" + suffix);
                for (int tick = 0; player.Creature.CombatState.RoundNumber == previousTurn || player.PlayerCombatState!.Phase != PlayerTurnPhase.Play; tick++)
                {
                    if (tick > 3600) throw new TimeoutException("060 native turn completion");
                    await NGame.Instance.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                foreach (var kind in Enum.GetValues<OrbKind>()) await Dispatch(session.Orbs.Unlock(kind));
                await Wait(.1);
                Check(!lockGlow.Visible && !lockImage.Visible, "unlock hides X and particles " + suffix);
                Check(fireSlot.GetChildren().OfType<Label>().Last().Text == "" && !fireSlot.GetChildren().OfType<Label>().Last().Visible, "unlock feedback has no words " + suffix);
            }
            foreach (var mode in new[] { FastModeType.Normal, FastModeType.Fast, FastModeType.Instant })
            {
                SaveManager.Instance.PrefsSave.FastMode = mode;
                foreach (var kind in Enum.GetValues<OrbKind>())
                {
                    var enemy = player.Creature.CombatState!.HittableEnemies.First();
                    var travel = LibrarianOrbVfx.Travel(session, kind, kind == OrbKind.Fire ? enemy : player.Creature, kind == OrbKind.Growth ? OrbKind.Fire : null);
                    if (mode != FastModeType.Instant) { await Wait(.06); await Capture("travel-" + mode + "-" + kind); }
                    await travel; await Wait(.8);
                    Check(!NCombatRoom.Instance!.CombatVfxContainer.GetChildren().Any(n => n.Name == "LibrarianOrbSettlement" || n is LibrarianSpellEffect), "settlement cleanup " + mode + " " + kind);
                }
            }
            prefs.ReducedMotion = true;
            display.Pulse(OrbKind.Fire); await Wait(.1);
            var fire = display.GetNode<Control>("Fire");
            Check(fire.GetChildren().OfType<Label>().First().Scale.IsEqualApprox(Vector2.One), "reduced motion retains color-only feedback");
            Check(!fire.GetChildren().OfType<LibrarianOrbIdleAura>().Any(a => a.Visible), "reduced motion disables elemental idle");
            await Capture("reduced-motion");
            await Dispatch(session.Orbs.Lock(OrbKind.Fire, 2)); await Wait(.1);
            var reducedLockNumber = fire.GetChildren().OfType<Label>().ElementAt(1);
            var reducedGlow = fire.GetNode<LibrarianOrbLockAura060>("LockGlow");
            Check(reducedLockNumber.Scale.IsEqualApprox(Vector2.One) && !reducedGlow.ParticlesVisible, "lock reduced motion uses static glow and color-only numeral");
            await Capture("lock-reduced-motion");
            prefs.ReducedMotion = false;
            prefs.Particles = false; await Wait(.1);
            Check(reducedGlow.Visible && !reducedGlow.ParticlesVisible, "particle setting preserves lock glow");
            prefs.OrbIdle = false; await Wait(.1);
            float stoppedPhase = reducedGlow.Phase; await Wait(.2);
            Check(reducedGlow.Phase == stoppedPhase, "orb idle setting also freezes lock");
            prefs.OrbIdle = prefs.Particles = true;
            await Dispatch(session.Orbs.Unlock(OrbKind.Fire)); await Wait(.7);
            SaveManager.Instance.PrefsSave.FastMode = FastModeType.Normal;
            foreach (var kind in Enum.GetValues<OrbKind>())
            {
                int settlements = 0;
                await session.Orbs.SettleImmediatelyAsync(OrbSelector.Named(kind, OrbScope.All), 1,
                    async request => { await LibrarianRuntime.Settle(session, context, request); settlements++; },
                    "visual060-native-settlement");
                Check(settlements == 1, "native settlement dispatch " + kind);
            }
            await Wait(.8);
            Check(!NCombatRoom.Instance!.CombatVfxContainer.GetChildren().Any(n => n.Name == "LibrarianOrbSettlement" || n is LibrarianSpellEffect), "native settlement cleanup");
        }
        finally
        {
            window.Size = size;
            prefs.OrbIdle = idle; prefs.Particles = particles; prefs.ReducedMotion = reduced; prefs.OrbEffects = effects;
            prefs.TideBlockFeedback = tideFeedback;
            SaveManager.Instance.PrefsSave.FastMode = fast;
        }
        MainFile.Logger.Info("VISUAL060_AUDIT_PASS checks=" + checks + " resolutions=1280x720,1920x1080 liveMulticlient=False");
    }
}
