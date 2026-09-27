using Godot;
using HarmonyLib;
using Librarian.LibrarianCode.Relics;
using MegaCrit.Sts2.Core.Models;

namespace Librarian.Mechanics;

/// <summary>Native NRelic supplies black 50% tint; its mask needs the native 85px canvas and ~3px expansion.</summary>
[HarmonyPatch(typeof(RelicModel), "get_IconOutline")]
internal static class LibrarianRelicOutlineBeta3
{
    private static readonly Dictionary<string, Texture2D> Masks = new(StringComparer.Ordinal);
    private static bool Prefix(RelicModel __instance, ref Texture2D __result)
    {
        if (__instance is not LibrarianRelic) return true;
        string path = __instance.PackedIconPath;
        if (!Masks.TryGetValue(path, out var mask))
        {
            using var source = __instance.Icon.GetImage();
            if (source.IsCompressed()) source.Decompress();
            int width = source.GetWidth(), height = source.GetHeight();
            using var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
            // Native relic masks have a round, approximately 3–3.5px silhouette expansion.
            // Preserve the original artwork and let the stock node tint/scale this mask.
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                float alpha = 0;
                for (int dy = -3; dy <= 3; dy++) for (int dx = -3; dx <= 3; dx++)
                    if (dx * dx + dy * dy <= 12 && x + dx >= 0 && x + dx < width && y + dy >= 0 && y + dy < height)
                        alpha = Math.Max(alpha, source.GetPixel(x + dx, y + dy).A);
                image.SetPixel(x, y, new Color(1, 1, 1, alpha));
            }
            mask = ImageTexture.CreateFromImage(image);
            Masks.Add(path, mask);
        }
        __result = mask;
        return false;
    }
}
