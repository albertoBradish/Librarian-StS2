using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Librarian.Loader;

var passed = 0;
var failed = new List<string>();
var testAssembly = Assembly.GetExecutingAssembly().Location;
var testHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(testAssembly)));
var json = Manifest();

Run("select stable exact version", () => Equal("0.107.1", BundleManifest.Parse(json).Select("0.107.1").GameVersion));
Run("select beta v-prefixed release label", () => Equal("0.111.0", BundleManifest.Parse(json).Select("v0.111.0").GameVersion));
Run("reject unknown newer version", () => Reject<NotSupportedException>(() => BundleManifest.Parse(json).Select("0.112.0")));
Run("reject unknown older version", () => Reject<NotSupportedException>(() => BundleManifest.Parse(json).Select("0.107.0")));
Run("reject missing game metadata", () => Reject<NotSupportedException>(() => BundleManifest.Parse(json).Select(null)));
Run("reject unsupported release suffix", () => Reject<NotSupportedException>(() => BundleManifest.Parse(json).Select("v0.111.0-beta1")));
Run("reject whitespace release label", () => Reject<NotSupportedException>(() => BundleManifest.Parse(json).Select(" 0.111.0")));
Run("reject manifest schema change", () => Reject<InvalidDataException>(() => BundleManifest.Parse(Manifest(schema: 2))));
Run("reject wrong owner", () => Reject<InvalidDataException>(() => BundleManifest.Parse(Manifest(id: "OtherMod"))));
Run("reject inconsistent mod version", () => Reject<InvalidDataException>(() => BundleManifest.Parse(Manifest(version: "1.2.0-beta13"))));
Run("reject missing target", () => Reject<InvalidDataException>(() => BundleManifest.Parse(Manifest(betaVersion: null))));
Run("reject duplicate target", () => Reject<InvalidDataException>(() => BundleManifest.Parse(Manifest(betaVersion: "0.107.1"))));
Run("reject unknown manifest target", () => Reject<InvalidDataException>(() => BundleManifest.Parse(Manifest(betaVersion: "0.112.0"))));
Run("reject wrong assembly identity declaration", () => Reject<InvalidDataException>(() => BundleManifest.Parse(Manifest(assembly: "Librarian.Loader"))));
Run("reject path traversal declaration", () => Reject<InvalidDataException>(() => BundleManifest.Parse(Manifest(stablePath: "compat/../../Librarian.dll"))));
Run("reject hash length", () => Reject<InvalidDataException>(() => BundleManifest.Parse(Manifest(hash: "ABCD"))));
Run("reject nonhex hash", () => Reject<InvalidDataException>(() => BundleManifest.Parse(Manifest(hash: new string('g', 64)))));
Run("reject unknown manifest property", () => Reject<JsonException>(() => BundleManifest.Parse(json.Replace("\"schema_version\":1", "\"unexpected\":true,\"schema_version\":1", StringComparison.Ordinal))));
Run("reject duplicate JSON property", () => Reject<InvalidDataException>(() => BundleManifest.Parse(json.Replace("\"schema_version\":1", "\"schema_version\":1,\"schema_version\":1", StringComparison.Ordinal))));
Run("reject null variants", () => Reject<InvalidDataException>(() => BundleManifest.Parse("{\"schema_version\":1,\"mod_id\":\"Librarian\",\"mod_version\":\"1.3.5\",\"variants\":null}")));
Run("reject missing required fields", () => Reject<JsonException>(() => BundleManifest.Parse("{}")));
Run("reject malformed JSON", () => Reject<JsonException>(() => BundleManifest.Parse("{")));
Run("accept content assembly identity", () => BundleManifest.ValidateAssemblyIdentity(Assembly.GetExecutingAssembly().GetName(), "Librarian"));
Run("reject loader identity as content", () => Reject<InvalidDataException>(() => BundleManifest.ValidateAssemblyIdentity(new AssemblyName("Librarian.Loader, Version=1.3.5.0"), "Librarian")));
Run("reject stale content assembly version", () => Reject<InvalidDataException>(() => BundleManifest.ValidateAssemblyIdentity(new AssemblyName("Librarian, Version=1.2.0.0"), "Librarian")));
Run("reject assembly culture", () => Reject<InvalidDataException>(() => BundleManifest.ValidateAssemblyIdentity(new AssemblyName("Librarian, Version=1.3.5.0, Culture=en-US"), "Librarian")));

foreach (var unsafePath in new[] { "../Librarian.dll", "/Librarian.dll", "C:/Librarian.dll", "compat\\0.107.1\\Librarian.dll", "compat//Librarian.dll", "compat/./Librarian.dll" })
    Run($"reject contained-path escape {unsafePath}", () => Reject<InvalidDataException>(() => BundleManifest.ResolveContainedPath(Path.GetTempPath(), unsafePath)));

