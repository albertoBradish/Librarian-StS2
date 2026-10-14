using System.Collections;
using System.Reflection;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace Librarian.Loader;

/// <summary>Repairs stable's post-initializer assignment and preserves native error reporting on both hosts.</summary>
internal sealed class GameAssemblyRegistration
{
    private readonly Mod _owner;
    private Assembly? _content;
    private Exception? _failure;
    private bool _subscribed;

    private GameAssemblyRegistration(Mod owner)
    {
        _owner = owner;
        ModManager.OnModDetected += OnModDetected;
        _subscribed = true;
    }

    internal static GameAssemblyRegistration Begin(string loaderDirectory)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var fullDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(loaderDirectory));
        var owner = ModManager.Mods.SingleOrDefault(mod =>
            mod.manifest?.id == BundleManifest.ModId &&
            string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(mod.path)), fullDirectory, comparison))
            ?? throw new InvalidOperationException("The game has no Librarian manifest for this loader directory.");
        return new GameAssemblyRegistration(owner);
    }

    internal void Register(Assembly content, string gameVersion)
    {
        _content = content;
        if (gameVersion == "0.107.1")
        {
            // This host writes mod.assembly = loader after its initializer returns. Replace it only
            // in OnModDetected, which runs before localization/model/AssemblyInfo discovery.
            if (typeof(Mod).GetField("assembly") is null)
                throw new MissingFieldException(typeof(Mod).FullName, "assembly");
            return;
        }

        var associate = typeof(ModManager).GetMethod("AssociateAssemblyWithMod", BindingFlags.Public | BindingFlags.Static,
            null, [typeof(string), typeof(Assembly)], null)
            ?? throw new MissingMethodException(typeof(ModManager).FullName, "AssociateAssemblyWithMod");
        associate.Invoke(null, [BundleManifest.ModId, content]);
        if (!ContainsContentAssembly(_owner, content))
            throw new InvalidOperationException("The game did not associate the selected Librarian content assembly with its mod.");
    }

    internal void RecordFailure(Exception exception) => _failure = exception;

    private void OnModDetected(Mod detected)
    {
        if (!ReferenceEquals(detected, _owner))
            return;
        try
        {
            if (_failure is not null)
            {
                MarkFailed(detected, _failure);
                return;
            }
            if (_content is null || detected.state != ModLoadState.Loaded)
                throw new InvalidOperationException("Librarian content initialization did not finish successfully.");

            var stableField = typeof(Mod).GetField("assembly");
            if (stableField is not null)
                stableField.SetValue(detected, _content);
            else if (!ContainsContentAssembly(detected, _content))
                throw new InvalidOperationException("Librarian content assembly registration was lost before discovery.");
            Log.Info($"[Librarian.Loader] Native mod registration complete: {_content.FullName}.");
        }
        catch (Exception exception)
        {
            MarkFailed(detected, exception);
            Log.Error($"[Librarian.Loader] Native mod registration failed: {exception}");
        }
        finally
        {
            if (_subscribed)
            {
                ModManager.OnModDetected -= OnModDetected;
                _subscribed = false;
            }
        }
    }

    private static bool ContainsContentAssembly(Mod mod, Assembly content)
    {
        var value = typeof(Mod).GetField("assemblies")?.GetValue(mod);
        return value is IEnumerable assemblies && assemblies.Cast<object?>().Any(item => ReferenceEquals(item, content));
    }

    private static void MarkFailed(Mod mod, Exception exception)
    {
        // Native CallModInitializer catches exceptions and still assigns Loaded. The final event is
        // the first stable point at which a failed content initializer can keep the mod disabled.
        mod.state = ModLoadState.Failed;
        var error = new LocString("main_menu_ui", "MOD_ERROR.EXCEPTION");
        error.Add("id", BundleManifest.ModId);
        error.Add("exceptionType", $"{exception.GetType().Name}: {exception.Message}");
        (mod.errors ??= []).Add(error);
    }
}
