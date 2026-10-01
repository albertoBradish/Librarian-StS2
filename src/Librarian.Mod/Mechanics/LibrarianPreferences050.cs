using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using STS2RitsuLib.Settings;

namespace Librarian.Mechanics;

public enum LibrarianLockedOrbDisplayMode
{
    NegativeTurns = 0,
    LegacyTurns = 1,
    ValueAndTurns = 2
}

/// <summary>Local presentation preferences. Never serialized into a run or used by combat rules.</summary>
public sealed class LibrarianPreferences050
{
    public bool CardEffects { get; set; } = true;
    public bool OrbEffects { get; set; } = true;
    public bool Particles { get; set; } = true;
    public bool ReducedMotion { get; set; }
    public bool TeammateEffects { get; set; } = true;
    public bool MagicCircle { get; set; } = true;
    public bool OrbIdle { get; set; } = true;
    public bool WaveBar { get; set; } = true;
    public bool TideBlockFeedback { get; set; }
    public LibrarianLockedOrbDisplayMode LockedOrbDisplay { get; set; } = LibrarianLockedOrbDisplayMode.NegativeTurns;
    public bool OrbSounds { get; set; } = true;
    public int EffectOpacity { get; set; } = 80;
    public int SoundVolume { get; set; } = 100;
    public int EffectLimit { get; set; } = 24;
    internal static LibrarianPreferences050 CreateDefaults() => new();
    internal static LibrarianPreferences050 Current { get; private set; } = CreateDefaults();
    internal static string FilePath => ProjectSettings.GlobalizePath("user://Librarian/presentation.json");
    private static string _statusKey = "";
    internal static string Status => _statusKey.Length == 0 ? "" : LibrarianLanguage.Text("main_menu_ui", "LIBRARIAN_SETTINGS." + _statusKey);
    internal static void Normalize(LibrarianPreferences050 p)
    {
        p.EffectOpacity = Math.Clamp(p.EffectOpacity, 10, 100);
        p.SoundVolume = Math.Clamp(p.SoundVolume, 0, 100);
        p.EffectLimit = Math.Clamp(p.EffectLimit, 4, 40);
        if (!Enum.IsDefined(typeof(LibrarianLockedOrbDisplayMode), p.LockedOrbDisplay))
            p.LockedOrbDisplay = CreateDefaults().LockedOrbDisplay;
    }
    internal static LibrarianPreferences050 Deserialize(string json)
    {
        var values = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("Presentation preferences must be an object.");
        var mode = CreateDefaults().LockedOrbDisplay;
        if (values.TryGetPropertyValue(nameof(LockedOrbDisplay), out var modeNode))
        {
            if (modeNode is JsonValue storedMode && storedMode.TryGetValue<int>(out int number)
                && Enum.IsDefined(typeof(LibrarianLockedOrbDisplayMode), number))
                mode = (LibrarianLockedOrbDisplayMode)number;
        }
        else if (values["LockedOrbValues"] is JsonValue oldMode && oldMode.TryGetValue<bool>(out bool showValues) && showValues)
            mode = LibrarianLockedOrbDisplayMode.ValueAndTurns;
        // Sanitize only the mode before deserialization, so malformed mode values do
        // not discard unrelated saved preferences. The old boolean is read-only input.
        values[nameof(LockedOrbDisplay)] = (int)mode;
        var result = JsonSerializer.Deserialize<LibrarianPreferences050>(values.ToJsonString()) ?? CreateDefaults();
        Normalize(result);
        return result;
    }
    internal static void Load()
    {
        try
        {
            Current = File.Exists(FilePath) ? Deserialize(File.ReadAllText(FilePath)) : CreateDefaults();
            Normalize(Current);
        }
        catch (Exception e)
        {
            Current = CreateDefaults();
            _statusKey = "preferences_load_failed";
            Librarian.LibrarianCode.MainFile.Logger.Warn("Settings load: " + e.GetType().Name);
        }
    }
    internal static void Save()
    {
        Normalize(Current);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(FilePath + ".tmp", FilePath, true);
            _statusKey = "";
        }
        catch (Exception e)
        {
            _statusKey = "preferences_save_failed";
            Librarian.LibrarianCode.MainFile.Logger.Warn("Settings save: " + e.GetType().Name);
        }
    }
    internal static void Reset() { Current = CreateDefaults(); Save(); }
    internal static IModSettingsValueBinding<T> Bind<T>(string key, Func<LibrarianPreferences050, T> read, Action<LibrarianPreferences050, T> write)
        => Bind(key, read, write, read(CreateDefaults()));
    internal static IModSettingsValueBinding<T> Bind<T>(string key, Func<LibrarianPreferences050, T> read, Action<LibrarianPreferences050, T> write, T defaultValue)
        => ModSettingsBindings.WithDefault(ModSettingsBindings.Callback("Librarian", key,
            () => read(Current), value => { write(Current, value); Normalize(Current); }, Save), () => defaultValue);
}