Run("first initialization then successful duplicate is no-op", () =>
{
    var gate = new InitializationGate();
    Equal(true, gate.Begin());
    gate.Complete();
    Equal(false, gate.Begin());
});
Run("reject reentrant initialization", () =>
{
    var gate = new InitializationGate();
    gate.Begin();
    Reject<InvalidOperationException>(() => gate.Begin());
});
Run("reject retry after partial failure", () =>
{
    var gate = new InitializationGate();
    gate.Begin();
    gate.Fail();
    Reject<InvalidOperationException>(() => gate.Begin());
});
Run("concurrent initialization admits one owner", () =>
{
    var gate = new InitializationGate();
    var admitted = 0;
    Parallel.For(0, 16, _ =>
    {
        try
        {
            if (gate.Begin())
                Interlocked.Increment(ref admitted);
        }
        catch (InvalidOperationException)
        {
        }
    });
    Equal(1, admitted);
});

var temp = Path.Combine(Path.GetTempPath(), "Librarian.Loader.Tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    foreach (var version in BundleManifest.SupportedGameVersions)
    {
        Directory.CreateDirectory(Path.Combine(temp, "compat", version));
        File.Copy(testAssembly, Path.Combine(temp, "compat", version, "Librarian.dll"));
    }
    File.WriteAllText(Path.Combine(temp, BundleManifest.FileName), json);
    File.WriteAllText(Path.Combine(temp, "Librarian.json"), "{\"id\":\"Librarian\",\"version\":\"1.3.5\"}");
    Run("read full manifest and matching native version", () => BundleManifest.Read(temp).ValidateNativeManifest(temp));
    Run("verify both variant bytes and assembly identity", () =>
    {
        foreach (var variant in BundleManifest.Read(temp).Variants)
        {
            using var file = BundleManifest.OpenVerifiedAssembly(temp, variant);
            Equal(0L, file.Position);
            Equal(new FileInfo(testAssembly).Length, file.Length);
        }
    });
    Run("reject mismatched native manifest version", () =>
    {
        File.WriteAllText(Path.Combine(temp, "Librarian.json"), "{\"id\":\"Librarian\",\"version\":\"1.2.0-beta13\"}");
        Reject<InvalidDataException>(() => BundleManifest.Read(temp).ValidateNativeManifest(temp));
    });
    Run("reject corrupted assembly before metadata load", () =>
    {
        var selected = BundleManifest.Read(temp).Select("0.111.0");
        File.WriteAllText(Path.Combine(temp, selected.Path), "damaged");
        Reject<InvalidDataException>(() => BundleManifest.OpenVerifiedAssembly(temp, selected));
    });
    Run("reject missing selected assembly", () =>
    {
        var selected = BundleManifest.Read(temp).Select("0.111.0");
        File.Delete(Path.Combine(temp, selected.Path));
        Reject<FileNotFoundException>(() => BundleManifest.OpenVerifiedAssembly(temp, selected));
    });
    Run("reject overlarge variant manifest", () =>
    {
        File.WriteAllText(Path.Combine(temp, BundleManifest.FileName), new string(' ', 128 * 1024 + 1));
        Reject<InvalidDataException>(() => BundleManifest.Read(temp));
    });
}
finally
{
    Directory.Delete(temp, recursive: true);
}

Console.WriteLine($"Librarian.Loader tests: {passed} passed; {failed.Count} failed.");
foreach (var error in failed)
    Console.Error.WriteLine(error);
return failed.Count == 0 ? 0 : 1;

string Manifest(int schema = 1, string id = "Librarian", string version = "1.3.5", string? betaVersion = "0.111.0",
    string assembly = "Librarian", string stablePath = "compat/0.107.1/Librarian.dll", string? hash = null)
{
    var variants = new List<object>
    {
        new { game_version = "0.107.1", assembly, path = stablePath, sha256 = hash ?? testHash }
    };
    if (betaVersion is not null)
        variants.Add(new { game_version = betaVersion, assembly, path = $"compat/{betaVersion}/Librarian.dll", sha256 = hash ?? testHash });
    return JsonSerializer.Serialize(new { schema_version = schema, mod_id = id, mod_version = version, variants });
}

void Run(string name, Action action)
{
    try
    {
        action();
        passed++;
    }
    catch (Exception exception)
    {
        failed.Add($"FAIL {name}: {exception}");
    }
}

void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}; got {actual}.");
}

void Reject<T>(Action action) where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
