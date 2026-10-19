using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Environment = System.Environment;
using Godot;
using HarmonyLib;
using Librarian.LibrarianCode;
using Librarian.LibrarianCode.Cards;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using STS2RitsuLib.Content;
using STS2RitsuLib.Settings;

namespace Librarian.Mechanics;

/// <summary>Opt-in unified-package menu gate; uses only explicitly isolated audit profiles.</summary>
[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class DevelopmentUnified130Audit
{
    private static bool _ran;
    private static int _checks;
    private static readonly TaskCompletionSource<Task<bool>> CommonPreloadStarted = new();

    // The menu becomes ready before Common is enqueued. LoadCommonAndMainMenuAssets
    // itself only waits for enqueueing; this task completes after all native loads finalize.
    [HarmonyPatch(typeof(NAssetLoader), nameof(NAssetLoader.LoadInTheBackground))]
    private static class ObserveCommonPreload
    {
        private static void Postfix(AssetLoadingSession session)
        {
            if (Environment.GetEnvironmentVariable("LIBRARIAN_V130_AUDIT") != "1"
                || Environment.GetEnvironmentVariable("LIBRARIAN_RUNTIME_AUDIT") != "1") return;
            if (AccessTools.Field(typeof(AssetLoadingSession), "_name")?.GetValue(session) is "Common")
                CommonPreloadStarted.TrySetResult(session.Task);
        }
    }

    internal static bool IsApprovedUnifiedProfile()
    {
        string profile = Path.GetFullPath(OS.GetUserDataDir()).Replace('\\', '/');
        return Environment.GetEnvironmentVariable("LIBRARIAN_RUNTIME_AUDIT") == "1"
            && Environment.GetEnvironmentVariable("LIBRARIAN_V130_AUDIT") == "1"
            && profile.Contains("/.research/unified-v1.3.7/", StringComparison.OrdinalIgnoreCase)
            && profile.Contains("revision030-userdata", StringComparison.OrdinalIgnoreCase);
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        if (_ran || Environment.GetEnvironmentVariable("LIBRARIAN_V130_AUDIT") != "1") return;
        _ran = true;
        Callable.From((Action)(() => { _ = Run(); })).CallDeferred();
    }

    private static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException("V130 prototype: " + label);
        _checks++;
    }

    private static IEnumerable<Node> Desc(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            yield return child;
            foreach (var nested in Desc(child)) yield return nested;
        }
    }

    private static async Task Run()
    {
        string mode = Environment.GetEnvironmentVariable("LIBRARIAN_V130_AUDIT_MODE") ?? "models-only";
        bool quit = mode == "models-only";
        Control? fixture = null;
        try
        {
            Check(mode is "models-only" or "observe", "explicit model-only or concurrent observe mode");
            Check(IsApprovedUnifiedProfile(), "authorized unified isolated profile");
            string output = Environment.GetEnvironmentVariable("LIBRARIAN_AUDIT_OUTPUT")
                ?? throw new InvalidOperationException("V130 audit output is required");
            Directory.CreateDirectory(output);
            var content = typeof(MainFile).Assembly;
            string expectedGame =
#if LIBRARIAN_API_0107
                "0.107.1";
#else
                "0.111.0";
#endif
            string nativeGameVersion = LibrarianGameApi.GameVersion;
            string gameVersion = nativeGameVersion.StartsWith('v') || nativeGameVersion.StartsWith('V')
                ? nativeGameVersion[1..] : nativeGameVersion;
            Check(gameVersion == expectedGame, "fixed native game version: " + nativeGameVersion);
            Check(content.GetName().Name == "Librarian" && content.GetName().Version == new Version(1, 3, 7, 0), "stable content identity and unified assembly version");
            string contentPath = Path.GetFullPath(content.Location).Replace('\\', '/');
            Check(contentPath.EndsWith("/compat/" + expectedGame + "/Librarian.dll", StringComparison.OrdinalIgnoreCase), "correct game-specific content path");
            var contentAssemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "Librarian").ToArray();
            Check(contentAssemblies.Length == 1 && ReferenceEquals(contentAssemblies[0], content), "only selected content assembly is loaded");
            var loaders = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "Librarian.Loader").ToArray();
            Check(loaders.Length == 1, "one bootstrap assembly");
            var mods = ModManager.GetLoadedMods().Where(m => m.manifest?.id == MainFile.ModId).ToArray();
            Check(mods.Length == 1, "one loaded native mod entry");
#if LIBRARIAN_API_0107
            Check(ReferenceEquals(mods[0].assembly, content), "native mod owns selected content assembly");
            var nativeModAssemblies = new[] { mods[0].assembly!.GetName().Name };
#else
            Check(mods[0].assemblies.Contains(content), "native mod owns selected content assembly");
            var nativeModAssemblies = mods[0].assemblies.Select(a => a.GetName().Name).ToArray();
