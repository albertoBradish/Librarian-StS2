using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Rngs;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace Librarian.Mechanics;

/// <summary>API-specific deterministic fixture setup; never called by gameplay.</summary>
internal static class DevelopmentGameApi
{
#if LIBRARIAN_API_0107
    internal readonly record struct RngSnapshot(uint Seed, int Counter);
#else
    internal readonly record struct RngSnapshot(SerializableRng State);
#endif
    internal static Rng CreateRng(ulong seed)
#if LIBRARIAN_API_0107
        => new(checked((uint)seed));
#else
        => new(seed);
#endif
    internal static RngSnapshot SnapshotRng(Rng rng)
#if LIBRARIAN_API_0107
        => new(rng.Seed, rng.Counter);
#else
        => new(rng.ToSerializable());
#endif

    internal static void RestoreRng(RunRngSet set, RunRngType type, RngSnapshot snapshot)
    {
        if (Environment.GetEnvironmentVariable("LIBRARIAN_RUNTIME_AUDIT") != "1")
            throw new InvalidOperationException("RNG fixture requires explicit runtime audit.");
#if LIBRARIAN_API_0107
        // Stable's native serializable RNG consists of the seed and counter;
        // replace only the fixture stream, as in the verified stable audits.
        var streams = (Dictionary<RunRngType, Rng>)AccessTools.Field(typeof(RunRngSet), "_rngs").GetValue(set)!;
        streams[type] = new Rng(snapshot.Seed, snapshot.Counter);
#else
        var stream = type switch
        {
            RunRngType.CombatTargets => set.CombatTargets,
            RunRngType.UpFront => set.UpFront,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        stream.LoadFromSerializable(snapshot.State);
#endif
    }
}
