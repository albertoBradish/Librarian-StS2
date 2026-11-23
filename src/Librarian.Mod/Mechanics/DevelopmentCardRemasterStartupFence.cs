using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;

namespace Librarian.Mechanics;

internal sealed record LibrarianStartupFenceReceipt(ulong SessionQueuedFrame,ulong CompletedFrame,bool Completed,string TaskStatus);

/// <summary>
/// Opt-in audit observation only. Main-menu visibility and even the native deferred-startup
/// method's return do not mean the queued Common session has finished. Observe the actual
/// existing session and await its native completion; never enqueue a second preload.
/// </summary>
[HarmonyPatch(typeof(AssetCache),nameof(AssetCache.CreateSession))]
internal static class DevelopmentCardRemasterStartupFence
{
    private static readonly TaskCompletionSource<AssetLoadingSession> Common = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static ulong _queuedFrame;

    [HarmonyPostfix]
    private static void Capture(string name,AssetLoadingSession __result)
    {
        if((System.Environment.GetEnvironmentVariable("LIBRARIAN_CARD_REMASTER_AUDIT")!="1"
            && System.Environment.GetEnvironmentVariable("LIBRARIAN_DEPENDENCY_AUDIT")!="1")
            || name!="Common" || Common.Task.IsCompleted) return;
        _queuedFrame=Engine.GetProcessFrames();
        Common.TrySetResult(__result);
    }

    internal static async Task<LibrarianStartupFenceReceipt> WaitForCompletion()
    {
        var session=await Common.Task.WaitAsync(TimeSpan.FromSeconds(60));
        await session.WaitForCompletion().WaitAsync(TimeSpan.FromSeconds(120));
        if(!session.IsCompleted || !session.Task.IsCompletedSuccessfully || !session.Task.Result)
            throw new InvalidOperationException("Native startup Common session did not complete successfully.");
        return new(_queuedFrame,Engine.GetProcessFrames(),session.IsCompleted,session.Task.Status.ToString());
    }
}