#endif
            Check(LibrarianUpdateNotice051.CurrentVersion == "1.3.7" && mods[0].manifest?.version == "1.3.7", "uniform version is read from ModId manifest");
            Check(!content.GetReferencedAssemblies().Any(a => a.Name == "BaseLib")
                && !AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "BaseLib"), "Ritsu-only reference and loaded process");

            var hub = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("STS2RitsuLib.Interop.ModTypeDiscoveryHub")).FirstOrDefault(t => t is not null)
                ?? throw new InvalidOperationException("Ritsu discovery hub was not loaded");
            object?[] arguments = [content, null];
            bool registered = (bool)(hub.GetMethod("TryResolveRegisteredModId", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(hub.FullName, "TryResolveRegisteredModId")).Invoke(null, arguments)!;
            Check(registered && (string?)arguments[1] == MainFile.ModId, "Ritsu discovery owns the content assembly");
            var models = new List<object>();
            Type[] characterPoolTypes = [typeof(LibrarianCardPool), typeof(LibrarianRelicPool), typeof(LibrarianPotionPool)];
            int explicitOwners = 0;
            int assemblyOwners = 0;
            Check(LibrarianRitsuCardRegistration.LegacyEntries.Count == 149, "149 original model mappings");
            foreach (var (type, entry) in LibrarianRitsuCardRegistration.LegacyEntries)
            {
                var model = ModelDb.GetById<AbstractModel>(ModelDb.GetId(type));
                Check(ModelDb.GetEntry(type) == entry && model.GetType() == type && ReferenceEquals(type.Assembly, content), "original model identity " + type.Name);
                bool hasRegistryOwner = ModContentRegistry.TryGetOwnerModId(type, out string? registryOwner);
                bool characterPool = characterPoolTypes.Contains(type);
                if (characterPool)
                {
                    // RegisterPoolModel owns its contributed model, not the pool type.
                    // Character pools are discovered through their native-owned content assembly.
                    Check(!hasRegistryOwner || registryOwner == MainFile.ModId, "no foreign Ritsu character pool ownership " + type.Name);
                    assemblyOwners++;
                }
                else
                {
                    Check(hasRegistryOwner && registryOwner == MainFile.ModId, "Ritsu model ownership " + type.Name);
                    explicitOwners++;
                }
                models.Add(new { type = type.FullName, id = model.Id.ToString(), owner = MainFile.ModId,
                    ownership_source = characterPool ? "native-model-and-registered-content-assembly" : "explicit-content-registry",
                    registry_owner = registryOwner });
            }
            Check(explicitOwners == 146 && assemblyOwners == 3, "146 explicit model owners and three native character pools");
            var character = ModelDb.Character<LibrarianCharacter>();
            Check(ReferenceEquals(character.CardPool, ModelDb.CardPool<LibrarianCardPool>())
                && ReferenceEquals(character.RelicPool, ModelDb.RelicPool<LibrarianRelicPool>())
                && ReferenceEquals(character.PotionPool, ModelDb.PotionPool<LibrarianPotionPool>()), "native character owns its three discovered pools");
            var cards = LibrarianRitsuCardRegistration.LegacyEntries.Keys.Where(t => typeof(CardModel).IsAssignableFrom(t))
                .Select(t => (CardModel)ModelDb.GetById<AbstractModel>(ModelDb.GetId(t))).OrderBy(c => c.Id.Entry).ToArray();
            Check(cards.Length == 100 && ModelDb.CardPool<LibrarianCardPool>().AllCards.Count() == 91, "100 card identities and 91 active cards");
            Check(LibrarianRitsuCardRegistration.LegacyEntries.Keys.Count(t => typeof(RelicModel).IsAssignableFrom(t)) == 9
                && ModelDb.RelicPool<LibrarianRelicPool>().AllRelics.Count() == 8
                && ModelDb.PotionPool<LibrarianPotionPool>().AllPotions.Count() == 3, "nine relic identities including event relic and three potions");
            foreach (string page in new[] { LibrarianSettings041.PageId, LibrarianSettings041.DisplayPageId, LibrarianSettings041.EffectsPageId, LibrarianSettings041.ToolsPageId })
                Check(ModSettingsRegistry.TryGetPage(MainFile.ModId, page, out _), "Ritsu settings page " + page);
            MainFile.Logger.Info("V130_MODEL_REGISTRATION_PASS models=149 cards=100 active=91 relics=9 potions=3 nativeAssembly=True ritsuOwnership=True explicitOwners=146 nativeCharacterPools=3");

            var artwork = new List<object>();
            foreach (var card in cards)
            {
                string smallPath = card.PortraitPath;
                string bigPath = smallPath.Replace("/card_portraits/", "/card_portraits/big/", StringComparison.Ordinal);
                foreach (string path in new[] { smallPath, bigPath })
                {
                    var texture = ResourceLoader.Load<Texture2D>(path);
                    Check(texture is not null && texture.GetWidth() > 0 && texture.GetHeight() > 0, "loaded card portrait " + card.Id + " " + path);
                    using var image = texture!.GetImage();
                    Check(image is not null && !image.IsEmpty(), "decoded card portrait " + card.Id + " " + path);
                    if (image!.IsCompressed()) Check(image.Decompress() == Error.Ok, "native texture decompression " + card.Id + " " + path);
                    image!.Convert(Image.Format.Rgba8);
                    Check(image.GetFormat() == Image.Format.Rgba8, "normalized decoded portrait " + path);
                    artwork.Add(new { card = card.Id.ToString(), path, width = texture.GetWidth(), height = texture.GetHeight(),
                        rgba_sha256 = Convert.ToHexString(SHA256.HashData(image.GetData())) });
                }
            }
            MainFile.Logger.Info("V130_CARD_RESOURCES_PASS cards=100 smallAndBig=200 nativeTextureDecode=True");

            fixture = new Control { Name = "LibrarianV130ScriptFixture", Visible = false, ProcessMode = Node.ProcessModeEnum.Disabled };
            NGame.Instance!.AddChild(fixture);
            var select = ResourceLoader.Load<PackedScene>(character.CharacterSelectBg).Instantiate<Node>();
            fixture.AddChild(select);
            Check(select is LibrarianCharacterSelect137 && ReferenceEquals(select.GetType().Assembly, content), "selection scene uses selected content script");
            var ownSelect = (LibrarianCharacterSelect137)select;
            Check(ownSelect.Spine.GetClass() == "SpineSprite" && ownSelect.Spine.GetChildCount() > 0, "selection script Ready loads actual native selection Spine");
            var scenes = new List<object>();
            foreach (string path in new[] { character.CustomVisualsPath, character.CustomRestSiteAnimPath, character.CustomMerchantAnimPath })
            {
                var scene = ResourceLoader.Load<PackedScene>(path).Instantiate<Node>();
                fixture.AddChild(scene);
                var motions = Desc(scene).OfType<LibrarianCharacterMotion>().ToArray();
                Check(motions.Length == 1 && ReferenceEquals(motions[0].GetType().Assembly, content), "scene uses selected character-motion script " + path);
                Check(motions[0].Rig is not null && motions[0].HasSeparateHands, "scene Ready creates layered rig and hands " + path);
                scenes.Add(new { path, script = motions[0].GetType().FullName, rig = true, separateHands = true });
            }
            await NGame.Instance.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
            MainFile.Logger.Info("V130_GODOT_SCRIPTS_PASS selection=True combat=True rest=True shop=True ready=True layeredRig=True");
            if (quit)
            {
                var commonPreload = await CommonPreloadStarted.Task.WaitAsync(TimeSpan.FromMinutes(2));
                Check(await commonPreload.WaitAsync(TimeSpan.FromMinutes(2)), "native Common preload completed before prototype quit");
                MainFile.Logger.Info("V130_NATIVE_PRELOAD_PASS common=True realSessionCompletion=True");
            }
            File.WriteAllText(Path.Combine(output, "v130-prototype.json"), JsonSerializer.Serialize(new {
                mode, checks = _checks, game_version = expectedGame, native_game_version = nativeGameVersion, mod_version = "1.3.7", user_data = OS.GetUserDataDir(),
                content_path = contentPath, content_sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(content.Location))),
                native_mod_assemblies = nativeModAssemblies, models, artwork, scenes,
                nativeCommonPreloadAwaited = quit, nativeRunTested = false, realMultiplayerTested = false
            }, new JsonSerializerOptions { WriteIndented = true }));
            MainFile.Logger.Info($"V130_PROTOTYPE_PASS checks={_checks} game={expectedGame} mode={mode} menu=True ids=149 scripts=True resources=True gameplay=False");
        }
        catch (Exception error) { MainFile.Logger.Error("V130_PROTOTYPE_FAIL " + error); }
        finally
        {
            if (fixture is not null && GodotObject.IsInstanceValid(fixture)) fixture.QueueFree();
        }
        if (quit && NGame.Instance is { } game)
        {
            // Failure paths also leave the native loader alive until its outstanding
            // Common work ends, so audit cleanup cannot create shutdown parser errors.
            try
            {
                var commonPreload = await CommonPreloadStarted.Task.WaitAsync(TimeSpan.FromMinutes(2));
                await commonPreload.WaitAsync(TimeSpan.FromMinutes(2));
            }
            catch (Exception error) { MainFile.Logger.Error("V130_PROTOTYPE_PRELOAD_FAIL " + error); return; }
            await game.ToSignal(game.GetTree(), SceneTree.SignalName.ProcessFrame);
            game.Quit();
        }
    }
}
