using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Rngs;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
namespace Librarian.Mechanics;
internal static class DevelopmentStableRng
{
    // Test fixture only: replace a stream in the disposable native run, never production RNG.
    internal static void Set(RunRngSet set, RunRngType type, uint seed, int counter)
    {
        if (Environment.GetEnvironmentVariable("LIBRARIAN_RUNTIME_AUDIT") != "1")
            throw new InvalidOperationException("Stable RNG fixture requires explicit runtime audit.");
        var streams = (Dictionary<RunRngType, Rng>)AccessTools.Field(typeof(RunRngSet), "_rngs").GetValue(set)!;
        streams[type] = new Rng(seed, counter);
    }
}
