using System.IO;
using Godot;
using Librarian.Core;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using Librarian.LibrarianCode.Relics;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;

namespace Librarian.Mechanics;

/// <summary>Explicitly enabled screenshots of actual native scenes in the isolated runtime fixture.
/// Combat capture mutates disposable fixture orbs; call after gameplay assertions, before a fresh fight.</summary>
internal static class DevelopmentVisualAudit
{
    internal static bool Enabled => System.Environment.GetEnvironmentVariable("LIBRARIAN_VISUAL_AUDIT") == "1";

    private static void Require(bool value, string description)
    {
        if (!value) throw new InvalidOperationException("Visual audit: " + description);
        MainFile.Logger.Info("VISUAL_CHECK_PASS " + description);
    }

    private static string OutputDirectory()
    {
        Require(Enabled, "explicitly enabled");
        Require(OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase), "dedicated runtime profile");
        Require(DisplayServer.GetName() != "headless", "rendering display required");
        string? output = System.Environment.GetEnvironmentVariable("LIBRARIAN_VISUAL_OUTPUT");
        Require(!string.IsNullOrWhiteSpace(output) && Path.IsPathFullyQualified(output), "absolute screenshot output supplied");
        string path = Path.GetFullPath(output!);
        const string workspace = @"D:\Slay The Spire_Mod Dev\";
        Require(path.StartsWith(workspace + @".research\", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(workspace + @"outputs\", StringComparison.OrdinalIgnoreCase), "screenshots remain in workspace audit output");
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task Frames(int count = 2)
    {
        for (int i = 0; i < count; i++)
            await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static async Task Wait(double seconds)
        => await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private static async Task Capture(string output, string name)
    {
        await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = NGame.Instance.GetViewport().GetTexture().GetImage();
        Require(image is not null && !image.IsEmpty() && image.GetWidth() > 0, "viewport contains image " + name);
        string path = Path.Combine(output, name + ".png");
        Require(image!.SavePng(path) == Error.Ok, "screenshot saved " + name);
        MainFile.Logger.Info($"VISUAL_SCREENSHOT path={path} dimensions={image.GetWidth()}x{image.GetHeight()}");
    }

    private static IEnumerable<Node> Descendants(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    internal static async Task CaptureCharacterSelect(NMainMenu menu)
    {
        if (!Enabled) return;
        string output = OutputDirectory();
        Require(!menu.SubmenuStack.SubmenusOpen, "main menu stack empty before visual fixture");
        var screen = menu.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();
        screen.InitializeSingleplayer();
        menu.SubmenuStack.Push(screen);
        try
        {
            await Wait(0.8);
            var buttons = Descendants(screen).OfType<NCharacterSelectButton>().ToArray();
            var ironclad = buttons.Single(b => b.Character is Ironclad);
            var librarian = buttons.Single(b => b.Character is LibrarianCharacter);
            Require(!librarian.IsLocked, "Librarian selection unlocked");
            ironclad.Select();
            await Wait(0.8);
            var description = screen.GetNode<RichTextLabel>("InfoPanel/VBoxContainer/DescriptionLabel");
            var nativeFont = description.GetThemeFont("normal_font");
            int nativeSize = description.GetThemeFontSize("normal_font_size");
            await Capture(output, "01-native-ironclad-font");
            var settledInfoPosition = screen.GetNode<Control>("InfoPanel").Position;
            librarian.Select();
            var entrance = (Tween)HarmonyLib.AccessTools.Field(typeof(NCharacterSelectScreen), "_infoPanelTween").GetValue(screen)!;
            Require(entrance.IsValid(), "native information panel tween created");
            entrance.Pause();
            entrance.CustomStep(0.08);
            Require(screen.GetNode<Control>("InfoPanel").Position.X < settledInfoPosition.X - 1,
                "native information panel entrance animates after selection");
            entrance.Play();
            await Wait(0.8);
            Require(screen.GetNode<Control>("InfoPanel").Position.IsEqualApprox(settledInfoPosition),
                "native information panel settles without cumulative drift");
            Require(description.GetThemeFontSize("normal_font_size") == nativeSize, "native description font size preserved");
            Require(description.Scale.IsEqualApprox(Vector2.One), "description has no compensating scale");
            foreach (string line in description.GetParsedText().Split('\n'))
            {
                float nativeWidth = nativeFont.GetStringSize(line, fontSize: nativeSize).X;
                float actualWidth = description.GetThemeFont("normal_font").GetStringSize(line, fontSize: nativeSize).X;
                Require(actualWidth <= nativeWidth + 0.1f && actualWidth >= nativeWidth * 0.90f, "description restrained glyph spacing " + line);
            }
            Require(screen.GetNode<TextureRect>("InfoPanel/VBoxContainer/Relic/Icon").Texture.ResourcePath
                == "res://Librarian/images/relics/v0.4.0/tattered_scroll.png", "selection uses generated starter icon");
            Require(Descendants(screen.GetNode("AnimatedBg")).OfType<LibrarianCharacterSelect040>().Any(), "native selection displays approved continuous painting");
            await Capture(output, "02-librarian-character-select");
            if (System.Environment.GetEnvironmentVariable("LIBRARIAN_041_ONLY") == "1")
                await DevelopmentRevision041VisualAudit.RunSelection(screen);
            ironclad.Select();
            await Wait(0.8);
            Require(description.GetThemeFont("normal_font") == nativeFont, "switch to Ironclad restores original font resource");
            await Capture(output, "03-native-font-restored");
            librarian.Select();
            await Wait(0.8);
            await Capture(output, "04-librarian-return");
            Require(screen.GetNode("AnimatedBg").GetChildCount() == 1, "character switching releases old backgrounds");
            Require(screen.GetNode<Control>("InfoPanel").Position.IsEqualApprox(settledInfoPosition), "returning selection preserves native panel position");
            if (System.Environment.GetEnvironmentVariable("LIBRARIAN_041_ONLY") != "1"
                && System.Environment.GetEnvironmentVariable("LIBRARIAN_041_FOCUS") != "ritsu-full")
                await DevelopmentRevision040SelectionAudit.Capture(screen);
            var window = screen.GetWindow();
            var priorSize = window.Size;
            try
            {
                foreach (var resolution in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080) })
                {
                    window.Size = resolution;
                    await Wait(0.5);
                    await Capture(output, $"04-selection-{resolution.X}x{resolution.Y}");
                }
            }
            finally { window.Size = priorSize; await Wait(0.25); }
        }
        finally
        {
            if (menu.SubmenuStack.Peek() == screen) menu.SubmenuStack.Pop();
            await Frames();
        }
        Require(!menu.SubmenuStack.SubmenusOpen, "character fixture restores menu stack");
        MainFile.Logger.Info("CHARACTER_VISUAL_AUDIT_PASS screenshots=4 nativeSize=true restrainedSpacing=true nativeFontRestored=true");
    }

    internal static async Task CaptureCombat(LibrarianSession session, PlayerChoiceContext context, Creature enemy)
    {
        if (!Enabled) return;
        string output = OutputDirectory();
        Require(session.Player.Creature.Powers.Count == 0, "power-free combat visual fixture");
        Require(!enemy.IsDead, "live target available for projectile fixture");
        Require(Enum.GetValues<OrbKind>().All(k => !session.Orbs.IsLocked(k)), "fresh unlocked orbs required");
        var values = new Dictionary<OrbKind, int> { [OrbKind.Fire] = 13, [OrbKind.Tide] = 4, [OrbKind.Growth] = 7 };
        foreach (var (kind, value) in values)
        {
            await LibrarianRuntime.Dispatch(session, context, session.Orbs.LoseAll(kind, OrbScope.All));
            await LibrarianRuntime.Dispatch(session, context, session.Orbs.Strengthen(kind, value, OrbScope.All));
            await LibrarianRuntime.Dispatch(session, context, session.Orbs.ActivateWithoutSwitch(kind));
        }
        LibrarianOrbPanel.Refresh(session);
        await Wait(3.0); // Let native battle-start banner and relic flash finish before inspection.
        var display = LibrarianOrbPanel.GetDisplay(session)!;
        Require(display is not null && display.IsVisibleInTree(), "actual combat orb UI visible");
        ValidateOrbLayout(display!, session);
        await Capture(output, "05-combat-centered-values");
        await CaptureMapAndHud(session, output);
        var starter = session.Player.Relics.OfType<TatteredSpellScroll>().Single();
        Require(starter.Icon.GetWidth() <= 100 && starter.Icon.GetHeight() <= 100, "starter icon uses native particle-safe dimensions");
        starter.Flash();
        await Wait(0.15);
        var flashParticles = Descendants(NRun.Instance!.GlobalUi.AboveTopBarVfxContainer).OfType<GpuParticles2D>()
            .Where(p => p.Texture == starter.Icon).ToArray();
        Require(flashParticles.Length > 0, "actual native starter inventory flash emitted");
        Require(flashParticles.All(p => p.Texture.GetWidth() <= 100 && p.Texture.GetHeight() <= 100), "starter flash particles cannot inherit oversized source art");
        await Capture(output, "05-starter-native-trigger");
        await Wait(2.1);
        foreach (var target in new[] { OrbKind.Tide, OrbKind.Growth, OrbKind.Fire })
        {
            await LibrarianRuntime.Dispatch(session, context, session.Orbs.Activate(target, OrbScope.All));
            await Wait(0.5);
            ValidateOrbLayout(display!, session);
        }
        await LibrarianRuntime.Dispatch(session, context, session.Orbs.Strengthen(OrbKind.Fire, int.MaxValue - 13, OrbScope.All));
        await Frames();
        ValidateOrbLayout(display!, session);
        var longLabel = display!.GetNode<Control>("Fire").GetChildren().OfType<Label>().Single(l => l.Visible);
        Require(longLabel.Size.IsEqualApprox(new Vector2(72, 72)), "largest integer does not expand label");
        Require(longLabel.GetThemeFont("font").GetStringSize(longLabel.Text, fontSize: longLabel.GetThemeFontSize("font_size")).X <= 64,
            "largest integer fits without truncation");
        await Capture(output, "05-combat-large-value");
        await LibrarianRuntime.Dispatch(session, context, session.Orbs.LoseAll(OrbKind.Fire, OrbScope.All));
        await LibrarianRuntime.Dispatch(session, context, session.Orbs.Strengthen(OrbKind.Fire, 13, OrbScope.All));

        var prefs = SaveManager.Instance.PrefsSave;
        var originalFastMode = prefs.FastMode;
        try
        {
            foreach (var mode in new[] { FastModeType.Normal, FastModeType.Fast, FastModeType.Instant })
            {
                prefs.FastMode = mode;
                foreach (var kind in Enum.GetValues<OrbKind>())
                {
                    var target = kind == OrbKind.Fire ? enemy : session.Player.Creature;
                    Task traveling = LibrarianOrbVfx.Travel(session, kind, target, kind == OrbKind.Growth ? OrbKind.Fire : null);
                    if (mode != FastModeType.Instant)
                    {
                        // Travel creates its node synchronously before its first awaited timer.
                        Require(Descendants(NCombatRoom.Instance!).Any(n => n.Name.ToString().StartsWith("LibrarianOrbSettlement")), $"{mode} {kind} creates projectile");
                        if (mode == FastModeType.Normal)
                        {
                            await Wait(0.08);
                            await Capture(output, "06-projectile-" + kind.ToString().ToLowerInvariant());
                        }
                    }
                    await traveling;
                    await Frames();
                    Require(!Descendants(NCombatRoom.Instance!).Any(n => n.Name.ToString().StartsWith("LibrarianOrbSettlement")), $"{mode} {kind} cleans projectile");
                }
            }
        }
        finally { prefs.FastMode = originalFastMode; }
        Require(prefs.FastMode == originalFastMode, "fast-mode preference restored");
        // Growth travel returns before its receiving-orb pulse finishes. Inspect resting geometry.
        await Wait(0.4);

        await LibrarianRuntime.Dispatch(session, context, session.Orbs.Lock(OrbKind.Fire, 2));
        await LibrarianRuntime.Dispatch(session, context, session.Orbs.Lock(OrbKind.Tide, 100));
        await LibrarianRuntime.Dispatch(session, context, session.Orbs.Lock(OrbKind.Growth));
        await Frames();
        ValidateOrbLayout(display!, session);
        await Capture(output, "07-combat-lock-2-100-permanent");
        foreach (var kind in Enum.GetValues<OrbKind>())
        {
            var slot = display!.GetNode<Control>(kind.ToString());
            try
            {
                slot.EmitSignal(Control.SignalName.MouseEntered);
                await Wait(0.25);
                var hoverText = string.Join("\n", Descendants(NGame.Instance!).OfType<NHoverTipSet>()
                    .SelectMany(Descendants).OfType<RichTextLabel>().Where(n => n.IsVisibleInTree()).Select(n => n.GetParsedText()));
                Require(hoverText.Contains("原始数值：" + values[kind]), "hover reveals hidden value " + kind);
                string lockText = kind == OrbKind.Growth ? "永久锁定" : $"锁定剩余 {(kind == OrbKind.Fire ? 2 : 100)} 回合";
                Require(hoverText.Contains(lockText), "hover distinguishes finite/permanent lock " + kind);
                await Capture(output, "08-hover-" + kind.ToString().ToLowerInvariant());
            }
            finally { slot.EmitSignal(Control.SignalName.MouseExited); NHoverTipSet.Remove(slot); }
        }
        session.Orbs.BeginOwnerTurn();
        LibrarianOrbPanel.Refresh(session);
        await Frames();
        Require(session.Orbs.LockedTurns(OrbKind.Tide) == 99, "finite100 advances to99");
        ValidateOrbLayout(display!, session);
        await Capture(output, "09-combat-lock-1-99-permanent");
        MainFile.Logger.Info("COMBAT_VISUAL_AUDIT_PASS centeredValues=true locks=2,100,permanent hover=true vfx=normal,fast,instant cleaned=true");
    }

    private static async Task CaptureMapAndHud(LibrarianSession session, string output)
    {
        const string iconPath = "res://Librarian/images/charui/v0.3.9/character_icon.png";
        const string markerPath = "res://Librarian/images/charui/map_marker_librarian.png";
        var hudIcon = Descendants(NRun.Instance!.GlobalUi.TopBar).OfType<TextureRect>()
            .Single(t => t.Texture?.ResourcePath == iconPath);
        Require(hudIcon.IsVisibleInTree(), "new standalone HUD icon mounted");
        Require(hudIcon.Texture.GetWidth() == 88 && hudIcon.Texture.GetHeight() == 88, "HUD asset imported at native88px");
        Require(hudIcon.Size.X <= 120 && hudIcon.Size.Y <= 120, "HUD icon bounded within top bar");
        ValidateTransparentIcon(hudIcon.Texture, "HUD icon");
        var map = NMapScreen.Instance!;
        Require(!map.IsOpen, "map initially closed");
        var marker = map.GetNode<NMapMarker>("TheMap/MapMarker");
        bool markerWasVisible = marker.Visible;
        var originalPosition = marker.Position;
        var originalScale = marker.Scale;
        var originalCoord = session.Player.RunState.CurrentMapCoord;
        int originalVisited = session.Player.RunState.MapPointHistory.Sum(act => act.Count);
        try
        {
            map.Open(isOpenedFromTopBar: true);
            await Wait(1.2);
            Require(marker.Texture.ResourcePath == markerPath, "native map marker uses new character asset");
            int mapSize = System.Environment.GetEnvironmentVariable("LIBRARIAN_041_FOCUS") == "ritsu-full" ? 128 : 96;
            Require(marker.Texture.GetWidth() == mapSize && marker.Texture.GetHeight() == mapSize, "map asset matches current authored dimensions " + mapSize);
            ValidateTransparentIcon(marker.Texture, "map marker");
            if (!marker.Visible)
            {
                // The fixture enters combat directly and may have no visited point. Position only
                // the visual marker on an existing visible point; never invoke map travel.
                var point = Descendants(map).OfType<NMapPoint>().Where(map.IsNodeOnScreen)
                    .OrderBy(p => p.Point.coord.row).First();
                marker.SetMapPoint(point);
            }
            await Wait(1.1);
            Require(marker.IsVisibleInTree(), "actual native map marker visible");
            await Capture(output, "05-native-map-marker");
        }
        finally
        {
            map.Close(animateOut: false);
            marker.Visible = markerWasVisible;
            marker.Position = originalPosition;
            marker.Scale = originalScale;
        }
        await Frames();
        Require(!map.IsOpen && Equals(session.Player.RunState.CurrentMapCoord, originalCoord)
            && session.Player.RunState.MapPointHistory.Sum(act => act.Count) == originalVisited, "map preview closed without travel or run-state mutation");
    }

    private static void ValidateTransparentIcon(Texture2D texture, string name)
    {
        using var image = texture.GetImage();
        if (image.IsCompressed()) Require(image.Decompress() == Error.Ok, name + " readable alpha");
        var used = image.GetUsedRect();
        Require(used.Size.X > 0 && used.Size.Y > 0 && used.Size.X < image.GetWidth()
            && used.Size.Y < image.GetHeight(), name + " nonempty silhouette with transparent margins");
        Require(image.GetPixel(0, 0).A == 0 && image.GetPixel(image.GetWidth() - 1, image.GetHeight() - 1).A == 0,
            name + " clean transparent corners");
    }

    private static void ValidateOrbLayout(LibrarianOrbDisplay display, LibrarianSession session)
    {
        var snapshot = session.Orbs.Snapshot();
        var foreground = display.GetNode<Control>(snapshot.Orbs.Single(o => o.IsForeground).Kind.ToString());
        var rear = snapshot.Orbs.Where(o => !o.IsForeground).Select(o => display.GetNode<Control>(o.Kind.ToString())).OrderBy(c => c.Position.X).ToArray();
        Require(Math.Abs(foreground.Position.X + 36) < 0.1, "foreground centered below rear pair");
        Require(rear.All(c => Math.Abs(foreground.Position.Y - c.Position.Y - 104) < 0.1), "inverted triangle has104px row separation");
        Require(Math.Abs((rear[0].Position.X + rear[1].Position.X) / 2 - foreground.Position.X) < 0.1, "rear pair horizontally symmetric");
        foreach (var orb in snapshot.Orbs)
        {
            var slot = display.GetNode<Control>(orb.Kind.ToString());
            var textures = slot.GetChildren().OfType<TextureRect>().ToArray();
            Require(textures[0].Size.IsEqualApprox(new Vector2(72, 72)), "orb texture bounded to72px " + orb.Kind);
            Require(textures[0].Position.IsEqualApprox(LibrarianOrbDisplay.ArtworkOffset(orb.Kind)), "orb dark core optically centered with value " + orb.Kind);
            Require(textures[1].Size.IsEqualApprox(new Vector2(48, 54)), "lock texture bounded " + orb.Kind);
            Require(slot.Scale.IsEqualApprox(Vector2.One * (orb.IsForeground ? 1.05f : 0.88f)), "reduced foreground/rear size " + orb.Kind);
            var labels = slot.GetChildren().OfType<Label>().ToArray();
            Require(labels.Length == 2, "only value and lock labels " + orb.Kind);
            var shown = labels.Single(l => l.Visible);
            Require(shown.GetThemeConstant("outline_size") == 2, "thin readable numeral outline " + orb.Kind);
            Require(shown.Text == OrbPresentation.CenterText(orb), "correct center text " + orb.Kind);
            Require(shown.HorizontalAlignment == HorizontalAlignment.Center && shown.VerticalAlignment == VerticalAlignment.Center, "center-aligned label " + orb.Kind);
            Require(Math.Abs(shown.Position.X + shown.Size.X / 2 - slot.Size.X / 2) < 0.1, "label horizontally centered on orb " + orb.Kind);
            if (!orb.IsLocked) Require((shown.Position + shown.Size / 2).IsEqualApprox(slot.Size / 2), "raw value vertically centered " + orb.Kind);
            Require(slot.GetChildren().OfType<TextureRect>().Count(t => t.Visible) == (orb.IsLocked ? 2 : 1), "lock overlays orb artwork " + orb.Kind);
        }
    }
}
