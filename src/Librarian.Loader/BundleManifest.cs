using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Librarian.Loader;

internal sealed class VariantDescriptor
{
    [JsonPropertyName("game_version")]
    public required string GameVersion { get; init; }

    [JsonPropertyName("assembly")]
    public required string Assembly { get; init; }

    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }
}

internal sealed class BundleManifest
{
    internal const string ModId = "Librarian";
    internal const string ModVersion = "1.4.0";
    internal const string FileName = "librarian-variants.manifest";
    internal static readonly string[] SupportedGameVersions = ["0.107.1", "0.111.0"];
    private const long MaximumManifestBytes = 128 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    [JsonPropertyName("schema_version")]
    public required int SchemaVersion { get; init; }

    [JsonPropertyName("mod_id")]
    public required string Id { get; init; }

    [JsonPropertyName("mod_version")]
    public required string Version { get; init; }

    [JsonPropertyName("variants")]
    public required VariantDescriptor[] Variants { get; init; }

    internal static BundleManifest Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        RejectDuplicateProperties(document.RootElement);
        var manifest = JsonSerializer.Deserialize<BundleManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("The Librarian variant manifest is empty.");
        manifest.Validate();
        return manifest;
    }

    internal static BundleManifest Read(string rootDirectory)
    {
        var manifestPath = ResolveContainedPath(rootDirectory, FileName);
        using var stream = OpenReadOnly(manifestPath);
        if (stream.Length > MaximumManifestBytes)
            throw new InvalidDataException("The Librarian variant manifest exceeds 128 KiB.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    internal VariantDescriptor Select(string? gameVersionLabel)
    {
        var gameVersion = NormalizeGameVersion(gameVersionLabel);
        return Variants.Single(variant => variant.GameVersion == gameVersion);
    }

    internal static string NormalizeGameVersion(string? label)
    {
        var version = label?.StartsWith('v') == true ? label[1..] : label;
        if (version is null || !SupportedGameVersions.Contains(version, StringComparer.Ordinal))
            throw new NotSupportedException($"Librarian {ModVersion} supports game 0.107.1 and 0.111.0 only; detected '{label ?? "unknown"}'.");
        return version;
    }

    internal void ValidateNativeManifest(string rootDirectory)
    {
        using var stream = OpenReadOnly(ResolveContainedPath(rootDirectory, "Librarian.json"));
        if (stream.Length > MaximumManifestBytes)
            throw new InvalidDataException("Librarian.json exceeds 128 KiB.");
        using var document = JsonDocument.Parse(stream);
        RejectDuplicateProperties(document.RootElement);
        var native = document.RootElement;
        if (!native.TryGetProperty("id", out var id) || id.GetString() != Id ||
            !native.TryGetProperty("version", out var version) || version.GetString() != Version)
            throw new InvalidDataException("Librarian.json and the variant manifest must both declare Librarian 1.4.0.");
    }

    /// <summary>The caller keeps the returned read lock alive until LoadFromAssemblyPath has completed.</summary>
    internal static FileStream OpenVerifiedAssembly(string rootDirectory, VariantDescriptor variant)
    {
        var path = ResolveContainedPath(rootDirectory, variant.Path);
        var stream = OpenReadOnly(path);
        try
        {
            var actualHash = SHA256.HashData(stream);
            if (!CryptographicOperations.FixedTimeEquals(actualHash, Convert.FromHexString(variant.Sha256)))
                throw new InvalidDataException($"Librarian content SHA-256 mismatch for {variant.GameVersion}: {variant.Path}.");
            ValidateAssemblyIdentity(AssemblyName.GetAssemblyName(path), variant.Assembly);
            stream.Position = 0;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    internal static void ValidateAssemblyIdentity(AssemblyName identity, string expectedName)
    {
        if (identity.Name != expectedName || identity.Version != System.Version.Parse(ModVersion + ".0") ||
            !string.IsNullOrEmpty(identity.CultureName) || identity.GetPublicKeyToken() is { Length: > 0 })
            throw new InvalidDataException($"Invalid Librarian content assembly identity: '{identity.FullName}'; expected Librarian, Version=1.4.0.0, Culture=neutral, PublicKeyToken=null.");
    }

    internal static string ResolveContainedPath(string rootDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || System.IO.Path.IsPathRooted(relativePath) ||
            relativePath.Contains('\\') || relativePath.Contains(':') ||
            relativePath.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException($"Unsafe Librarian bundle path '{relativePath}'.");
        var root = System.IO.Path.GetFullPath(rootDirectory);
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relativePath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = System.IO.Path.TrimEndingDirectorySeparator(root) + System.IO.Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, comparison))
            throw new InvalidDataException($"Librarian bundle path leaves its mod directory: '{relativePath}'.");
        var current = root;
        foreach (var segment in relativePath.Split('/'))
        {
            current = System.IO.Path.Combine(current, segment);
            if ((System.IO.File.Exists(current) || Directory.Exists(current)) &&
                (System.IO.File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Librarian bundle path contains a symbolic link or junction: '{relativePath}'.");
        }
        return path;
    }

    private static FileStream OpenReadOnly(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    private void Validate()
    {
        if (SchemaVersion != 1 || Id != ModId || Version != ModVersion)
            throw new InvalidDataException("The variant manifest must use schema 1 and declare Librarian 1.4.0.");
        if (Variants is null || Variants.Length != SupportedGameVersions.Length)
            throw new InvalidDataException("The Librarian bundle must declare exactly two game variants.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variant in Variants)
        {
            if (variant is null || !SupportedGameVersions.Contains(variant.GameVersion, StringComparer.Ordinal) || !seen.Add(variant.GameVersion))
                throw new InvalidDataException("The Librarian bundle contains an unknown or duplicate game variant.");
            if (variant.Assembly != ModId || variant.Path != $"compat/{variant.GameVersion}/{ModId}.dll")
                throw new InvalidDataException($"Invalid content assembly or path for game {variant.GameVersion}.");
            if (variant.Sha256 is null || variant.Sha256.Length != 64 || !variant.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException($"Invalid SHA-256 for game {variant.GameVersion}.");
        }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException($"Duplicate JSON property '{property.Name}'.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                RejectDuplicateProperties(item);
        }
    }
}
