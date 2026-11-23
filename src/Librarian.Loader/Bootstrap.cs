using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using Godot;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace Librarian.Loader;

[ModInitializer(nameof(Initialize))]
public static class Bootstrap
{
    private const string RitsuId = "STS2-RitsuLib";
    private static readonly InitializationGate Gate = new();

    /// <summary>Selects one verified assembly in the host context before any content initializer runs.</summary>
    public static void Initialize()
    {
        if (!Gate.Begin())
            return;

        GameAssemblyRegistration? registration = null;
        try
        {
            var loaderAssembly = typeof(Bootstrap).Assembly;
            var root = Path.GetDirectoryName(loaderAssembly.Location)
                ?? throw new InvalidOperationException("Cannot locate the Librarian loader directory.");
            registration = GameAssemblyRegistration.Begin(root);
            var label = ReleaseInfoManager.Instance.ReleaseInfo?.Version;
            var game = BundleManifest.SupportedGameVersion(label);
            if (game is null)
            {
                registration.Skip();
                Gate.Complete();
                CompatibilityNotice.Queue(label, unsupportedGame: true);
                return;
            }

            var context = AssemblyLoadContext.GetLoadContext(loaderAssembly)
                ?? throw new InvalidOperationException("The Librarian loader has no assembly load context.");
            if (!ReferenceEquals(context, AssemblyLoadContext.GetLoadContext(typeof(ModManager).Assembly)))
                throw new InvalidOperationException("Librarian must load into the game's existing assembly context.");

            var owner = registration;
            var framework = ModManager.Mods.FirstOrDefault(mod => mod.manifest?.id == RitsuId
                && mod.state is ModLoadState.None or ModLoadState.Loaded);
            if (framework?.state == ModLoadState.None)
            {
                // The native manifest cannot declare a hard dependency: that would suppress
                // this bootstrap when the framework is absent, disabled or broken.
                void WhenFrameworkDetected(Mod detected)
                {
                    if (!ReferenceEquals(detected, framework)) return;
                    ModManager.OnModDetected -= WhenFrameworkDetected;
                    try { StartContent(owner, context, root, game, framework); }
                    catch (Exception exception) { HandleFailure(owner, exception); }
                }
                ModManager.OnModDetected += WhenFrameworkDetected;
                return;
            }

            StartContent(owner, context, root, game, framework);
        }
        catch (Exception exception)
        {
            var failure = HandleFailure(registration, exception);
            ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
    }

    private static void StartContent(GameAssemblyRegistration registration, AssemblyLoadContext context,
        string root, string game, Mod? framework)
    {
        var register = FindRitsuRegistration(context, framework);
        if (register is null)
        {
            registration.Skip();
            Gate.Complete();
            CompatibilityNotice.Queue(game, unsupportedGame: false);
            return;
        }

        var manifest = BundleManifest.Read(root);
        manifest.ValidateNativeManifest(root);
        var selected = manifest.Select(game);
        if (context.Assemblies.Any(assembly => assembly.GetName().Name == BundleManifest.ModId))
            throw new InvalidOperationException("A Librarian content assembly is already loaded. Remove the duplicate mod and restart the game.");

        using var dependencies = new HostDependencies(context);
        var streams = new List<FileStream>();
        try
        {
            foreach (var variant in manifest.Variants)
                streams.Add(BundleManifest.OpenVerifiedAssembly(root, variant));
            var pack = BundleManifest.ResolveContainedPath(root, "Librarian.pck");
            if (!File.Exists(pack) || !ProjectSettings.LoadResourcePack(pack, replaceFiles: true))
                throw new InvalidDataException("The Librarian resource pack could not be mounted.");
            var path = BundleManifest.ResolveContainedPath(root, selected.Path);
            var content = context.LoadFromAssemblyPath(path);
            BundleManifest.ValidateAssemblyIdentity(content.GetName(), selected.Assembly);
            registration.Register(content, selected.GameVersion);
            register.Invoke(null, [BundleManifest.ModId, content]);
            InvokeContentInitializer(content);
        }
        finally
        {
            foreach (var stream in streams)
                stream.Dispose();
        }

        Gate.Complete();
        Log.Info($"[Librarian.Loader] Initialized Librarian {manifest.Version}; host={selected.GameVersion}; content={selected.Path}; sha256={selected.Sha256}.");
    }

    private static MethodInfo? FindRitsuRegistration(AssemblyLoadContext context, Mod? framework)
    {
        if (framework?.state != ModLoadState.Loaded)
            return null;
        try
        {
            var runtime = context.Assemblies.SingleOrDefault(assembly => assembly.GetName().Name == "STS2-RitsuLib.Runtime");
            if (!BundleManifest.IsRitsuVersionSupported(runtime?.GetName().Version))
                return null;
            if (runtime!.GetType("STS2RitsuLib.RitsuLibFramework")?.GetProperty("IsInitialized")?.GetValue(null) is not true)
                return null;
            return runtime.GetType("STS2RitsuLib.Interop.ModTypeDiscoveryHub")?.GetMethod("RegisterModAssembly",
                BindingFlags.Public | BindingFlags.Static, null, [typeof(string), typeof(Assembly)], null);
        }
        catch (Exception exception)
        {
            Log.Warn($"[Librarian.Loader] RitsuLib readiness check did not complete: {Unwrap(exception).GetType().Name}.");
            return null;
        }
    }

    private static Exception HandleFailure(GameAssemblyRegistration? registration, Exception exception)
    {
        Gate.Fail();
        var failure = Unwrap(exception);
        registration?.RecordFailure(failure);
        Log.Error($"[Librarian.Loader] Content initialization failed: {failure}");
        return failure;
    }

    private static void InvokeContentInitializer(Assembly content)
    {
        var entry = content.GetType("Librarian.LibrarianCode.MainFile", true)!;
        var initialize = entry.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null)
            ?? throw new MissingMethodException(entry.FullName, "Initialize");
        if (initialize.ReturnType != typeof(void))
            throw new InvalidDataException("Librarian's content initializer must return void.");
        // MainFile retains Godot script discovery followed by the existing content setup order.
        initialize.Invoke(null, null);
    }

    private static Exception Unwrap(Exception exception)
    {
        while (exception is TargetInvocationException { InnerException: not null } invocation)
            exception = invocation.InnerException!;
        return exception;
    }

    private sealed class HostDependencies : IDisposable
    {
        private readonly AssemblyLoadContext _context;

        internal HostDependencies(AssemblyLoadContext context)
        {
            _context = context;
            context.Resolving += Resolve;
        }

        private Assembly? Resolve(AssemblyLoadContext context, AssemblyName requested)
        {
            var name = requested.Name;
            if (name is not ("sts2" or "GodotSharp" or "GodotSharpEditor" or "0Harmony") &&
                name?.StartsWith("STS2-RitsuLib", StringComparison.Ordinal) != true)
                return null;
            // Reuse only the host's already loaded dependency. Never probe or load another target's
            // framework modules, a second game DLL, or a dependency adjacent to the content DLL.
            return context.Assemblies.SingleOrDefault(assembly => assembly.GetName().Name == name)
                ?? throw new FileNotFoundException($"Librarian dependency '{name}' was not loaded by the host.");
        }

        public void Dispose() => _context.Resolving -= Resolve;
    }
}
