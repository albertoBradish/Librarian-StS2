using System.Text.Json;

namespace Librarian.Mechanics;

internal enum LibrarianSpineRoom { Combat, Merchant, Rest }
internal enum LibrarianSpineSpeed { Normal, Fast, Instant }
internal readonly record struct LibrarianSpineSettings(bool ReducedMotion, LibrarianSpineSpeed Speed);
internal readonly record struct LibrarianSpineContext(LibrarianSpineRoom Room, int ActIndex, bool IsDead, bool LowHealth);

/// <summary>Authored metadata, read once from the same exported skeleton's receipt.</summary>
internal sealed class LibrarianSpineBank
{
    internal static readonly string[] BodyNames = [
        "idle_loop", "low_health_loop", "relaxed_loop", "overgrowth_loop", "hive_loop", "glory_loop",
        "attack", "cast", "cast_guard", "cast_draw", "cast_transfer", "power", "hurt", "die", "revive", "idle_still", "book_flip"
    ];
    internal static readonly string[] ExpressionNames = [
        "expression_dot", "expression_friendly_arc", "expression_check", "expression_star", "expression_heart",
        "expression_uneasy_arc", "expression_question", "expression_surprise", "expression_tear",
        "expression_narrowed", "expression_sleepy_arc", "expression_ellipsis", "expression_off"
    ];
    internal static readonly string[] SelectionSlots = [
        "hand_screen_left", "hand_screen_right", "pages_left", "pages_right", "spine", "cover_left", "cover_right",
        "bookmark_left_root_fill", "bookmark_right_root_fill", "bookmark_left", "bookmark_right"
    ];
    private static readonly string[] BookGroup = [
        "pages_left", "pages_right", "spine", "cover_left", "cover_right", "bookmark_left_root_fill",
        "bookmark_right_root_fill", "bookmark_left", "bookmark_right"
    ];
    private readonly Dictionary<string, LibrarianSpineClip> _clips;
    private readonly IReadOnlyDictionary<(string From, string To), float> _pairMixes;
    internal IReadOnlyList<string> Slots { get; }
    internal IEnumerable<string> Names => _clips.Keys;
    internal string SkeletonSha256 { get; }
    internal string ContractSha256 { get; }

