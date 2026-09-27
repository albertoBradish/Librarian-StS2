using Godot;
using HarmonyLib;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using System.Runtime.CompilerServices;

namespace Librarian.Mechanics;

/// <summary>Versioned character UI assets; no card portrait or native texture is replaced.</summary>
public static class LibrarianVisualTheme
{
    public static readonly Color ThemeColor = new("EBA466");
    public const string BigEnergyIconPath = "res://Librarian/images/charui/v0.3.5/energy_triangle.svg";
    public const string TextEnergyIconPath = "res://Librarian/images/charui/v0.3.5/energy_triangle_text.svg";
    public const string EnergyCounterPath = "res://Librarian/scenes/charui/v0.3.5/energy_counter.tscn";
    private static ShaderMaterial? _frameMaterial;

    internal static ShaderMaterial FrameMaterial => _frameMaterial ??= new ShaderMaterial
    {
        Shader = GD.Load<Shader>("res://Librarian/shaders/v0.3.5/card_frame_orange.gdshader")
    };
}

[HarmonyPatch(typeof(CardPoolModel), nameof(CardPoolModel.FrameMaterial), MethodType.Getter)]
internal static class LibrarianCardFrameThemePatch
{
    [HarmonyPostfix]
    private static void ApplyTheme(CardPoolModel __instance, ref Material __result)
    {
        if (__instance is LibrarianCardPool)
            __result = LibrarianVisualTheme.FrameMaterial;
    }
}

[HarmonyPatch(typeof(NCard), "Reload")]
internal static class LibrarianEnergyLabelAlignmentPatch
{
    private sealed record OriginalOffsets(float Top, float Bottom);
    private static readonly ConditionalWeakTable<NCard, OriginalOffsets> Offsets = new();

    [HarmonyPostfix]
    private static void AlignLabel(NCard __instance)
    {
        if (!__instance.IsNodeReady() || __instance.Model == null ||
            __instance.GetNodeOrNull<Control>("%EnergyLabel") is not { } label)
            return;
        var original = Offsets.GetValue(__instance, _ => new OriginalOffsets(label.OffsetTop, label.OffsetBottom));
        // Match the triangle's optical center. Restore pooled nodes for other characters.
        var shift = __instance.Model.VisualCardPool is LibrarianCardPool ? -6f : 0f;
        label.OffsetTop = original.Top + shift;
        label.OffsetBottom = original.Bottom + shift;
    }
}
