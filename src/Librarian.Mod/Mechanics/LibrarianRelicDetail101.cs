using Godot;
using HarmonyLib;
using Librarian.LibrarianCode.Relics;
using MegaCrit.Sts2.Core.Models;

namespace Librarian.Mechanics;

/// <summary>Native large relics include their opaque outline in the 256px image.</summary>
[HarmonyPatch(typeof(RelicModel), "get_BigIcon")]
internal static class LibrarianRelicDetail101
{
    private static readonly Dictionary<string, Texture2D> Cache = new(StringComparer.Ordinal);
    private static void Postfix(RelicModel __instance, ref Texture2D __result)
    {
        if (__instance is not LibrarianRelic) return;
        string key = __instance.PackedIconPath;
        if (Cache.TryGetValue(key, out var cached)) { __result = cached; return; }
        using var source = __result.GetImage();
        if (source.IsCompressed()) source.Decompress();
        var bounds = source.GetUsedRect();
        using var art = source.GetRegion(bounds);
        // Match native large icons: nearly fill the canvas, leaving room for a 9px outline.
        float scale = 236f / Math.Max(bounds.Size.X, bounds.Size.Y);
        art.Resize(Math.Max(1,(int)Math.Round(bounds.Size.X*scale)), Math.Max(1,(int)Math.Round(bounds.Size.Y*scale)), Image.Interpolation.Lanczos);
        using var centered = Image.CreateEmpty(256,256,false,Image.Format.Rgba8);
        centered.BlitRect(art,new Rect2I(0,0,art.GetWidth(),art.GetHeight()),new Vector2I((256-art.GetWidth())/2,(256-art.GetHeight())/2));
        using var result = Image.CreateEmpty(256,256,false,Image.Format.Rgba8);
        for(int y=0;y<256;y++) for(int x=0;x<256;x++)
        {
            float a=0;
            for(int dy=-9;dy<=9;dy++) for(int dx=-9;dx<=9;dx++)
                if(dx*dx+dy*dy<=81 && x+dx>=0 && x+dx<256 && y+dy>=0 && y+dy<256)
                    a=Math.Max(a,centered.GetPixel(x+dx,y+dy).A);
            result.SetPixel(x,y,new Color(.025f,.055f,.055f,a));
        }
        result.BlendRect(centered,new Rect2I(0,0,256,256),Vector2I.Zero);
        __result=ImageTexture.CreateFromImage(result);
        Cache.Add(key,__result);
    }
}
