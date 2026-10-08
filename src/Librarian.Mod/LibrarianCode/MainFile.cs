using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using Librarian.LibrarianCode.Integration;

namespace Librarian.LibrarianCode;

//You're recommended but not required to keep all your code in this package and all your assets in the Librarian folder.
[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "Librarian"; //Used for resource filepath
    public const string ResPath = $"res://{ModId}";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } = new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        var assembly = Assembly.GetExecutingAssembly();

        //If you want to use scripts defined in your mod for Godot scenes, uncomment the following line.
        Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(assembly);
     
        Harmony harmony = new(ModId);

        Librarian.Mechanics.LibrarianUnlocks040.Initialize();
        Librarian.Mechanics.LibrarianSettings041.Initialize();
        harmony.PatchAll(assembly);
        Librarian.Mechanics.LibrarianRitsuCardRegistration.Initialize();
        Librarian.Mechanics.LibrarianRuntime.Initialize();
        Logger.Info("Librarian character skeleton initialized. Orb mechanics connected=" + LibrarianMechanicsBridge.IsConnected);
    }
}