    internal LibrarianSpineBank(IEnumerable<LibrarianSpineClip> clips, IEnumerable<string> slots,
        string skeletonSha256 = "", string contractSha256 = "", bool requireFullBank = true,
        IReadOnlyDictionary<(string From, string To), float>? pairMixes = null)
    {
        _clips = clips.ToDictionary(c => c.Name, StringComparer.Ordinal);
        Slots = slots.ToArray();
        SkeletonSha256 = skeletonSha256;
        ContractSha256 = contractSha256;
        _pairMixes = pairMixes ?? new Dictionary<(string, string), float>();
        foreach (var pair in _pairMixes)
            if (!_clips.ContainsKey(pair.Key.From) || !_clips.ContainsKey(pair.Key.To) || !float.IsFinite(pair.Value) || pair.Value < 0)
                throw new InvalidDataException("Invalid authored from/to mix.");
        if (!Slots.SequenceEqual(SelectionSlots, StringComparer.Ordinal))
            throw new InvalidDataException("The approved clean-cloth controller requires all eleven selection slots in the frozen order.");
        foreach (var clip in _clips.Values)
        {
            string semantic = clip.Name.StartsWith("reduced/", StringComparison.Ordinal) ? clip.Name[8..] : clip.Name;
            bool isBody = BodyNames.Contains(semantic, StringComparer.Ordinal);
            bool isExpression = ExpressionNames.Contains(semantic, StringComparer.Ordinal);
            if (clip.Track is not 0 and not 1 || (isBody && clip.Track != 0) || (isExpression && clip.Track != 1) || (!isBody && !isExpression))
                throw new InvalidDataException("Clip identity and body/expression track disagree: " + clip.Name);
            if (!float.IsFinite(clip.Duration) || clip.Duration < 0 || !float.IsFinite(clip.MixIn) || clip.MixIn < 0)
                throw new InvalidDataException("Invalid duration/mix: " + clip.Name);
            if (clip.ReleaseTime is { } release && (!float.IsFinite(release) || release < 0 || release > clip.Duration))
                throw new InvalidDataException("Release is outside the authored clip: " + clip.Name);
            if (clip.Track == 0)
            {
                if (clip.FirstAttachmentKeys.Count != Slots.Count || Slots.Any(s => !clip.FirstAttachmentKeys.ContainsKey(s)))
                    throw new InvalidDataException("Every selection slot must have an explicit first-key time or null: " + clip.Name);
                foreach (var time in clip.FirstAttachmentKeys.Values)
                    if (time is { } t && (!float.IsFinite(t) || t < 0 || t > clip.Duration + .0001f))
                        throw new InvalidDataException("Invalid attachment phase: " + clip.Name);
                // The repaired book, roots and cloth must never change to mismatched views.
                var first = clip.FirstAttachmentKeys[BookGroup[0]];
                if (BookGroup.Any(s => clip.FirstAttachmentKeys[s] != first))
                    throw new InvalidDataException("Book/root/cloth first selectors are not atomic: " + clip.Name);
            }
        }
        if (requireFullBank)
        {
            var required = BodyNames.Concat(ExpressionNames).SelectMany(n => new[] { n, "reduced/" + n }).ToHashSet(StringComparer.Ordinal);
            if (_clips.Count != 60 || !_clips.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(required))
                throw new InvalidDataException("Expected the authored 60-clip bank: normal30 plus reduced/30.");
            foreach (string semantic in BodyNames.Concat(ExpressionNames))
            {
                var normal = _clips[semantic];
                var reduced = _clips["reduced/" + semantic];
                bool expectedLoop = semantic.EndsWith("_loop", StringComparison.Ordinal); // Exactly the six frozen room loops.
                if (normal.Loop != expectedLoop || reduced.Loop != expectedLoop || normal.Loop != reduced.Loop)
                    throw new InvalidDataException("Frozen full-bank loop/mirror contract differs: " + semantic);
                if (Math.Abs(normal.Duration - reduced.Duration) > .0001f)
                    throw new InvalidDataException("Frozen normal/reduced duration mirror differs: " + semantic);
                if (IsCardGesture(semantic))
                {
                    float release = semantic == "attack" ? .15f : .25f;
                    if (normal.ReleaseTime is not { } n || reduced.ReleaseTime is not { } r
                        || Math.Abs(n - release) > .0001f || Math.Abs(r - release) > .0001f || Math.Abs(n - r) > .0001f)
                        throw new InvalidDataException("Frozen full-bank visual release differs: " + semantic);
                }
            }
        }
    }

