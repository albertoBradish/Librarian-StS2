#if LIBRARIAN_API_0113
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Timeline;
using MegaCrit.Sts2.Core.Timeline.Epochs;

namespace Librarian.Mechanics;

[HarmonyPatch(typeof(ProgressSaveManager), "UpdateEpochsPostRun")]
internal static class LibrarianDreadUnlock1410
{
    private static bool HasOriginalA10(ProgressSaveManager manager)
    {
        var progress = manager.Progress;
        return progress.GetStatsForCharacter<Ironclad>() is { MaxAscension: >= 10 }
            && progress.GetStatsForCharacter<Silent>() is { MaxAscension: >= 10 }
            && progress.GetStatsForCharacter<Regent>() is { MaxAscension: >= 10 }
            && progress.GetStatsForCharacter<Necrobinder>() is { MaxAscension: >= 10 }
            && progress.GetStatsForCharacter<Defect>() is { MaxAscension: >= 10 }
            && progress.GetStatsForCharacter<Fool>() is { MaxAscension: >= 10 };
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> ReplaceDreadRequirements(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var dreadGet = SymbolExtensions.GetMethodInfo(() => EpochModel.Get<DreadEpoch>());
        var modeLock = AccessTools.Method(typeof(GameModeHelper), nameof(GameModeHelper.AreAchievementsAndEpochsLocked));
        var allStats = SymbolExtensions.GetMethodInfo(() =>
            Enumerable.All(Array.Empty<CharacterStats>(), (Func<CharacterStats, bool>)null!));

        // Both 0.113 builds short-circuit the count, All and mode checks to
        // the next epoch check. Harmony can assign a distinct label per branch.
        var getDread = code.FindIndex(i => i.Calls(dreadGet));
        const int predicateLength = 14;
        int start = getDread - 22;
        int all = start + predicateLength - 1;
        int nextEpochCheck = getDread + 10;
        if (start < 1 || nextEpochCheck >= code.Count || code.FindLastIndex(i => i.Calls(dreadGet)) != getDread
            || code[start - 1].opcode != OpCodes.Pop
            || code[start].opcode != OpCodes.Ldloc_3 || code[start + 1].opcode != OpCodes.Ldloc_1
            || (code[start + 2].opcode != OpCodes.Blt_S && code[start + 2].opcode != OpCodes.Blt)
            || code[start + 3].opcode != OpCodes.Ldloc_2 || code[start + 4].opcode != OpCodes.Ldsfld
            || code[start + 5].opcode != OpCodes.Dup || !IsBranchTrue(code[start + 6])
            || code[start + 7].opcode != OpCodes.Pop || code[start + 8].opcode != OpCodes.Ldsfld
            || code[start + 9].opcode != OpCodes.Ldftn || code[start + 10].opcode != OpCodes.Newobj
            || code[start + 11].opcode != OpCodes.Dup || code[start + 12].opcode != OpCodes.Stsfld
            || !code[all].Calls(allStats)
            || (code[all + 1].opcode != OpCodes.Brfalse_S && code[all + 1].opcode != OpCodes.Brfalse)
            || !code[getDread - 3].Calls(modeLock) || !IsBranchTrue(code[getDread - 2])
            || !BranchesTo(code[start + 2], code[nextEpochCheck])
            || !BranchesTo(code[all + 1], code[nextEpochCheck])
            || !BranchesTo(code[getDread - 2], code[nextEpochCheck])
            || !BranchesTo(code[getDread + 4], code[nextEpochCheck])
            || code.Skip(start).Take(predicateLength).Any(i => i.blocks.Count != 0)
            || code.Skip(start + 1).Take(predicateLength - 2).Any(i => i.labels.Count != 0)
            || code[all].labels.Count != 1 || !Equals(code[start + 6].operand, code[all].labels[0])
            || code.Count(i => Equals(i.operand, code[all].labels[0])) != 1
            || code.Any(i => i.operand is Label[] targets && targets.Contains(code[all].labels[0])))
        {
            Log.Warn("Librarian could not identify the native Dread A10 gate for this build.");
            return code;
        }

        // Leave the native mode guard, post-run discovery and slot override in place.
        var loadManager = new CodeInstruction(OpCodes.Ldarg_0);
        loadManager.labels.AddRange(code[start].labels);
        var check = new CodeInstruction(OpCodes.Call,
            AccessTools.Method(typeof(LibrarianDreadUnlock1410), nameof(HasOriginalA10)));
        code.RemoveRange(start, predicateLength);
        code.InsertRange(start, [loadManager, check]);
        return code;
    }

    private static bool IsBranchTrue(CodeInstruction instruction)
        => instruction.opcode == OpCodes.Brtrue_S || instruction.opcode == OpCodes.Brtrue;

    private static bool BranchesTo(CodeInstruction branch, CodeInstruction destination)
        => branch.operand is Label label && destination.labels.Contains(label);
}
#endif
