using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace Librarian.Loader;

[ModInitializer(nameof(Initialize))]
public static class Bootstrap
{
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
            var manifest = BundleManifest.Read(root);
            manifest.ValidateNativeManifest(root);
            var selected = manifest.Select(ReleaseInfoManager.Instance.ReleaseInfo?.Version);
            var context = AssemblyLoadContext.GetLoadContext(loaderAssembly)
                ?? throw new InvalidOperationException("The Librarian loader has no assembly load context.");
            if (!ReferenceEquals(context, AssemblyLoadContext.GetLoadContext(typeof(ModManager).Assembly)))
                throw new InvalidOperationException("Librarian must load into the game's existing assembly context.");

            if (context.Assemblies.Any(assembly => assembly.GetName().Name == BundleManifest.ModId))
                throw new InvalidOperationException("A Librarian content assembly is already loaded. Remove the duplicate mod and restart the game.");

            using var dependencies = new HostDependencies(context);
            Assembly content;
            // Hold read-only locks for both DLLs through loading. A corrupted or incomplete bundle
            // fails as a whole even when the damaged DLL belongs to the other game target.
            var streams = new List<FileStream>();
            try
            {
                foreach (var variant in manifest.Variants)
                    streams.Add(BundleManifest.OpenVerifiedAssembly(root, variant));
                var path = BundleManifest.ResolveContainedPath(root, selected.Path);
                content = context.LoadFromAssemblyPath(path);
                BundleManifest.ValidateAssemblyIdentity(content.GetName(), selected.Assembly);
                registration.Register(content, selected.GameVersion);
                RegisterWithRitsu(context, content);
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
        catch (Exception exception)
        {
            Gate.Fail();
            var failure = Unwrap(exception);
            registration?.RecordFailure(failure);
            Log.Error($"[Librarian.Loader] Content initialization failed: {failure}");
            ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
    }

    private static void RegisterWithRitsu(AssemblyLoadContext context, Assembly content)
    {
        var runtime = context.Assemblies.SingleOrDefault(assembly => assembly.GetName().Name == "STS2-RitsuLib.Runtime")
            ?? throw new InvalidOperationException("RitsuLib must be loaded before Librarian. Install RitsuLib 0.6.4 or newer.");
        var hub = runtime.GetType("STS2RitsuLib.Interop.ModTypeDiscoveryHub", true)!;
        var register = hub.GetMethod("RegisterModAssembly", BindingFlags.Public | BindingFlags.Static,
            null, [typeof(string), typeof(Assembly)], null)
            ?? throw new MissingMethodException(hub.FullName, "RegisterModAssembly");
        register.Invoke(null, [BundleManifest.ModId, content]);
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
