using BaseLib.Utils;
using Godot;
using HarmonyLib;
using Librarian.LibrarianCode;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace Librarian.Mechanics;

/// <summary>
/// Route the native merchant entry points for this character's non-Spine scene.
/// BaseLib auto-conversion does not mark its factory flag and its flexible merchant
/// structure retains the original scene as a nested child. Detect our component recursively.
/// </summary>
[HarmonyPatch]
internal static class LibrarianMerchantFactoryCompatibility
{
    internal static LibrarianCharacterMotion? FindMotion(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is LibrarianCharacterMotion motion) return motion;
            if (FindMotion(child) is { } nested) return nested;
        }
        return null;
    }

    [HarmonyPatch(typeof(NMerchantCharacter), nameof(NMerchantCharacter._Ready))]
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Ready(NMerchantCharacter __instance)
    {
        if (FindMotion(__instance) is not { } motion) return true;
        MainFile.Logger.Info($"LIBRARIAN_MERCHANT_ROUTE root={__instance.Name} motion={motion.GetPath()} animator={motion.HasNode("AnimationPlayer")}");
        PlayOwned(motion, "Idle");
        return false;
    }

    [HarmonyPatch(typeof(NMerchantCharacter), nameof(NMerchantCharacter.PlayAnimation))]
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Play(NMerchantCharacter __instance, string anim, bool loop)
    {
        if (FindMotion(__instance) is not { } motion) return true;
        PlayOwned(motion, anim);
        return false;
    }

    private static void PlayOwned(LibrarianCharacterMotion motion, string requested)
    {
        // Our own component is the identity boundary. Its _Ready replaces the old
        // body texture and may attach the current layered rig before the merchant
        // parent's _Ready runs; a historical texture path cannot identify it.
        // Native characters and other mods without this component retain their
        // original Spine/factory route.
        var player = motion.GetNode<AnimationPlayer>("AnimationPlayer");
        var selected = requested switch { "relaxed_loop" => "Idle", "die" => "Dead", _ => requested };
        if (!player.HasAnimation(selected)) selected = "Idle";
        // Scope discovery to our component rather than other animations that a
        // factory or another mod may add to the enclosing merchant character.
        if (!CustomAnimation.PlayCustomAnimation(motion, selected))
            throw new InvalidOperationException("Librarian merchant custom animation could not be discovered: " + selected);
    }
}
