using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using STS2RitsuLib;
using STS2RitsuLib.Settings;

namespace Librarian.Mechanics;

/// <summary>
/// Isolated checks for the v0.4.1 settings registration, progression actions,
/// and diagnostics export. This audit deliberately never calls SaveProgressFile
/// and refuses to run outside the revision fixture profile.
/// </summary>
internal static class DevelopmentRevision041UiAudit
{
    private static void Require(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("041 UI audit: " + name);
        Librarian.LibrarianCode.MainFile.Logger.Info("REVISION041_UI_PASS " + name);
    }

    private static string RequireIsolatedProfile()
    {
        string userData = OS.GetUserDataDir();
        Require(userData.Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase),
            "isolated revision fixture profile");
        return userData;
    }

    /// <summary>Runs only in the isolated game fixture; all filesystem writes are beneath user://.</summary>
    internal static async Task Run()
    {
        string userData = RequireIsolatedProfile();
        AuditSettingsRegistration();
        await AuditNativeSettingsViewAsync();
        AuditProgressIdempotence();
        await AuditExportAsync(userData);
        Librarian.LibrarianCode.MainFile.Logger.Info("REVISION041_UI_AUDIT_PASS");
    }

    private static void AuditSettingsRegistration()
    {
        Require(LibrarianSettings041.PageId == "librarian-settings", "stable settings page id");
        Require(typeof(LibrarianSettings041).GetMethod(nameof(LibrarianSettings041.Initialize),
            BindingFlags.Public | BindingFlags.Static) is not null, "public registration entry point");

        // Register twice: the second call must be a no-op and must not add a duplicate page.
        LibrarianSettings041.Initialize();
        LibrarianSettings041.Initialize();
        IReadOnlyList<ModSettingsPage> pages = RitsuLibFramework.GetRegisteredModSettings();
        Require(pages.Count(p => string.Equals(p.ModId, "Librarian", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.Id, LibrarianSettings041.PageId, StringComparison.OrdinalIgnoreCase)) == 1,
            "registry contains one Librarian settings page");
        Require(ModSettingsRegistry.TryGetPage("Librarian", LibrarianSettings041.PageId, out ModSettingsPage? page) &&
            page is not null, "registry resolves the Librarian page");
        string[] sections = page!.Sections.Select(section => section.Id).ToArray();
        Require(sections.SequenceEqual(new[] { "progression", "diagnostics" }), "settings section order");
        Require(page.Sections[0].Entries.Select(entry => entry.Id).SequenceEqual(
                new[] { "current", "progressive", "all" }) &&
            page.Sections[1].Entries.Select(entry => entry.Id).SequenceEqual(
                new[] { "export_scope", "export", "export_status" }),
            "settings entry order and stable IDs");
        FieldInfo? registered = typeof(LibrarianSettings041).GetField("_registered",
            BindingFlags.NonPublic | BindingFlags.Static);
        Require(registered?.GetValue(null) is true, "settings registration is idempotently complete");
        Require(typeof(LibrarianSettings041).GetMethod("ApplyProgress",
            BindingFlags.NonPublic | BindingFlags.Static) is not null, "progress action remains private to settings");
    }

    private static async Task AuditNativeSettingsViewAsync()
    {
        Require(DisplayServer.GetName() != "headless", "native settings view available");
        var result = await ModSettingsNavigator.OpenByIdsAsync("Librarian", LibrarianSettings041.PageId,
            sectionId: "diagnostics", entryId: "export",
            options: new ModSettingsOpenOptions { Highlight = false, Focus = true });
        Require(result.Success && string.Equals(result.PageId, LibrarianSettings041.PageId,
            StringComparison.OrdinalIgnoreCase) && result.SectionId == "diagnostics" && result.EntryId == "export",
            "native settings page and export entry open through RitsuLib navigation");

        async Task Frame()
        {
            await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
            await NGame.Instance.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }

        await Frame();
        await Frame();
        STS2RitsuLib.Settings.RitsuModSettingsSubmenu? submenu = Descendants(NGame.Instance!)
            .OfType<STS2RitsuLib.Settings.RitsuModSettingsSubmenu>().LastOrDefault(node => node.Visible);
        Require(submenu is not null, "native settings submenu is visible");
        var viewport = submenu!.GetViewport();
        Rect2 bounds = submenu.GetGlobalRect();
        Rect2 visible = viewport.GetVisibleRect();
        Require(bounds.Position.X >= visible.Position.X - 2 && bounds.Position.Y >= visible.Position.Y - 2 &&
            bounds.End.X <= visible.End.X + 2 && bounds.End.Y <= visible.End.Y + 2,
            "settings shell stays within viewport bounds");

        var oldEntry = NGame.Instance.MainMenu.GetNodeOrNull<Node>(
            "MainMenuTextButtons/LibrarianUnlockButton");
        Require(oldEntry is null, "legacy main-menu unlock entry is absent");

        string captures = Path.Combine(RequireIsolatedProfile(), "revision-v041-ui-audit-captures");
        Directory.CreateDirectory(captures);
        using Image image = viewport.GetTexture().GetImage();
        string screenshot = Path.Combine(captures, "041-settings-page.png");
        Require(image.SavePng(screenshot) == Error.Ok && File.Exists(screenshot),
            "native settings page screenshot saved");

        if (submenu.GetParent() is NSubmenuStack stack && ReferenceEquals(stack.Peek(), submenu))
            stack.Pop();
        await Frame();
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        yield return node;
        foreach (Node child in node.GetChildren())
            foreach (Node nested in Descendants(child))
                yield return nested;
    }

    private static void AuditProgressIdempotence()
    {
        // This is an in-memory ProgressState fixture. It never touches SaveManager's active profile.
        ProgressState progressive = ProgressState.CreateDefault();
        LibrarianUnlocks040.ApplyChoice(progressive, unlockAll: false);
        var first = EpochSnapshot(progressive);
        var firstMarkers = progressive.FtueCompleted.Order().ToArray();
        LibrarianUnlocks040.ApplyChoice(progressive, unlockAll: false);
        Require(first.SequenceEqual(EpochSnapshot(progressive)), "progressive choice is idempotent");
        Require(firstMarkers.SequenceEqual(progressive.FtueCompleted.Order()), "progressive marker is idempotent");

        LibrarianUnlocks040.ApplyChoice(progressive, unlockAll: true);
        var all = EpochSnapshot(progressive);
        Require(all.Length >= 7 && Enumerable.Range(1, 7).All(n =>
            progressive.Epochs.Any(e => e.Id == LibrarianUnlocks040.Id(n) && e.State == EpochState.Revealed)),
            "all choice reveals every librarian chapter");
        Require(progressive.FtueCompleted.Contains(LibrarianUnlocks040.AllMarker), "all marker recorded");

        LibrarianUnlocks040.ApplyChoice(progressive, unlockAll: false);
        Require(all.SequenceEqual(EpochSnapshot(progressive)), "progressive action never downgrades all choice");
        Require(progressive.FtueCompleted.Contains(LibrarianUnlocks040.AllMarker), "all marker is never downgraded");
    }

    private static (string Id, EpochState State, long ObtainDate)[] EpochSnapshot(ProgressState progress) =>
        progress.Epochs.Select(e => (e.Id, e.State, e.ObtainDate)).ToArray();

    private static async Task AuditExportAsync(string userData)
    {
        string root = Path.Combine(userData, "revision-v041-ui-audit-" + Guid.NewGuid().ToString("N"));
        string logs = Path.Combine(root, "logs");
        Directory.CreateDirectory(logs);
        try
        {
            File.WriteAllText(Path.Combine(logs, "audit.log"), "normal log line\naccount token should be sanitized\n");
            File.WriteAllText(Path.Combine(logs, "audit.txt"), "text log line\n");
            File.WriteAllText(Path.Combine(logs, "ignored.json"), "must not be exported\n");
            string metadata = JsonSerializer.Serialize(new { librarian = "0.4.1", audit = true });
            string archive = LibrarianLogExport041.Export(root, metadata);

            Require(File.Exists(archive) && archive.StartsWith(root, StringComparison.OrdinalIgnoreCase),
                "diagnostic archive is inside isolated root");
            Require(!Directory.EnumerateFiles(Path.GetDirectoryName(archive)!, "*.partial").Any(),
                "successful export removes partial file");
            using (ZipArchive zip = ZipFile.OpenRead(archive))
            {
                string[] names = zip.Entries.Select(e => e.FullName).Order().ToArray();
                Require(names.Contains("logs/audit.log") && names.Contains("logs/audit.txt"),
                    "archive includes supported log files");
                Require(!names.Contains("logs/ignored.json"), "archive excludes unsupported files");
                Require(names.Contains("versions.json") && names.Contains("export-summary.json"),
                    "archive includes metadata and summary");
                string versions = ReadEntry(zip, "versions.json");
                Require(versions.Contains("\"librarian\":\"0.4.1\"", StringComparison.Ordinal),
                    "archive preserves version metadata");
                using JsonDocument summary = JsonDocument.Parse(ReadEntry(zip, "export-summary.json"));
                Require(summary.RootElement.GetProperty("copied").GetInt32() == 2,
                    "summary counts only readable supported logs");
                Require(!summary.RootElement.GetProperty("saves_included").GetBoolean(),
                    "summary excludes saves");
            }

            string emptyRoot = Path.Combine(root, "empty");
            Directory.CreateDirectory(Path.Combine(emptyRoot, "logs"));
            bool failed = false;
            try { _ = LibrarianLogExport041.Export(emptyRoot, metadata); }
            catch (IOException) { failed = true; }
            Require(failed, "empty log directory fails explicitly");
            string diagnostics = Path.Combine(emptyRoot, "Librarian", "diagnostics");
            Require(!Directory.Exists(diagnostics) || !Directory.EnumerateFiles(diagnostics, "*.partial").Any(),
                "empty export leaves no partial archive");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        await Task.Yield();
    }

    private static string ReadEntry(ZipArchive archive, string name)
    {
        ZipArchiveEntry entry = archive.GetEntry(name) ?? throw new InvalidOperationException("Missing archive entry " + name);
        using Stream stream = entry.Open();
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
