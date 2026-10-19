using System.Reflection;
using HarmonyLib;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace Librarian.Mechanics;

/// <summary>
/// Issue #4: RegentCardsAnimeRework v1.0 runs its default-portrait cache on foreign
/// cards. Its disposed texture escapes UpdateVisuals and kills the native draw/turn
/// task. Our cards use our own portraits; bypass only that foreign portrait handler.
/// </summary>
[HarmonyPatch(typeof(NGame), nameof(NGame._Ready))]
internal static class LibrarianPortraitCompatibility1311
{
    private static readonly FieldInfo? CardModelField = AccessTools.Field(typeof(NCard), "_model");
    private static readonly HashSet<MethodBase> Installed = [];
    private static readonly Harmony Harmony = new("Librarian.Compatibility.RegentPortraits1311");

    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void Prefix()
    {
        var type = AccessTools.TypeByName("regentCardsAnimeRework.PortraitPatch");
        if (type is not null) InstallFor(type);
    }

    internal static bool InstallFor(Type type)
    {
        var method = AccessTools.Method(type, "TryReplacePortrait", [typeof(NCard)]);
        if (method is null || CardModelField is null)
        {
            MainFile.Logger.Warn("PORTRAIT_COMPAT1311 unsupported portrait API; no patch applied");
            return false;
        }
        if (Installed.Add(method))
            Harmony.Patch(method, prefix: new HarmonyMethod(typeof(LibrarianPortraitCompatibility1311), nameof(OwnPortrait)));
        return true;
    }

    // Read the backing field: EnterTree can run before NCard.Model is assigned.
    // All other cards keep the third-party mod's normal behavior and diagnostics.
    private static bool OwnPortrait(NCard __0)
        => CardModelField?.GetValue(__0) is not ILibrarianCard;
}
