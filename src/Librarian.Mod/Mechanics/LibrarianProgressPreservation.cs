using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection.Emit;
using HarmonyLib;
using Librarian.LibrarianCode;
using MegaCrit.Sts2.Core.Saves;

namespace Librarian.Mechanics;

/// <summary>Continue a Librarian reset without discarding records absent from the native projection.</summary>
internal static class LibrarianProgressPreservation
{
    private static readonly ConditionalWeakTable<ProgressState, LibrarianProgressDocument> Shadows = new();

    internal static MethodInfo? ProviderMethod(string type, string method) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(assembly => assembly.GetType("STS2RitsuLib.Saves." + type))
        .OfType<Type>().Select(found => found.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic))
        .FirstOrDefault(found => found is not null);

    internal static void Attach(ProgressState progress, string rawJson, string knownJson, ref bool accepted, bool resetPreflight = false)
    {
        Shadows.Remove(progress);
        if (accepted) return;
        try
        {
            var raw = JsonNode.Parse(rawJson) as JsonObject ?? throw new JsonException("Progress must be an object.");
            bool owned = raw["epochs"] is JsonArray epochs && epochs.Any(row =>
                row?["id"] is JsonValue id && id.TryGetValue<string>(out var text)
                && Enumerable.Range(1, 7).Any(n => text == LibrarianUnlocks040.Id(n)));
            if (!owned && !LibrarianDebugReset.Applying && !resetPreflight) return;
            var shadow = new LibrarianProgressDocument(raw, Parse(knownJson));
            // Every unknown field/record must be reconstructed before overriding the provider check.
            // Ambiguous identities and unidentifiable structured arrays still reject the reset.
            shadow.Verify();
            Shadows.Add(progress, shadow);
            accepted = true;
            MainFile.Logger.Info("LIBRARIAN_PROGRESS_SHADOW_ATTACHED missingRecords=" + shadow.MissingRecords);
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException)
        {
            MainFile.Logger.Warn("Librarian progress preservation rejected an ambiguous document; no replacement was installed.");
        }
    }

    internal static bool TryPreserve(ProgressState progress, string knownJson, bool advance, out string content)
    {
        content = knownJson;
        if (!Shadows.TryGetValue(progress, out var shadow)) return false;
        if (!shadow.Matches(knownJson)) return false;
        content = shadow.Preserve(knownJson, advance);
        return true;
    }

    private static JsonObject Parse(string json) => JsonNode.Parse(json) as JsonObject
        ?? throw new JsonException("Progress must be an object.");

    // The game's Harmony emitter cannot reproduce Ritsu's filtered catch clauses.
    // Turn only the reviewed IsRecoverable filter into an equivalent guarded catch;
    // retain the provider body, its state table, and nonrecoverable exception behavior.
    internal static IEnumerable<CodeInstruction> CompatibleFilters(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var source = instructions.ToList();
        for (int i = 0; i < source.Count; i++)
        {
            var instruction = source[i];
            if (!instruction.blocks.Any(block => block.blockType == ExceptionBlockType.BeginExceptFilterBlock))
            { yield return instruction; continue; }
            int end = i + 1;
            while (end < source.Count && source[end].opcode != OpCodes.Endfilter) end++;
            if (end + 1 >= source.Count) throw new InvalidOperationException("Unterminated Ritsu preservation filter.");
            var policyCalls = source.Skip(i).Take(end - i).Where(code => code.operand is MethodInfo method
                && method.Name == "IsRecoverable" && method.DeclaringType?.Name == "RitsuLibExceptionPolicy").ToArray();
            if (policyCalls.Length != 1) throw new InvalidOperationException("Unrecognized Ritsu preservation filter.");
            end++;
            var handler = source[end];
            var next = generator.DefineLabel();
            var guard = new CodeInstruction(OpCodes.Dup);
            guard.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginCatchBlock, typeof(Exception)));
            guard.labels.AddRange(handler.labels); handler.labels.Clear();
            handler.blocks.RemoveAll(block => block.blockType == ExceptionBlockType.BeginCatchBlock);
            yield return guard;
            yield return new CodeInstruction(OpCodes.Call, policyCalls[0].operand);
            yield return new CodeInstruction(OpCodes.Brtrue, next);
            yield return new CodeInstruction(OpCodes.Pop);
            yield return new CodeInstruction(OpCodes.Rethrow);
            handler.labels.Add(next);
            yield return handler;
            i = end;
        }
    }

}

[HarmonyPatch]
internal static class LibrarianProgressAttachPatch
{
    private static MethodInfo? Method => LibrarianProgressPreservation.ProviderMethod("RawProgress.RawProgressJsonPreservation", "TryAttach");
    private static bool Prepare() => Method is not null;
    private static MethodBase TargetMethod() => Method!;
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) =>
        LibrarianProgressPreservation.CompatibleFilters(instructions, generator);
    [HarmonyPostfix]
    private static void Postfix(ProgressState __0, string __1, string __2, ref bool __result) =>
        LibrarianProgressPreservation.Attach(__0, __1, __2, ref __result);
}

[HarmonyPatch]
internal static class LibrarianProgressContinuePatch
{
    private static MethodInfo? Method => LibrarianProgressPreservation.ProviderMethod("RawProgress.RawProgressJsonPreservation", "PreserveAndAdvance");
    private static bool Prepare() => Method is not null;
    private static MethodBase TargetMethod() => Method!;
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) =>
        LibrarianProgressPreservation.CompatibleFilters(instructions, generator);
    [HarmonyPrefix]
    private static bool Prefix(ProgressState __0, string __1, ref string __result)
    {
        if (!LibrarianProgressPreservation.TryPreserve(__0, __1, true, out string content)) return true;
        __result = content;
        return false;
    }
}

[HarmonyPatch]
internal static class LibrarianProgressMirrorPatch
{
    private static MethodInfo? Method => LibrarianProgressPreservation.ProviderMethod("ProgressMirrorStore", "SaveMirror");
    private static bool Prepare() => Method is not null;
    private static MethodBase TargetMethod() => Method!;
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) =>
        LibrarianProgressPreservation.CompatibleFilters(instructions, generator);
    [HarmonyPrefix]
    private static void Prefix(SerializableProgress __0, ref string? __1)
    {
        var progress = SaveManager.Instance.Progress;
        string known = __1 ?? JsonSerializationUtility.ToJson(__0);
        if (LibrarianProgressPreservation.TryPreserve(progress, known, false, out string content)) __1 = content;
    }
}
