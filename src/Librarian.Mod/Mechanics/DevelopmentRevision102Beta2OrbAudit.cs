using System.Globalization;
using System.IO;
using System.Text.Json;
using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Saves;
using STS2RitsuLib.Settings;

namespace Librarian.Mechanics;

/// <summary>Three native lock display modes, migration, and the settings reset boundary.</summary>
internal static class DevelopmentRevision102Beta2OrbAudit
{
    private static IEnumerable<Node> Desc(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            yield return child;
            foreach (var descendant in Desc(child)) yield return descendant;
        }
    }
    private static string? ReadOptional(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

    internal static async Task Run(Player player)
    {
        int checks = 0;
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("102 beta2 orb: " + label);
            checks++;
            MainFile.Logger.Info("V102_BETA2_ORB_CHECK_PASS " + label);
        }
        async Task Wait(double seconds = .12) => await NGame.Instance!.ToSignal(
            NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        string output = System.Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT")
            ?? @"D:\Slay The Spire_Mod Dev\outputs\revision-v1.0.2-beta2\screenshots";
        Directory.CreateDirectory(output);
        async Task Shot(string name)
        {
            await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
            Check(image.SavePng(Path.Combine(output, name + ".png")) == Error.Ok, "capture " + name);
        }
        var session = LibrarianRuntime.Get(player);
        var context = new ThrowingPlayerChoiceContext();
        Task Dispatch(OrbOperationResult result) => LibrarianRuntime.Dispatch(session, context, result);
        string Rules() => JsonSerializer.Serialize(new { Orbs = session.Orbs.Snapshot(),
            session.Orbs.SettlementsThisTurn, session.Orbs.SettlementsThisCombat,
            Ledger = session.Orbs.BlockLedger.Snapshot(), session.Waves.Amount,
            session.Player.Creature.CurrentHp, session.Player.Creature.MaxHp, session.Player.Creature.Block });
        var window = NGame.Instance!.GetWindow();
        Vector2I originalWindow = window.Size;
        string originalLanguage = LibrarianLanguage.Selected;
        string? originalPrefs = ReadOptional(LibrarianPreferences050.FilePath);
        string? originalLanguageFile = ReadOptional(LibrarianLanguage.FilePath);
        RitsuModSettingsSubmenu? submenu = null;
        void CloseSettings()
        {
            if (submenu is not null && GodotObject.IsInstanceValid(submenu)
                && submenu.GetParent() is NSubmenuStack stack && ReferenceEquals(stack.Peek(), submenu)) stack.Pop();
            submenu = null;
        }
        try
        {
            Check(DisplayServer.GetName() != "headless", "native rendering enabled");
            foreach (var (json, expected) in new[]
            {
                ("{}", LibrarianLockedOrbDisplayMode.NegativeTurns),
                ("{\"LockedOrbValues\":false}", LibrarianLockedOrbDisplayMode.NegativeTurns),
                ("{\"LockedOrbValues\":true}", LibrarianLockedOrbDisplayMode.ValueAndTurns),
                ("{\"LockedOrbDisplay\":1,\"LockedOrbValues\":true}", LibrarianLockedOrbDisplayMode.LegacyTurns),
                ("{\"LockedOrbDisplay\":99,\"LockedOrbValues\":true}", LibrarianLockedOrbDisplayMode.NegativeTurns),
                ("{\"LockedOrbDisplay\":-1}", LibrarianLockedOrbDisplayMode.NegativeTurns),
                ("{\"LockedOrbDisplay\":\"invalid\"}", LibrarianLockedOrbDisplayMode.NegativeTurns),
                ("{\"LockedOrbDisplay\":null}", LibrarianLockedOrbDisplayMode.NegativeTurns),
                ("{\"LockedOrbDisplay\":{}}", LibrarianLockedOrbDisplayMode.NegativeTurns)
            })
                Check(LibrarianPreferences050.Deserialize(json).LockedOrbDisplay == expected, "migration " + json);
            var malformed = LibrarianPreferences050.Deserialize("{\"LockedOrbDisplay\":\"invalid\",\"CardEffects\":false,\"SoundVolume\":42}");
            Check(!malformed.CardEffects && malformed.SoundVolume == 42, "invalid mode preserves unrelated preferences");
            malformed.LockedOrbDisplay = (LibrarianLockedOrbDisplayMode)123;
            LibrarianPreferences050.Normalize(malformed);
            Check(malformed.LockedOrbDisplay == LibrarianLockedOrbDisplayMode.NegativeTurns, "invalid in-memory enum normalizes to default");
            File.WriteAllText(LibrarianPreferences050.FilePath, "{\"LockedOrbValues\":true,\"SoundVolume\":42}");
            LibrarianPreferences050.Load(); LibrarianPreferences050.Save();
            using (var saved = JsonDocument.Parse(File.ReadAllText(LibrarianPreferences050.FilePath)))
                Check(saved.RootElement.GetProperty("LockedOrbDisplay").GetInt32() == 2
                    && !saved.RootElement.TryGetProperty("LockedOrbValues", out _)
                    && saved.RootElement.GetProperty("SoundVolume").GetInt32() == 42,
                    "disk migration persists new mode and retires boolean");

            Check(ModSettingsRegistry.TryGetPage("Librarian", LibrarianSettings041.PageId, out var page), "settings page registered");
            var entry = page!.Sections.Single(s => s.Id == "display").Entries
                .OfType<ChoiceModSettingsEntryDefinition<LibrarianLockedOrbDisplayMode>>().Single(e => e.Id == "locked_orb_display");
            Check(entry.Presentation == ModSettingsChoicePresentation.Dropdown && entry.Options.Count == 3, "exactly three dropdown modes");
            foreach (var mode in Enum.GetValues<LibrarianLockedOrbDisplayMode>())
            {
                entry.Binding.Write(mode); entry.Binding.Save(); LibrarianPreferences050.Load();
                Check(entry.Binding.Read() == mode, "mode persists through reload " + mode);
            }
            foreach (string language in new[] { "zhs", "eng" })
            {
                LibrarianLanguage.Select(language);
                string before = Rules();
                var opened = await ModSettingsNavigator.OpenByIdsAsync("Librarian", LibrarianSettings041.PageId,
                    sectionId: "display", entryId: "locked_orb_display", options: new ModSettingsOpenOptions { Highlight = false, Focus = true });
                Check(opened.Success, "settings dropdown opens " + language);
                await Wait(.3);
                submenu = Desc(NGame.Instance).OfType<RitsuModSettingsSubmenu>().Last(n => n.IsVisibleInTree());
                var dropdown = Desc(submenu).OfType<ModSettingsDropdownChoiceControl<LibrarianLockedOrbDisplayMode>>().Single();
                foreach (var mode in Enum.GetValues<LibrarianLockedOrbDisplayMode>())
                {
                    dropdown.GetChildren().OfType<Button>().First().EmitSignal(BaseButton.SignalName.Pressed);
                    await Wait(.1);
                    string key = mode switch { LibrarianLockedOrbDisplayMode.LegacyTurns => "legacy", LibrarianLockedOrbDisplayMode.ValueAndTurns => "value", _ => "negative" };
                    string label = LibrarianLanguage.Text("main_menu_ui", "LIBRARIAN_SETTINGS.locked_orb_mode_" + key);
                    var option = Desc(NGame.Instance).OfType<ModSettingsMiniButton>()
                        .Single(b => b.IsVisibleInTree() && (b.Text == label || b.Text == "✓ " + label));
                    if (mode == LibrarianLockedOrbDisplayMode.NegativeTurns) await Shot("v102-beta2-orb-dropdown-" + language);
                    option.EmitSignal(BaseButton.SignalName.Pressed);
                    await Wait(.1);
                    Check(entry.Binding.Read() == mode && dropdown.GetChildren().OfType<Button>().First().Text == label,
                        "native dropdown choice and selected text " + language + " " + mode);
                }
                CloseSettings();
                await Wait(.1);
                Check(Rules() == before, "settings mode changes preserve combat rules " + language);
            }

            string defaultPrefs = JsonSerializer.Serialize(LibrarianPreferences050.CreateDefaults());
            var altered = LibrarianPreferences050.Current;
            foreach (var property in typeof(LibrarianPreferences050).GetProperties().Where(p => p.CanWrite))
            {
                if (property.PropertyType == typeof(bool)) property.SetValue(altered, !(bool)property.GetValue(altered)!);
                else if (property.PropertyType == typeof(int)) property.SetValue(altered, property.Name == "EffectLimit" ? 4 : 10);
                else if (property.PropertyType == typeof(LibrarianLockedOrbDisplayMode)) property.SetValue(altered, LibrarianLockedOrbDisplayMode.ValueAndTurns);
            }
            LibrarianPreferences050.Save();
            string expectedLanguage = LibrarianSettings041.DefaultLanguage;
            LibrarianLanguage.Select(expectedLanguage == "zhs" ? "eng" : "zhs");
            string progress = JsonSerializer.Serialize(SaveManager.Instance.Progress.ToSerializable());
            string? noticeHistory = ReadOptional(LibrarianNoticeHistory051.FilePath);
            string? reviewHistory = ReadOptional(LibrarianArchitectReviewHistory102.FilePath);
            string resetRules = Rules();
            var defaultsOpened = await ModSettingsNavigator.OpenByIdsAsync("Librarian", LibrarianSettings041.PageId,
                sectionId: "defaults", entryId: "reset_settings", options: new ModSettingsOpenOptions { Highlight = false, Focus = true });
            Check(defaultsOpened.Success, "default settings button reachable");
            await Wait(.3);
            submenu = Desc(NGame.Instance).OfType<RitsuModSettingsSubmenu>().Last(n => n.IsVisibleInTree());
            string restoreLabel = LibrarianLanguage.Text("main_menu_ui", "LIBRARIAN_SETTINGS.reset");
            Desc(submenu).OfType<Button>().Single(b => b.IsVisibleInTree() && b.Text == restoreLabel)
                .EmitSignal(BaseButton.SignalName.Pressed);
            await Wait(.3);
            Check(JsonSerializer.Serialize(LibrarianPreferences050.Current) == defaultPrefs, "native restore button resets every presentation preference");
            LibrarianPreferences050.Load();
            Check(JsonSerializer.Serialize(LibrarianPreferences050.Current) == defaultPrefs, "restored preferences persisted");
            Check(LibrarianLanguage.Selected == expectedLanguage, "restore matches mod language to game default");
            Check(JsonSerializer.Deserialize<LibrarianLanguage.Preference>(File.ReadAllText(LibrarianLanguage.FilePath))!.Language == expectedLanguage,
                "restored language persisted");
            Check(JsonSerializer.Serialize(SaveManager.Instance.Progress.ToSerializable()) == progress,
                "restore preserves all native progression and unlock data");
            Check(ReadOptional(LibrarianNoticeHistory051.FilePath) == noticeHistory
                && ReadOptional(LibrarianArchitectReviewHistory102.FilePath) == reviewHistory, "restore preserves independent notice and victory receipts");
            Check(Rules() == resetRules, "restore leaves combat state unchanged");
            var restoredDropdown = Desc(submenu).OfType<ModSettingsDropdownChoiceControl<LibrarianLockedOrbDisplayMode>>().Single();
            Check(restoredDropdown.GetChildren().OfType<Button>().First().Text
                == LibrarianLanguage.Text("main_menu_ui", "LIBRARIAN_SETTINGS.locked_orb_mode_negative"), "current settings display refreshes to red default mode");
            await Shot("v102-beta2-restored-default-settings");
            CloseSettings(); await Wait(.1);

            foreach (var (kind, value) in new[] { (OrbKind.Fire, 9), (OrbKind.Tide, 12), (OrbKind.Growth, 16) })
            {
                await Dispatch(session.Orbs.Unlock(kind));
                await Dispatch(session.Orbs.LoseAll(kind, OrbScope.All));
                await Dispatch(session.Orbs.Strengthen(kind, value, OrbScope.All));
            }
            await Dispatch(session.Orbs.Lock(OrbKind.Fire, 1));
            await Dispatch(session.Orbs.Lock(OrbKind.Tide, 100));
            await Dispatch(session.Orbs.Lock(OrbKind.Growth));
            LibrarianOrbPanel.Refresh(session); await Wait(.7);
            var display = LibrarianOrbPanel.GetDisplay(session)!;
            void Numbers(LibrarianLockedOrbDisplayMode mode, string label)
            {
                foreach (var orb in session.Orbs.Snapshot().Orbs)
                {
                    var slot = display.GetNode<Control>(orb.Kind.ToString());
                    var value = slot.GetNode<Label>("OrbValue");
                    var turns = slot.GetNode<Label>("LockTurns");
                    bool below = mode == LibrarianLockedOrbDisplayMode.ValueAndTurns;
                    bool negative = orb.IsLocked && mode == LibrarianLockedOrbDisplayMode.NegativeTurns;
                    string lockText = orb.IsPermanentlyLocked || orb.LockedTurns > 99 ? "∞" : orb.LockedTurns.ToString(CultureInfo.InvariantCulture);
                    if (negative) lockText = "−" + lockText;
                    Check(value.Visible == (!orb.IsLocked || below), label + " raw visibility " + orb.Kind);
                    Check(!orb.IsLocked ? value.Text == orb.Value.ToString(CultureInfo.InvariantCulture)
                        : below ? value.Text == orb.Value.ToString(CultureInfo.InvariantCulture) : turns.Text == lockText,
                        label + " selected mode text " + orb.Kind);
                    Check(turns.Visible == orb.IsLocked && turns.Position.IsEqualApprox(below ? new Vector2(0, 72) : Vector2.Zero),
                        label + " countdown visibility and anchor " + orb.Kind);
                    Check(turns.GetThemeColor("font_color").IsEqualApprox(negative ? new Color("ff6565") : new Color("eedfc5")),
                        label + " countdown color " + orb.Kind);
                    Check(value.GetThemeColor("font_color").IsEqualApprox(new Color("eedfc5")), label + " raw value keeps normal color " + orb.Kind);
                }
            }
            foreach (var resolution in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
            {
                window.Size = resolution; await Wait(.5);
                foreach (var mode in Enum.GetValues<LibrarianLockedOrbDisplayMode>())
                {
                    string before = Rules();
                    LibrarianPreferences050.Current.LockedOrbDisplay = mode; await Wait(.7);
                    Numbers(mode, resolution.X + "x" + resolution.Y + " " + mode);
                    Check(Rules() == before, "display mode switch preserves all rules " + mode);
                    await Shot("v102-beta2-orbs-" + resolution.X + "x" + resolution.Y + "-" + mode);
                }
            }
            LibrarianPreferences050.Current.LockedOrbDisplay = LibrarianLockedOrbDisplayMode.NegativeTurns;
            var fire = display.GetNode<Control>("Fire");
            var fireTurns = fire.GetNode<Label>("LockTurns");
            await Dispatch(session.Orbs.Lock(OrbKind.Fire, 1)); await Wait(.08);
            Check(fireTurns.Text == "−2" && fireTurns.Scale.X > 1
                && fireTurns.GetThemeColor("font_color").IsEqualApprox(new Color("ff6565")), "stacking animates while countdown stays red");
            await Wait(.7);
            int nativeRound = player.Creature.CombatState!.RoundNumber;
            CombatManager.Instance.SetReadyToEndTurn(player, false);
            for (int i = 0; session.Orbs.LockedTurns(OrbKind.Fire) == 2; i++)
            {
                if (i > 3600) throw new TimeoutException("102 beta2 native countdown");
                await NGame.Instance.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            await Wait(.08);
            Check(session.Orbs.LockedTurns(OrbKind.Fire) == 1 && fireTurns.Text == "−1" && fireTurns.Scale.X > 1,
                "native owner turn decreases lock to red negative one");
            Check(session.Orbs.LockedTurns(OrbKind.Tide) == 99 && display.GetNode<Label>("Tide/LockTurns").Text == "−99"
                && session.Orbs.LockedTurns(OrbKind.Growth) == OrbView.PermanentLock, "large finite lock resumes exact countdown and permanent lock stays permanent");
            for (int i = 0; player.Creature.CombatState.RoundNumber == nativeRound || player.PlayerCombatState!.Phase != PlayerTurnPhase.Play; i++)
            {
                if (i > 3600) throw new TimeoutException("102 beta2 native turn completion");
                await NGame.Instance.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            foreach (var kind in Enum.GetValues<OrbKind>()) await Dispatch(session.Orbs.LoseAll(kind, OrbScope.All));
            await Wait(.7); Numbers(LibrarianLockedOrbDisplayMode.NegativeTurns, "locked zero values");
            foreach (var kind in Enum.GetValues<OrbKind>()) await Dispatch(session.Orbs.Unlock(kind));
            await Wait(.7); Numbers(LibrarianLockedOrbDisplayMode.NegativeTurns, "unlocked zero values");
            Check(fire.GetNode<Label>("OrbValue").Text == "0"
                && fire.GetNode<Label>("OrbValue").GetThemeColor("font_color").IsEqualApprox(new Color("eedfc5")), "unlock restores normal zero value and color");
            MainFile.Logger.Info("V102_BETA2_ORB_AUDIT_PASS checks=" + checks + " modes=3 migration=True restoreDefaults=True nativeOwnerTurn=True");
        }
        finally
        {
            CloseSettings();
            window.Size = originalWindow;
            if (originalPrefs is null) File.Delete(LibrarianPreferences050.FilePath); else File.WriteAllText(LibrarianPreferences050.FilePath, originalPrefs);
            LibrarianPreferences050.Load();
            LibrarianLanguage.Select(originalLanguage);
            if (originalLanguageFile is null) File.Delete(LibrarianLanguage.FilePath); else File.WriteAllText(LibrarianLanguage.FilePath, originalLanguageFile);
        }
    }
}
