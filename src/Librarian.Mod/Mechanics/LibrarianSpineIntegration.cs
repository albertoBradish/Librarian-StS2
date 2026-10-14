using Godot;
using Librarian.LibrarianCode.Character;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;

namespace Librarian.Mechanics;

/// <summary>Own resource identity and startup binding, before any character node is constructed.</summary>
internal static class LibrarianSpineIntegration
{
    // Shared engine resources are retained and borrowed, never disposed by this adapter.
    private static Resource? _nativeBank;
    private static LibrarianSpineAssetPins? _pins;
    private static Action<Exception>? _fault;
    internal static bool IsConfigured => _nativeBank is not null && _pins is not null;
    internal static bool IsOwnPlayer(Player? player) => player?.Character is LibrarianCharacter;

    internal static void Initialize(LibrarianSpineAssetPins pins, Action<Exception> fault,
        Action<LibrarianSpineObservation>? observation = null)
    {
        AssertMainThread();
        if (IsConfigured) throw new InvalidOperationException("Spine startup configuration is immutable for this process.");
        if (!pins.AssetRoot.StartsWith("res://Librarian/spine/", StringComparison.Ordinal)
            || pins.AssetRoot.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException("Use the owned versioned bank path.");
        foreach (string hash in new[] { pins.ReceiptSha256, pins.SkeletonSha256, pins.AtlasSha256,
            pins.BankSha256, pins.ContractSha256 })
            if (hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c)))
                throw new InvalidDataException("Fill all G1 resource pins from the actual frozen export before enabling Spine.");
        CheckHash(pins.ReceiptPath, pins.ReceiptSha256);
        CheckHash(pins.SkeletonPath, pins.SkeletonSha256);
        CheckHash(pins.AtlasPath, pins.AtlasSha256);
        CheckHash(pins.BankPath, pins.BankSha256);
        var bank = LibrarianSpineBank.Read(Godot.FileAccess.GetFileAsString(pins.ReceiptPath));
        if (!SameHash(bank.SkeletonSha256, pins.SkeletonSha256) || !SameHash(bank.ContractSha256, pins.ContractSha256))
            throw new InvalidDataException("Runtime receipt differs from the pinned authored skeleton/contract.");
        var native = ResourceLoader.Load<Resource>(pins.BankPath, "SpineSkeletonDataResource")
            ?? throw new InvalidDataException("Could not load the actual native bank.");
        if (native.GetClass() != "SpineSkeletonDataResource" || !native.HasMethod("is_skeleton_data_loaded")
            || !Bool(native, "is_skeleton_data_loaded") || native.ResourcePath != pins.BankPath)
            throw new InvalidDataException("The owned bank is not a loaded native Spine resource.");
        CheckDependency(native, "skeleton_file_res", pins.SkeletonPath, "SpineSkeletonFileResource");
        CheckDependency(native, "atlas_res", pins.AtlasPath, "SpineAtlasResource");
        ValidateMetadata(native, bank);
        LibrarianSpineRuntime.Configure(bank, ReadSettings, fault, observation);
        _fault = fault; _pins = pins; _nativeBank = native;
        GC.KeepAlive(native);
    }

    private static void ValidateMetadata(Resource native, LibrarianSpineBank bank)
    {
        if (!native.HasMethod("get_animations")) throw new InvalidDataException("Native bank metadata binding is absent.");
        using var value = native.Call("get_animations");
        using var array = value.AsGodotArray();
        var expected = bank.Names.ToHashSet(StringComparer.Ordinal);
        if (array.Count != 60) throw new InvalidDataException("Native bank must contain exactly 60 authored clips.");
        for (int i = 0; i < array.Count; i++)
        {
            using var item = array[i];
            var animation = item.AsGodotObject();
            if (animation is not RefCounted || animation.GetClass() != "SpineAnimation")
                throw new InvalidDataException("Invalid native animation metadata wrapper.");
            try
            {
                using var nameValue = animation.Call("get_name");
                using var durationValue = animation.Call("get_duration");
                string name = nameValue.AsString();
                if (!expected.Remove(name)) throw new InvalidDataException("Unexpected/duplicate native clip: " + name);
                bank.ValidateNativeAnimation(name, durationValue.AsSingle());
            }
            finally { GC.KeepAlive(animation); animation.Dispose(); GC.KeepAlive(native); }
        }
        if (expected.Count != 0) throw new InvalidDataException("Native bank is incomplete.");
    }

    internal static LibrarianSpineSettings ReadSettings()
    {
        var mode = SaveManager.Instance?.PrefsSave?.FastMode ?? FastModeType.Normal;
        return new(LibrarianPreferences050.Current.ReducedMotion, mode switch {
            FastModeType.Instant => LibrarianSpineSpeed.Instant,
            FastModeType.Fast => LibrarianSpineSpeed.Fast, _ => LibrarianSpineSpeed.Normal });
    }

    internal static CreatureAnimator CreateCombatAnimator(MegaSprite sprite)
    {
        AssertMainThread();
        var node = sprite.BoundObject as Node ?? throw new InvalidDataException("Combat Spine is not a Node.");
        var creature = Ancestor<NCreature>(node) ?? throw new InvalidDataException("Combat Spine has no real NCreature.");
        if (!IsOwnPlayer(creature.Entity.Player) || creature.Visuals.SceneFilePath != LibrarianSpineAssetPins.CombatScene)
            throw new InvalidDataException("The protected animator factory must receive this character's actual owned combat scene.");
        ValidateSprite(node);
        return LibrarianSpineRuntime.CreateCombatAnimator(sprite);
    }

    internal static Node RequireRoomShape(Node root, string scene)
    {
        AssertMainThread();
        if (!IsConfigured || root.SceneFilePath != scene || root.GetChildCount() == 0)
            throw new InvalidDataException("Expected the configured own C# room scene root.");
        var sprite = root.GetChild(0);
        if (sprite.GetClass() != "SpineSprite") throw new InvalidDataException("Own room scene requires direct first-child SpineSprite.");
        return sprite;
    }
    internal static void ValidateSprite(Node sprite)
    {
        AssertMainThread();
        if (!IsConfigured || sprite.GetClass() != "SpineSprite" || !sprite.HasMethod("get_skeleton_data_res"))
            throw new InvalidDataException("Unconfigured or foreign native sprite.");
        using var value = sprite.Call("get_skeleton_data_res");
        var resource = value.AsGodotObject();
        try
        {
            if (resource is not Resource native || native.ResourcePath != _pins!.BankPath
                || native.GetInstanceId() != _nativeBank!.GetInstanceId())
                throw new InvalidDataException("Room/combat sprite does not borrow the exact configured native bank.");
            if (!Bool(native, "is_skeleton_data_loaded")) throw new InvalidDataException("Owned skeleton data is not ready.");
        }
        finally { GC.KeepAlive(resource); GC.KeepAlive(_nativeBank); GC.KeepAlive(sprite); }
    }
    internal static T? Ancestor<T>(Node node) where T : Node
    {
        for (Node? parent = node; parent is not null; parent = parent.GetParent())
            if (parent is T typed) return typed;
        return null;
    }
    internal static void PresentationSafely(Action action)
    {
        try { action(); }
        catch (Exception error) { Report(error); }
    }
    internal static void Report(Exception error) { AssertMainThread(); _fault?.Invoke(error); }
    internal static void AssertMainThread()
    {
        if (OS.GetThreadCallerId() != OS.GetMainThreadId()) throw new InvalidOperationException("Spine adapter requires Godot main thread.");
    }
    private static bool Bool(GodotObject value, StringName method)
    { using var result = value.Call(method); try { return result.AsBool(); } finally { GC.KeepAlive(value); } }
    private static bool SameHash(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static void CheckHash(string path, string expected)
    {
        if (!Godot.FileAccess.FileExists(path) || !SameHash(Godot.FileAccess.GetSha256(path), expected))
            throw new InvalidDataException("Packed authored resource hash differs: " + path);
    }
    private static void CheckDependency(Resource bank, StringName property, string path, string type)
    {
        using var value = bank.Get(property);
        var dependency = value.AsGodotObject();
        try
        {
            if (dependency is not Resource resource || resource.GetClass() != type || resource.ResourcePath != path)
                throw new InvalidDataException("Native bank restored a different dependency: " + property);
        }
        finally { GC.KeepAlive(dependency); GC.KeepAlive(bank); }
    }
}
