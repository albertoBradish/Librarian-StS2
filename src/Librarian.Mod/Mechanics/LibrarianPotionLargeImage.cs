using Godot;
using HarmonyLib;
using Librarian.LibrarianCode.Potions;
using MegaCrit.Sts2.Core.Models;

namespace Librarian.Mechanics;

// RitsuLib 0.6.4 exposes the packed image and outline; preserve the existing large artwork separately.
[HarmonyPatch(typeof(PotionModel), nameof(PotionModel.LargeImagePath), MethodType.Getter)]
internal static class LibrarianPotionLargeImagePath
{
    [HarmonyPrefix]
    private static bool Prefix(PotionModel __instance, ref string __result)
    {
        if (__instance is not LibrarianPotion potion) return true;
        __result = potion.CustomLargeImagePath!;
        return false;
    }
}