    internal static LibrarianSpineBank Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("schema_version").GetInt32() != 1)
            throw new InvalidDataException("Unsupported Spine runtime receipt.");
        var slots = root.GetProperty("selection_slots").EnumerateArray().Select(e => e.GetString()!).ToArray();
        var clips = root.GetProperty("clips").EnumerateArray().Select(e => new LibrarianSpineClip(
            e.GetProperty("name").GetString()!, e.GetProperty("duration").GetSingle(),
            e.GetProperty("loop").GetBoolean(), e.GetProperty("track").GetInt32(), e.GetProperty("mix_in").GetSingle(),
            e.GetProperty("release_time").ValueKind == JsonValueKind.Null ? null : e.GetProperty("release_time").GetSingle(),
            e.GetProperty("first_attachment_keys").EnumerateObject().ToDictionary(p => p.Name,
                p => p.Value.ValueKind == JsonValueKind.Null ? (float?)null : p.Value.GetSingle(), StringComparer.Ordinal)));
        var pairs = root.TryGetProperty("pair_mixes", out var mixList)
            ? mixList.EnumerateArray().ToDictionary(e => (e.GetProperty("from").GetString()!, e.GetProperty("to").GetString()!),
                e => e.GetProperty("duration").GetSingle()) : new Dictionary<(string, string), float>();
        return new LibrarianSpineBank(clips, slots, root.GetProperty("skeleton_sha256").GetString()!,
            root.GetProperty("authoring_contract_sha256").GetString()!, pairMixes: pairs);
    }

    internal LibrarianSpineClip Resolve(string semantic, bool reduced)
        => _clips.TryGetValue((reduced ? "reduced/" : "") + semantic, out var clip) ? clip
            : throw new InvalidDataException("Required Spine clip is absent: " + semantic);
    internal void ValidateNativeAnimation(string name, float duration)
    {
        if (!_clips.TryGetValue(name, out var clip) || !float.IsFinite(duration) || Math.Abs(duration - clip.Duration) > .0001f)
            throw new InvalidDataException("Native animation name/duration differs from frozen receipt: " + name);
    }
    internal float MixDuration(string? from, LibrarianSpineClip to, LibrarianSpineSpeed speed = LibrarianSpineSpeed.Normal)
    {
        string target = to.Name.StartsWith("reduced/", StringComparison.Ordinal) ? to.Name[8..] : to.Name;
        float duration = from is not null && _pairMixes.TryGetValue((from, to.Name), out float authored)
            ? authored : to.MixIn;
        // Cap entry blends before the authored release, and give the return pose a softer recovery.
        // Native mix_time advances in wall time, independently of this entry's Fast time_scale.
        duration = target switch
        {
            "attack" => Math.Min(duration, .04f),
            "cast" or "cast_guard" or "cast_draw" or "cast_transfer" or "power" => Math.Min(duration, .06f),
            "hurt" => Math.Min(duration, .03f),
            _ when to.Loop && from is not null => Math.Max(duration, .12f),
            _ => duration
        };
        return speed == LibrarianSpineSpeed.Fast ? duration * .5f : duration;
    }

    internal static bool IsCardGesture(string name) => name is "attack" or "cast" or "cast_guard" or "cast_draw" or "cast_transfer" or "power";
    internal static string ReturnSemantic(LibrarianSpineContext context, LibrarianSpineSettings settings)
        => context.Room switch
        {
            LibrarianSpineRoom.Merchant => "relaxed_loop",
            LibrarianSpineRoom.Rest => context.ActIndex switch { 0 => "overgrowth_loop", 1 => "hive_loop", 2 => "glory_loop", _ => "idle_still" },
            _ when context.LowHealth => "low_health_loop",
            _ => "idle_loop"
        };
}

internal sealed record LibrarianSpineClip(string Name, float Duration, bool Loop, int Track, float MixIn,
    float? ReleaseTime, IReadOnlyDictionary<string, float?> FirstAttachmentKeys);

/// <summary>An exact float32 marker on our own native entry's expiration field; never written into an asset.</summary>
internal static class LibrarianSpineEntryTags
{
    // 2^65 seconds (~1.17 trillion years), fixed exponent and monotonically allocated 23-bit mantissa.
    // Neither animation duration/end, loop state, track time nor normal session expiration changes.
    private const int FirstBits = 0x60000000;
    private const int Capacity = 0x007fffff;
    private static int _counter;
    internal static int Next()
    {
        while (true)
        {
            int previous = Volatile.Read(ref _counter);
            if (previous < 0 || previous >= Capacity)
                throw new InvalidOperationException("Spine presentation tag domain exhausted; do not reuse a live-entry identity.");
            if (Interlocked.CompareExchange(ref _counter, previous + 1, previous) == previous)
                return FirstBits | (previous + 1);
        }
    }
    internal static float AsFloat(int bits) => BitConverter.Int32BitsToSingle(bits);
    internal static int FromFloat(float value) => BitConverter.SingleToInt32Bits(value);
}
