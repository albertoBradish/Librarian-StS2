using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Reflection;
using Godot;
using HarmonyLib;
using Librarian.LibrarianCode;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Entities.Cards;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Powers;
using Librarian.LibrarianCode.Relics;
using Librarian.LibrarianCode.Potions;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using STS2RitsuLib.Settings;

namespace Librarian.Mechanics;

/// <summary>Local text selection. Does not alter game locale, combat state, or other mods' keys.</summary>
internal static class LibrarianLanguage
{
    internal sealed class PackInfo
    {
        public string Name { get; set; } = "";
        public string Culture { get; set; } = "en-US";
    }
    internal sealed class Preference
    {
        public bool Initialized { get; set; }
        public string Language { get; set; } = "eng";
    }
    private sealed record Pack(PackInfo Info, Dictionary<string, Dictionary<string, string>> Tables);
    internal static readonly string[] Tables = ["ancients", "cards", "card_selection", "characters", "epochs", "events", "main_menu_ui", "potions", "powers", "relics", "static_hover_tips", "librarian_runtime"];
    private static readonly Dictionary<string, Pack> Packs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Pack> Bundled = new(StringComparer.OrdinalIgnoreCase);
    private static readonly AccessTools.FieldRef<LocTable, string> ReadTableName = AccessTools.FieldRefAccess<LocTable, string>("_name");
    private static readonly FieldInfo FormatterField = AccessTools.Field(typeof(LocManager), "_smartFormatter");
    private static readonly MethodInfo FormatMethod = FormatterField.FieldType.GetMethod("Format", [typeof(IFormatProvider), typeof(string), typeof(object[])])!;
    private static bool _initialized;
    [ThreadStatic] private static int _nativeScope;
    private static readonly Dictionary<string, Dictionary<string, string>> NativeTables = new();
    private static string _selected = "eng";
    internal static string Selected => _selected;
    internal static string FilePath => ProjectSettings.GlobalizePath("user://Librarian/language.json");
    internal static string PackDirectory => ProjectSettings.GlobalizePath("user://Librarian/languages");
    internal static string TemplateDirectory { get; private set; } = "";
    internal static string Status { get; private set; } = "";
    internal static CultureInfo Culture => CultureInfo.GetCultureInfo(Packs.TryGetValue(_selected, out var p) ? p.Info.Culture : "en-US");
    internal static string TableName(LocTable table) => ReadTableName(table);

