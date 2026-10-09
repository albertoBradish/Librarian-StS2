using System.IO;
using System.Text.Json.Nodes;
using Godot;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves;
using STS2RitsuLib.Settings;

namespace Librarian.Mechanics;

internal static class LibrarianAscensionTools
{
    private static string _statusKey = "";
    internal static string Status => _statusKey.Length == 0 ? "" : Text(_statusKey);
    private static string Text(string key) => LibrarianLanguage.Text("main_menu_ui", "LIBRARIAN_SETTINGS." + key);
    internal static string Current
    {
        get
        {
            var stats = SaveManager.Instance.Progress.GetStatsForCharacter(ModelDb.Character<LibrarianCharacter>().Id);
            string template = LibrarianLanguage.TryRaw("main_menu_ui", "LIBRARIAN_SETTINGS.ascension_status", out string raw)
                ? raw : "A{Max} / A{Selected}";
            return template.Replace("{Max}", (stats?.MaxAscension ?? 0).ToString())
                .Replace("{Selected}", (stats?.PreferredAscension ?? 0).ToString());
        }
    }

    internal static void Apply(bool reset, IModSettingsUiActionHost host)
    {
        if (!LibrarianDebugReset.CanReset || LibrarianDebugReset.IsBusy)
        {
            _statusKey = "ascension_menu_required";
            host.RequestRefresh();
            return;
        }
        var progress = SaveManager.Instance.Progress;
        var stats = progress.GetOrCreateCharacterStats(ModelDb.Character<LibrarianCharacter>().Id);
        int oldMax = stats.MaxAscension, oldSelected = stats.PreferredAscension;
        try
        {
            stats.MaxAscension = reset ? 0 : Math.Max(10, oldMax);
            stats.PreferredAscension = reset ? 0 : oldSelected;
            SaveManager.Instance.SaveProgressFile();
            string path = ProjectSettings.GlobalizePath(SaveManager.Instance.GetProfileScopedPath(Path.Combine("saves", "progress.save")));
            var saved = JsonNode.Parse(File.ReadAllText(path));
            string id = ModelDb.Character<LibrarianCharacter>().Id.ToString();
            var rows = (saved?["character_stats"] as JsonArray)?.Where(row => row?["id"]?.GetValue<string>() == id).ToArray();
            if (rows is not { Length: 1 } || rows[0]?["max_ascension"]?.GetValue<int>() != stats.MaxAscension
                || rows[0]?["preferred_ascension"]?.GetValue<int>() != stats.PreferredAscension)
                throw new IOException("Ascension progress could not be read back.");
            _statusKey = reset ? "ascension_reset_done" : "ascension_unlock_done";
            MainFile.Logger.Info($"LIBRARIAN_ASCENSION_APPLIED reset={reset} max={stats.MaxAscension} selected={stats.PreferredAscension} historiesChanged=False");
        }
        catch (Exception error)
        {
            stats.MaxAscension = oldMax; stats.PreferredAscension = oldSelected;
            _statusKey = "ascension_failed";
            MainFile.Logger.Warn("Librarian ascension update failed: " + error.GetType().Name);
        }
        host.RequestRefreshAfterDataModelBatchChange();
    }
}
