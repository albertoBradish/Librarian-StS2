using System.Globalization;
using System.IO;
using System.Text.Json;
using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Settings;

namespace Librarian.Mechanics;

/// <summary>Native UI geometry and optional lock labels on a disposable audit fight. Not a network test.</summary>
internal static class DevelopmentRevision102VisualAudit
{
    private static IEnumerable<Node> Desc(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            yield return child;
            foreach (var descendant in Desc(child)) yield return descendant;
        }
    }

    internal static async Task Run(Player player)
    {
        int checks = 0;
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("102 visual: " + label);
            checks++;
            MainFile.Logger.Info("V102_VISUAL_CHECK_PASS " + label);
        }
        async Task Wait(double seconds = .15) => await NGame.Instance!.ToSignal(
            NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        string output = System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT")
            ?? @"D:\Slay The Spire_Mod Dev\.research\channels\stable\workspace\outputs\revision-v1.1.0-stable\screenshots";
        Directory.CreateDirectory(output);
        async Task Shot(string name)
        {
            await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
            Check(image.SavePng(Path.Combine(output, name + ".png")) == Error.Ok, "capture " + name);
        }
        Check(DisplayServer.GetName() != "headless", "native rendering enabled");
        var session = LibrarianRuntime.Get(player);
        var context = new ThrowingPlayerChoiceContext();
        Task Dispatch(OrbOperationResult result) => LibrarianRuntime.Dispatch(session, context, result);
        var actor = NCombatRoom.Instance!.GetCreatureNode(player.Creature)!;
        var healthBar = Desc(actor).OfType<NHealthBar>().Single();
        var scaleParent = healthBar.GetParent<Control>();
        Vector2 originalScale = scaleParent.Scale;
        var window = NGame.Instance!.GetWindow();
        Vector2I originalWindow = window.Size;
        string prefsPath = LibrarianPreferences050.FilePath;
        string? originalPrefs = File.Exists(prefsPath) ? File.ReadAllText(prefsPath) : null;
        RitsuModSettingsSubmenu? submenu = null;
        var measurements = new List<object>();
        string Rules() => JsonSerializer.Serialize(new
        {
            Orbs = session.Orbs.Snapshot(),
            session.Orbs.SettlementsThisTurn,
            session.Orbs.SettlementsThisCombat,
            Ledger = session.Orbs.BlockLedger.Snapshot(),
            session.Waves.Amount,
            session.Waves.Retained,
            session.Waves.RetentionFloor,
            session.Player.Creature.CurrentHp,
            session.Player.Creature.MaxHp,
            session.Player.Creature.Block
        });
        try
        {
            var binding = LibrarianPreferences050.Bind("locked_orb_display", p => p.LockedOrbDisplay,
                (p, value) => p.LockedOrbDisplay = value);
            Check(LibrarianPreferences050.CreateDefaults().LockedOrbDisplay == LibrarianLockedOrbDisplayMode.NegativeTurns,
                "locked display new-profile default negative turns");
            Check(LibrarianPreferences050.Deserialize("{}").LockedOrbDisplay == LibrarianLockedOrbDisplayMode.NegativeTurns,
                "locked display legacy-profile default negative turns");
            string originalRules = Rules();
            foreach (var mode in Enum.GetValues<LibrarianLockedOrbDisplayMode>())
            {
                binding.Write(mode); binding.Save(); LibrarianPreferences050.Load();
                Check(binding.Read() == mode, "locked display persisted " + mode);
                Check(Rules() == originalRules, "preference persistence preserves combat state " + mode);
            }
            var opened = await ModSettingsNavigator.OpenByIdsAsync("Librarian", LibrarianSettings041.PageId,
                sectionId: "display", entryId: "locked_orb_display",
                options: new ModSettingsOpenOptions { Highlight = false, Focus = true });
            Check(opened.Success, "native display settings navigation to lock toggle");
            await Wait(.4);
            submenu = Desc(NGame.Instance).OfType<RitsuModSettingsSubmenu>().Last(n => n.IsVisibleInTree());
            string toggleLabel = LibrarianLanguage.Text("main_menu_ui", "LIBRARIAN_SETTINGS.locked_orb_display");
            Check(Desc(submenu).OfType<Label>().Any(n => n.IsVisibleInTree() && n.Text == toggleLabel)
                || Desc(submenu).OfType<RichTextLabel>().Any(n => n.IsVisibleInTree() && n.Text == toggleLabel),
                "localized locked-value setting row visible");
            await Shot("v102-settings-locked-values");
            Check(submenu.GetParent() is NSubmenuStack, "settings hosted on native submenu stack");
            ((NSubmenuStack)submenu.GetParent()).Pop();
            submenu = null;
            await Wait(.2);
            Check(Rules() == originalRules, "settings navigation preserves combat state");

            var prefs = LibrarianPreferences050.Current;
            prefs.WaveBar = true;
            prefs.LockedOrbDisplay = LibrarianLockedOrbDisplayMode.ValueAndTurns;
            prefs.ReducedMotion = true;
            Check(session.Waves.Amount == 0, "fresh fight starts without Waves");
            session.Waves.Add(55);
            await CreatureCmd.SetMaxAndCurrentHp(player.Creature, 79);
            player.Creature.LoseBlockInternal(player.Creature.Block);
            await CreatureCmd.GainBlock(player.Creature, 36, ValueProp.Unpowered, null);
            Check(player.Creature.Block == 36, "native command creates 36 Block");
            foreach (var (kind, value) in new[] { (OrbKind.Fire, 79), (OrbKind.Tide, 63), (OrbKind.Growth, 12) })
            {
                await Dispatch(session.Orbs.Unlock(kind));
                await Dispatch(session.Orbs.LoseAll(kind, OrbScope.All));
                await Dispatch(session.Orbs.Gain(kind, value));
            }
            await Dispatch(session.Orbs.Activate(OrbKind.Tide, OrbScope.All));
            await Dispatch(session.Orbs.Lock(OrbKind.Fire, 2));
            await Dispatch(session.Orbs.Lock(OrbKind.Tide, 100));
            await Dispatch(session.Orbs.Lock(OrbKind.Growth));
            LibrarianOrbPanel.Refresh(session);
            await Wait(.8);
            var display = LibrarianOrbPanel.GetDisplay(session)!;
            var hp = healthBar.GetNode<Control>("%HpForegroundContainer");
            var block = healthBar.GetNode<Control>("%BlockContainer");
            var hpContainer = healthBar.GetNode<Control>("HpBarContainer");
            var band = hpContainer.GetNode<LibrarianWaveBar>("LibrarianWaves");
            var fill = band.GetNode<NinePatchRect>("WaveFill");
            var amount = band.GetNode<Label>("WaveAmount");
            void Numbers(bool enabled, string label)
            {
                foreach (var orb in session.Orbs.Snapshot().Orbs)
                {
                    var slot = display.GetNode<Control>(orb.Kind.ToString());
                    var value = slot.GetNode<Label>("OrbValue");
                    var turns = slot.GetNode<Label>("LockTurns");
                    Check(value.Visible == (!orb.IsLocked || enabled), label + " raw value visibility " + orb.Kind);
                    Check(value.Text == (enabled ? orb.Value.ToString(CultureInfo.InvariantCulture) : OrbPresentation.CenterText(orb)),
                        label + " center text " + orb.Kind);
                    Check(turns.Visible == orb.IsLocked && turns.Text == OrbPresentation.CenterText(orb),
                        label + " lock text and visibility " + orb.Kind);
                    Check(turns.Position.IsEqualApprox(enabled ? new Vector2(0, 72) : Vector2.Zero)
                        && turns.Size.IsEqualApprox(enabled ? new Vector2(72, 24) : new Vector2(72, 72)),
                        label + " lock label bounds " + orb.Kind);
                    Check(turns.GetThemeFontSize("font_size") == (enabled ? 16 : value.GetThemeFontSize("font_size")),
                        label + " lock label font " + orb.Kind);
                }
            }
            void Geometry(string label)
            {
                var hpStart = hp.GetGlobalTransform() * Vector2.Zero;
                var hpEnd = hp.GetGlobalTransform() * new Vector2(hp.Size.X, 0);
                var waveStart = band.GetGlobalTransform() * Vector2.Zero;
                var waveEnd = band.GetGlobalTransform() * new Vector2(band.Size.X, 0);
                Check(band.IsVisibleInTree() && fill.IsVisibleInTree(), label + " wave band visible");
                Check(Math.Abs(waveStart.X - hpStart.X) < .1f, label + " wave starts at transformed native HP edge");
                Check(Math.Abs(waveEnd.X - hpEnd.X) < .1f, label + " wave full extent matches transformed HP width");
                Check(fill.Size.X <= band.Size.X + .01f, label + " fill remains within current HP width");
                Check(Math.Abs(fill.Size.X - band.Size.X * 55f / 79f) < .15f,
                    label + " fill uses full HP scale for 55 of 79");
                var toParent = hpContainer.GetGlobalTransform().AffineInverse();
                var amountOrigin = toParent * amount.GlobalPosition;
                var hpOrigin = toParent * hpStart;
                float expectedAmountX = block.IsVisibleInTree()
                    ? (toParent * (block.GetGlobalTransform() * new Vector2(block.Size.X, 0))).X - 9 : hpOrigin.X;
                Check(Math.Abs(amountOrigin.X - expectedAmountX) < .1f && amount.Text == "+55",
                    label + " amount follows visible shield or native HP edge");
                if (block.IsVisibleInTree())
                {
                    var blockRight = block.GetGlobalTransform() * new Vector2(block.Size.X, 0);
                    Check(waveStart.X <= blockRight.X + .1f, label + " wave overlaps shield without reserved gap");
                    Check(hpContainer.GetIndex() < block.GetIndex(), label + " native shield draws over wave overlap");
                }
                measurements.Add(new { label, window = new[] { window.Size.X, window.Size.Y },
                    scale = new[] { scaleParent.Scale.X, scaleParent.Scale.Y },
                    hpStartX = hpStart.X, hpEndX = hpEnd.X, waveStartX = waveStart.X, waveEndX = waveEnd.X,
                    fillWidth = fill.Size.X, fullWidth = band.Size.X, blockVisible = block.IsVisibleInTree() });
            }
            foreach (var resolution in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080), new Vector2I(1600, 900) })
            {
                window.Size = resolution;
                await Wait(.6);
                foreach (float scale in new[] { .75f, 1f, 1.25f })
                {
                    string label = resolution.X + "x" + resolution.Y + "-scale-" + scale.ToString(CultureInfo.InvariantCulture);
                    string before = Rules();
                    scaleParent.Scale = originalScale * scale;
                    await Wait(.8);
                    Geometry(label);
                    Numbers(true, label + " enabled");
                    if (scale == 1f) await Shot("v102-locked-values-on-" + resolution.X + "x" + resolution.Y);
                    if (resolution.X == 1920 && scale == 1.25f) await Shot("v102-wave-scaled-1920x1080-1.25");
                    Check(Rules() == before, label + " layout and read-only display preserve rules");
                    if (scale == 1f)
                    {
                        prefs.LockedOrbDisplay = LibrarianLockedOrbDisplayMode.LegacyTurns;
                        await Wait(.08);
                        Numbers(false, label + " disabled");
                        await Shot("v102-locked-values-off-" + resolution.X + "x" + resolution.Y);
                        prefs.LockedOrbDisplay = LibrarianLockedOrbDisplayMode.ValueAndTurns;
                        await Wait(.08);
                        Check(Rules() == before, label + " lock display toggle preserves all state");
                    }
                }
            }
            scaleParent.Scale = originalScale;
            window.Size = new(1280, 720);
            await Wait(.6);
            string waveRules = Rules();
            prefs.WaveBar = false; await Wait(.08);
            Check(!band.Visible, "wave setting immediately hides band");
            prefs.WaveBar = true; await Wait(.08);
            Check(band.Visible && Rules() == waveRules, "wave setting restores display without state changes");
            foreach (var kind in Enum.GetValues<OrbKind>()) await Dispatch(session.Orbs.LoseAll(kind, OrbScope.All));
            string zeroRules = Rules();
            await Wait(.7);
            Numbers(true, "locked zero values");
            Check(session.Orbs.Snapshot().Orbs.All(o => o.Value == 0 && o.IsLocked), "zero stored values retain their locks");
            await Shot("v102-locked-zero-values");
            prefs.LockedOrbDisplay = LibrarianLockedOrbDisplayMode.LegacyTurns; await Wait(.08); Numbers(false, "legacy locked zero values");
            prefs.LockedOrbDisplay = LibrarianLockedOrbDisplayMode.ValueAndTurns; await Wait(.08);
            Check(Rules() == zeroRules, "zero-value display toggles preserve rules");
            foreach (var kind in Enum.GetValues<OrbKind>()) await Dispatch(session.Orbs.Unlock(kind));
            string unlockedRules = Rules();
            await Wait(.08);
            Numbers(true, "unlocked zero values");
            prefs.LockedOrbDisplay = LibrarianLockedOrbDisplayMode.LegacyTurns; await Wait(.08); Numbers(false, "unlocked legacy values");
            Check(Rules() == unlockedRules, "unlock display toggle preserves rules");

            player.Creature.LoseBlockInternal(player.Creature.Block);
            healthBar.RefreshValues();
            await Wait(.7);
            Check(player.Creature.Block == 0 && !block.IsVisibleInTree(), "native zero Block hides shield");
            Vector2 staleBlockPosition = block.Position;
            block.Position += new Vector2(137, -40);
            try
            {
                string noBlockRules = Rules();
                await Wait(.08);
                Geometry("zero Block with stale hidden shield position");
                Check(Rules() == noBlockRules, "hidden shield positioning preserves combat state");
                await Shot("v102-wave-without-native-block");
            }
            finally { block.Position = staleBlockPosition; }
            File.WriteAllText(Path.Combine(output, "v102-visual-geometry.json"),
                JsonSerializer.Serialize(new { simulatedNativeParentScales = new[] { .75f, 1f, 1.25f },
                    realMulticlient = false, reducedMotion = true, measurements }, new JsonSerializerOptions { WriteIndented = true }));
            MainFile.Logger.Info("V102_VISUAL_AUDIT_PASS checks=" + checks
                + " resolutions=1280x720,1920x1080,1600x900 parentScales=0.75,1,1.25 realMulticlient=False");
        }
        finally
        {
            if (submenu is not null && GodotObject.IsInstanceValid(submenu)
                && submenu.GetParent() is NSubmenuStack stack && ReferenceEquals(stack.Peek(), submenu)) stack.Pop();
            if (GodotObject.IsInstanceValid(scaleParent)) scaleParent.Scale = originalScale;
            window.Size = originalWindow;
            if (originalPrefs is null) File.Delete(prefsPath); else File.WriteAllText(prefsPath, originalPrefs);
            LibrarianPreferences050.Load();
        }
    }
}
