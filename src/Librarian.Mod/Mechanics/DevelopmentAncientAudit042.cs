using System.IO;
using System.Text.Json;
using Godot;
using HarmonyLib;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;

namespace Librarian.Mechanics;

// Explicit opt-in isolated diagnostic; never runs in a player's normal session.
internal static class DevelopmentAncientAudit042
{
    private static void Check(bool ok, string text)
    {
        if (!ok) throw new InvalidOperationException("Ancient042: " + text);
        MainFile.Logger.Info("ANCIENT042_CHECK_PASS " + text);
    }
    private static async Task Wait(double seconds) => await NGame.Instance!.ToSignal(
        NGame.Instance.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    internal static async Task Run()
    {
        Check(OS.GetUserDataDir().Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase), "isolated profile");
        string output = Path.Combine(@"D:\Slay The Spire_Mod Dev", "outputs/revision-v0.4.2/ancient-audit");
        Directory.CreateDirectory(output);
        var progress = SaveManager.Instance.Progress;
        var before = new UnlockState(progress);
        var beforeEpochs = before.ToSerializable().UnlockedEpochs.ToHashSet();
        LibrarianUnlocks040.ApplyChoice(progress, true);
        var modAll = new UnlockState(progress);
        Check(modAll.ToSerializable().UnlockedEpochs.Except(beforeEpochs).All(x => x.StartsWith("LIBRARIAN_")), "mod unlock changes only Librarian epochs");
        var command = new UnlockConsoleCmd().Process(null, ["all"]);
        Check(command.success, "native unlock all command: " + command.msg);
        if (command.task is not null) await command.task;
        var both = new UnlockState(progress);
        var report = new List<object>();
        var baseline = new UnlockState(["NEOW_EPOCH"], [], 1);
        foreach (var (label, state) in new[] { ("minimal",baseline), ("profile-before",before), ("mod-all",modAll), ("both-all",both), ("native-all-reference",UnlockState.all) })
        {
            var counts = new Dictionary<string,int>();
            var firstSeeds = new Dictionary<string,int>();
            for (int seed = 0; seed < 512; seed++)
            {
                var rng = new Rng((ulong)seed);
                var acts = ActModel.GetDefaultList().Select(a => a.ToMutable()).ToList();
                var shared = state.SharedAncients.ToList().UnstableShuffle(rng);
                foreach (var act in acts.Skip(1))
                {
                    var selected = shared.Take(rng.NextInt(shared.Count + 1)).ToList();
                    shared = shared.Except(selected).ToList();
                    act.SetSharedAncientSubset(selected);
                }
                foreach (var act in acts)
                {
                    act.GenerateRooms(rng, state, false);
                    act.ApplyDiscoveryOrderModifications(state);
                    string key = act.Id.Entry + "/" + act.Ancient.Id.Entry;
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                    firstSeeds.TryAdd(key, seed);
                }
            }
            Check(counts.Any(c => c.Key.EndsWith("/VAKUU") && c.Value > 0), label + " naturally rolls Vakuu");
            report.Add(new { label, samples=512, counts, firstSeeds });
            MainFile.Logger.Info("ANCIENT042_GENERATION " + JsonSerializer.Serialize(new {label,counts,firstSeeds}));
        }
        File.WriteAllText(Path.Combine(output,"generation.json"), JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));
        var actual = new Dictionary<string, Dictionary<string,int>>();
        foreach (CharacterModel character in new CharacterModel[] {ModelDb.Character<Ironclad>(),ModelDb.Character<LibrarianCharacter>()})
        {
            var trial = await NGame.Instance!.StartNewSingleplayerRun(character, true,
                ActModel.GetDefaultList(), [], "ANCIENT042CONTROL", GameMode.Standard);
            var counts = new Dictionary<string,int>();
            for (int seed=0;seed<512;seed++)
            {
                trial.Rng.UpFront.LoadFromSerializable(new Rng((ulong)seed).ToSerializable());
                RunManager.Instance.GenerateRooms();
                foreach (var act in trial.Acts)
                {
                    string key=act.Id.Entry+"/"+act.Ancient.Id.Entry;
                    counts[key]=counts.GetValueOrDefault(key)+1;
                }
            }
            actual[character.Id.Entry]=counts;
            MainFile.Logger.Info("ANCIENT042_ACTUAL_RUN_GENERATION "+JsonSerializer.Serialize(new {character=character.Id.Entry,counts}));
            await NGame.Instance.ReturnToMainMenu();
        }
        Check(actual.First().Value.OrderBy(x=>x.Key).SequenceEqual(actual.Last().Value.OrderBy(x=>x.Key)),"actual GenerateRooms matches Ironclad and Librarian for identical 512 RNG states");
        File.WriteAllText(Path.Combine(output,"actual-run-generation.json"),JsonSerializer.Serialize(actual,new JsonSerializerOptions{WriteIndented=true}));
        var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<LibrarianCharacter>(), true,
            ActModel.GetDefaultList(), [], "LIBRARIANANCIENT042", GameMode.Standard);
        Player player = run.Players.Single();
        var entered = new List<object>();
        foreach (var ancient in ModelDb.AllAncients.OrderBy(a => a.Id.Entry))
        {
            run.AppendToMapPointHistory(MapPointType.Ancient, RoomType.Event, ancient.Id);
            var room = new EventRoom(ancient);
            await RunManager.Instance.EnterRoom(room);
            await NGame.Instance.Transition.FadeIn();
            await Wait(1.2);
            var ev = room.LocalMutableEvent;
            Check(ev.Id == ancient.Id && ev.Owner == player, "correct Librarian event owner " + ancient.Id);
            Check(NEventRoom.Instance?.Layout is NAncientEventLayout, "native ancient layout " + ancient.Id);
            Check(ev.CurrentOptions.Count > 0, "generated options " + ancient.Id);
            var layout = (NAncientEventLayout)NEventRoom.Instance!.Layout!;
            // Exercise the same dialogue advance callback as the native click surface.
            var lines = (System.Collections.ICollection)AccessTools.Field(typeof(NAncientEventLayout),"_dialogue").GetValue(layout)!;
            for (int i=0;i<lines.Count;i++)
            {
                AccessTools.Method(typeof(NAncientEventLayout),"OnDialogueHitboxClicked").Invoke(layout,[null]);
                await Wait(0.12);
            }
            await Wait(1.2);
            await NGame.Instance.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            using var image=NGame.Instance.GetViewport().GetTexture().GetImage();
            Check(image.SavePng(Path.Combine(output,ancient.Id.Entry+".png"))==Error.Ok,"screenshot " + ancient.Id);
            entered.Add(new { id=ancient.Id.Entry, options=ev.CurrentOptions.Select(o=>o.TextKey).ToArray(), dialogueLines=lines.Count });
        }
        File.WriteAllText(Path.Combine(output,"entered.json"),JsonSerializer.Serialize(entered,new JsonSerializerOptions {WriteIndented=true}));
        MainFile.Logger.Info("ANCIENT042_AUDIT_PASS entered=" + entered.Count + " generationProfiles=5 seedsPerProfile=512 liveMulticlient=False rewardsNotExhaustivelyTested=True");
    }
}