    internal static void Initialize()
    {
        if (_initialized) return;
        foreach (string language in new[] { "eng", "zhs" })
        {
            var tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (string table in Tables)
            {
                using var file = Godot.FileAccess.Open($"res://Librarian/localization/{language}/{table}.json", Godot.FileAccess.ModeFlags.Read);
                if (file is null) throw new IOException($"Missing bundled Librarian table: {language}/{table}");
                tables[table] = JsonSerializer.Deserialize<Dictionary<string, string>>(file.GetAsText()) ?? throw new InvalidDataException(table);
            }
            Bundled[language] = new(new PackInfo { Name = language == "zhs" ? "简体中文" : "English", Culture = language == "zhs" ? "zh-CN" : "en-US" }, tables);
        }
        LoadPacks();
        Preference? saved = null;
        if (File.Exists(FilePath))
        {
            try { saved = JsonSerializer.Deserialize<Preference>(File.ReadAllText(FilePath)); }
            catch (Exception e)
            {
                MainFile.Logger.Warn("Language preference unreadable: " + e.GetType().Name);
                // Preserve the malformed input before writing a replacement.
                try { File.Copy(FilePath, FilePath + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ")); }
                catch (IOException) { /* Read-only profiles still allow a session-only selection. */ }
                catch (UnauthorizedAccessException) { }
            }
        }
        _selected = saved?.Initialized == true && !string.IsNullOrWhiteSpace(saved.Language) ? saved.Language : Detect(LocManager.Instance.Language);
        if (!Packs.ContainsKey(_selected)) _selected = "eng";
        _initialized = true;
        UpdateManifestText();
        if (saved?.Initialized != true) Save();
        MainFile.Logger.Info($"LIBRARIAN_LANGUAGE_READY selected={_selected} game={LocManager.Instance.Language} packs={Packs.Count}");
    }

    internal static string Detect(string? language)
    {
        if (language is not null && Packs.ContainsKey(language)) return language;
        return language is "zhs" or "zht" ? "zhs" : "eng";
    }

    private static void LoadPacks()
    {
        Packs.Clear();
        foreach (var pair in Bundled) Packs[pair.Key] = pair.Value;
        Status = "";
        if (!Directory.Exists(PackDirectory)) return;
        foreach (string directory in Directory.EnumerateDirectories(PackDirectory).Order())
        {
            string code = Path.GetFileName(directory);
            if (!Regex.IsMatch(code, "^[a-z][a-z0-9_-]{1,31}$")) continue;
            try
            {
                var info = JsonSerializer.Deserialize<PackInfo>(File.ReadAllText(Path.Combine(directory, "pack.json"))) ?? throw new InvalidDataException("pack.json");
                if (string.IsNullOrWhiteSpace(info.Name) || info.Name.Length > 80) throw new InvalidDataException("Invalid language name");
                _ = CultureInfo.GetCultureInfo(info.Culture);
                var baseline = Bundled.GetValueOrDefault(code, Bundled["eng"]);
                var tables = baseline.Tables.ToDictionary(p => p.Key, p => new Dictionary<string, string>(p.Value, StringComparer.Ordinal), StringComparer.Ordinal);
                foreach (string table in Tables)
                {
                    string file = Path.Combine(directory, table + ".json");
                    if (!File.Exists(file)) continue;
                    var translations = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? throw new InvalidDataException(file);
                    foreach (var entry in translations)
                    {
                        if (!tables[table].TryGetValue(entry.Key, out var original) || entry.Value is null)
                            throw new InvalidDataException($"Unknown or null key: {table}/{entry.Key}");
                        var required = Parameters(original);
                        if (!required.SetEquals(Parameters(entry.Value))) throw new InvalidDataException($"Parameters differ: {table}/{entry.Key}");
                        object formatter = FormatterField.GetValue(null)!;
                        object parser = formatter.GetType().GetProperty("Parser")!.GetValue(formatter)!;
                        parser.GetType().GetMethod("ParseFormat", [typeof(string)])!.Invoke(parser, [entry.Value]);
                        tables[table][entry.Key] = entry.Value;
                    }
                }
                Packs[code] = new(info, tables);
            }
            catch (Exception e)
            {
                Status += (Status.Length == 0 ? "" : "\n") + SettingsStatus("language_pack_failed", ("Pack", code), ("Reason", e.Message));
                MainFile.Logger.Warn("Language pack rejected: " + code + "; bundled fallback retained. Details are shown in mod settings.");
            }
        }
    }

    private static HashSet<string> Parameters(string text) => Regex.Matches(text, @"\{([A-Za-z_][A-Za-z0-9_.]*)")
        .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    internal static IReadOnlyList<ModSettingsChoiceOption<string>> Options()
    {
        if (!_initialized) return [new("zhs", ModSettingsText.Literal("简体中文")), new("eng", ModSettingsText.Literal("English"))];
        return Packs.Select(p => new ModSettingsChoiceOption<string>(p.Key, ModSettingsText.Literal(p.Value.Info.Name))).ToArray();
    }

    internal static void Select(string language)
    {
        Initialize();
        if (!Packs.ContainsKey(language)) throw new ArgumentException("Unknown Librarian language", nameof(language));
        _selected = language;
        Save();
        Refresh();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(new Preference { Initialized = true, Language = _selected }, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(FilePath + ".tmp", FilePath, true);
        }
        catch (Exception e)
        {
            Status = Text("main_menu_ui", "LIBRARIAN_SETTINGS.language_save_failed");
            MainFile.Logger.Warn("Language selection save failed: " + e.GetType().Name);
        }
    }

    internal static void Reload()
    {
        Initialize();
        LoadPacks();
        if (!Packs.ContainsKey(_selected)) { _selected = "eng"; Save(); }
        Refresh();
    }

    internal static void ExportTemplates(bool openDirectory = true, string? destinationDirectory = null)
    {
        Initialize();
        try
        {
            string exportDirectory = LibrarianExportDestination.CreateUniqueDirectory("Librarian-translations-" + LibrarianUpdateNotice051.CurrentVersion, destinationDirectory, partial: true);
            foreach (var pair in Bundled)
            {
                string directory = Path.Combine(exportDirectory, pair.Key);
                Directory.CreateDirectory(directory);
                WriteNew(Path.Combine(directory, "pack.json"), JsonSerializer.Serialize(pair.Value.Info, new JsonSerializerOptions { WriteIndented = true }));
                foreach (var table in pair.Value.Tables)
                    WriteNew(Path.Combine(directory, table.Key + ".json"), JsonSerializer.Serialize(table.Value, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            }
            string completedDirectory = exportDirectory[..^".partial".Length];
            Directory.Move(exportDirectory, completedDirectory);
            exportDirectory = completedDirectory;
            TemplateDirectory = exportDirectory;
            Status = SettingsStatus("language_templates_exported", ("Path", exportDirectory));
        }
        catch (Exception e)
        {
            Status = SettingsStatus("language_templates_failed", ("Reason", e.Message));
            MainFile.Logger.Warn("Translation template export failed: " + e.Message);
            return;
        }

        if (openDirectory && !LibrarianExportDestination.TryOpenDirectory(TemplateDirectory, out string reason))
        {
            Status += "\n" + SettingsStatus("folder_open_failed", ("Reason", reason));
            MainFile.Logger.Warn("Translation templates exported, but the export folder could not be opened: " + reason);
        }
    }

    private static void WriteNew(string file, string text)
    {
        if (File.Exists(file)) return;
        using var stream = new FileStream(file, FileMode.CreateNew, System.IO.FileAccess.Write);
        using var writer = new StreamWriter(stream);
        writer.Write(text);
    }

    private static void Refresh()
    {
        UpdateManifestText();
        // Rebuild labels using their own source keys; the native language remains unchanged.
        AccessTools.Method(typeof(LocManager), "TriggerLocaleChange").Invoke(LocManager.Instance, null);
        if (NGame.Instance is { } game)
            foreach (var card in game.FindChildren("*", "", true, false).OfType<NCard>().Where(n => n.IsNodeReady() && n.Model is LibrarianCard))
            {
                card.UpdateVisuals(card.DisplayingPile, CardPreviewMode.Normal);
                AccessTools.Method(typeof(NCard), "UpdateTypePlaque").Invoke(card, null);
            }
    }

    private static void UpdateManifestText()
    {
        var manifest = ModManager.GetLoadedMods().FirstOrDefault(m => m.manifest?.id == MainFile.ModId)?.manifest;
        if (manifest is null) return;
        if (TryRaw("main_menu_ui", "LIBRARIAN_MOD.name", out var name)) manifest.name = name;
        if (TryRaw("main_menu_ui", "LIBRARIAN_MOD.description", out var description)) manifest.description = description;
    }

    internal static bool TryRaw(string table, string key, out string value)
    {
        value = "";
        if (!_initialized) return false;
        if (Packs.TryGetValue(_selected, out var pack) && pack.Tables.TryGetValue(table, out var entries) && entries.TryGetValue(key, out value!)) return true;
        if (_nativeScope == 0) return false;
        // Native keywords appended to OUR cards/tooltips use the selected language too.
        // These translations remain in the game's PCK and are never copied/distributed.
        string language = LocManager.Languages.Contains(_selected) ? _selected : "eng";
        string path = $"res://localization/{language}/{table}.json";
        if (!NativeTables.TryGetValue(path, out var native))
        {
            native = new();
            if (Godot.FileAccess.FileExists(path))
            {
                using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
                native = JsonSerializer.Deserialize<Dictionary<string, string>>(file.GetAsText()) ?? new();
            }
            NativeTables[path] = native;
        }
        return native.TryGetValue(key, out value!);
    }
    internal static bool EnterNativeScope(object model)
    {
        if (model is not (LibrarianCard or LibrarianPower or LibrarianRelic or LibrarianPotion)) return false;
        _nativeScope++;
        return true;
    }
    internal static void LeaveNativeScope(bool entered) { if (entered) _nativeScope--; }
    internal static T NativeTip<T>(Func<T> create)
    {
        _nativeScope++;
        try { return create(); }
        finally { _nativeScope--; }
    }
    internal static bool TryRuntimeTable(string name, out LocTable table)
    {
        table = null!;
        if (name != "librarian_runtime" || !_initialized) return false;
        table = new LocTable(name, Packs[_selected].Tables[name]);
        return true;
    }

    internal static string Text(string table, string key) => new LocString(table, key).GetFormattedText();
    internal static string FormatText(LocString text, string raw)
    {
        var variables = new Dictionary<string, object>(text.Variables);
        try { return FormatRaw(raw, variables); }
        catch (TargetInvocationException)
        {
            // A syntactically valid third-party formatter can still fail with real model values.
            // Restore only that external entry; bundled failures remain visible to validation.
            var baseline = Bundled.GetValueOrDefault(_selected, Bundled["eng"]);
            if (!baseline.Tables.TryGetValue(text.LocTable, out var entries) ||
                !entries.TryGetValue(text.LocEntryKey, out var fallback) || fallback == raw) throw;
            string result = FormatRaw(fallback, variables);
            Packs[_selected].Tables[text.LocTable][text.LocEntryKey] = fallback;
            Status = SettingsStatus("language_entry_failed", ("Pack", _selected), ("Table", text.LocTable), ("Key", text.LocEntryKey));
            MainFile.Logger.Warn("External translation could not format; bundled entry restored. Details are shown in mod settings.");
            return result;
        }
    }
    internal static string FormatRaw(string raw, Dictionary<string, object> variables)
        => (string)FormatMethod.Invoke(FormatterField.GetValue(null), [Culture, raw, new object[] { variables }])!;
    // Diagnostics use bundled text directly so an invalid external formatter cannot
    // recursively break the fallback message itself, including during initial loading.
    private static string SettingsStatus(string key, params (string Name, object Value)[] values)
    {
        string language = _initialized ? _selected : Detect(LocManager.Instance.Language);
        var baseline = Bundled.GetValueOrDefault(language, Bundled["eng"]);
        string raw = baseline.Tables["main_menu_ui"]["LIBRARIAN_SETTINGS." + key];
        var vars = values.ToDictionary(p => p.Name, p => p.Value);
        return (string)FormatMethod.Invoke(FormatterField.GetValue(null), [CultureInfo.GetCultureInfo(baseline.Info.Culture), raw, new object[] { vars }])!;
    }
    internal static string Format(string key, params (string Name, object Value)[] values)
    {
        var text = new LocString("librarian_runtime", "LIBRARIAN_" + key);
        foreach (var (name, value) in values) text.AddObj(name, value);
        return text.GetFormattedText();
    }
}

[HarmonyPatch(typeof(NCard), "UpdateTypePlaque")]
internal static class LibrarianLanguageCardTypeScope
{
    [HarmonyPrefix] private static void Prefix(NCard __instance, out bool __state) => __state = LibrarianLanguage.EnterNativeScope(__instance.Model);
    [HarmonyFinalizer] private static void Finalizer(bool __state) => LibrarianLanguage.LeaveNativeScope(__state);
}

[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class LibrarianLanguageFirstUse
{
    [HarmonyPrefix] private static void Prefix() => LibrarianLanguage.Initialize();
}

[HarmonyPatch(typeof(LocTable), nameof(LocTable.GetRawText))]
internal static class LibrarianLanguageRawText
{
    [HarmonyPrefix] private static bool Prefix(LocTable __instance, string key, ref string __result)
        => !LibrarianLanguage.TryRaw(LibrarianLanguage.TableName(__instance), key, out __result);
}

[HarmonyPatch(typeof(LocTable), nameof(LocTable.HasEntry))]
internal static class LibrarianLanguageHasEntry
{
    [HarmonyPrefix] private static bool Prefix(LocTable __instance, string key, ref bool __result)
    {
        if (!LibrarianLanguage.TryRaw(LibrarianLanguage.TableName(__instance), key, out _)) return true;
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(LocString), nameof(LocString.GetFormattedText))]
internal static class LibrarianLanguageFormatting
{
    [HarmonyPrefix] private static bool Prefix(LocString __instance, ref string __result)
    {
        if (!LibrarianLanguage.TryRaw(__instance.LocTable, __instance.LocEntryKey, out string raw)) return true;
        __result = LibrarianLanguage.FormatText(__instance, raw);
        return false;
    }
}

[HarmonyPatch(typeof(LocManager), nameof(LocManager.GetTable))]
internal static class LibrarianLanguageRuntimeTable
{
    [HarmonyPrefix] private static bool Prefix(string name, ref LocTable __result)
        => !LibrarianLanguage.TryRuntimeTable(name, out __result);
}

[HarmonyPatch]
internal static class LibrarianLanguageCardDescriptionScope
{
    private static MethodBase TargetMethod() => typeof(CardModel).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
        .Single(m => m.Name == "GetDescriptionForPile" && m.GetParameters().Length == 3);
    [HarmonyPrefix] private static void Prefix(CardModel __instance, out bool __state) => __state = LibrarianLanguage.EnterNativeScope(__instance);
    [HarmonyFinalizer] private static void Finalizer(bool __state) => LibrarianLanguage.LeaveNativeScope(__state);
}

[HarmonyPatch]
internal static class LibrarianLanguageHoverScope
{
    private static IEnumerable<MethodBase> TargetMethods() => new[] { typeof(CardModel), typeof(PowerModel), typeof(RelicModel), typeof(PotionModel) }
        .Select(t => AccessTools.PropertyGetter(t, "HoverTips")).Where(m => m is not null).Distinct();
    [HarmonyPrefix] private static void Prefix(object __instance, out bool __state) => __state = LibrarianLanguage.EnterNativeScope(__instance);
    [HarmonyPostfix, HarmonyPriority(Priority.Last)] private static void Postfix(bool __state, ref IEnumerable<IHoverTip> __result)
    {
        if (__state) __result = __result.ToArray();
    }
    [HarmonyFinalizer] private static void Finalizer(bool __state) => LibrarianLanguage.LeaveNativeScope(__state);
}
