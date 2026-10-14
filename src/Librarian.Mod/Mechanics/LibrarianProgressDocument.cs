using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Librarian.Mechanics;

/// <summary>Pure JSON preservation with unique identities; no filesystem or game access.</summary>
internal sealed class LibrarianProgressDocument(JsonObject raw, JsonObject baseline)
{
    private static readonly string[] IdentityKeys =
        ["id", "encounter_id", "enemy_id", "ancient_id", "character", "achievement", "key", "name", "player_id", "slot"];

    private static JsonObject Parse(string json) => JsonNode.Parse(json) as JsonObject
        ?? throw new JsonException("Progress must be an object.");
    private readonly object _sync = new();
    private JsonObject _raw = raw, _baseline = baseline;
    internal int MissingRecords { get; private set; }
    internal bool Matches(string knownJson) => JsonNode.DeepEquals(Parse(knownJson)["unique_id"], _raw["unique_id"]);

    internal void Verify()
    {
        int missing = 0;
        var restored = Merge(_raw, _baseline, _baseline, ref missing);
        AssertPreserved(_raw, _baseline, restored);
        MissingRecords = missing;
    }

    internal string Preserve(string knownJson, bool advance)
    {
        lock (_sync)
        {
            var known = Parse(knownJson);
            int missing = 0;
            var merged = Merge(_raw, _baseline, known, ref missing) as JsonObject
                ?? throw new InvalidDataException("Progress merge did not produce an object.");
            string result = merged.ToJsonString();
            if (advance) { _raw = merged; _baseline = known; }
            return result;
        }
    }

    private static JsonNode? Merge(JsonNode? raw, JsonNode? baseline, JsonNode? current, ref int missing)
    {
        if (raw is JsonObject source && baseline is JsonObject known && current is JsonObject next)
        {
            var merged = (JsonObject)next.DeepClone();
            foreach (var (key, value) in source)
            {
                if (!known.TryGetPropertyValue(key, out var previous))
                {
                    if (merged.TryGetPropertyValue(key, out var collision) && !JsonNode.DeepEquals(value, collision))
                        throw new InvalidDataException("A previously unknown field now conflicts with the projection.");
                    merged[key] = value?.DeepClone();
                }
                else if (next.TryGetPropertyValue(key, out var updated))
                    merged[key] = Merge(value, previous, updated, ref missing);
                else if (!JsonNode.DeepEquals(value, previous))
                    throw new InvalidDataException("An unknown field lost its parent object.");
            }
            return merged;
        }
        if (raw is JsonArray sourceArray && baseline is JsonArray knownArray && current is JsonArray nextArray)
        {
            var sourceMap = Index(sourceArray);
            var knownMap = Index(knownArray);
            var nextMap = Index(nextArray);
            if (sourceMap is null || knownMap is null || nextMap is null)
            {
                if (!JsonNode.DeepEquals(raw, baseline))
                    throw new InvalidDataException("Structured array records have no unique identity.");
                return current.DeepClone();
            }
            var merged = new JsonArray();
            foreach (var item in nextArray)
            {
                string key = Identity(item)!;
                if (sourceMap.TryGetValue(key, out var original) && knownMap.TryGetValue(key, out var previous))
                    merged.Add(Merge(original, previous, item, ref missing));
                else if (sourceMap.TryGetValue(key, out original) && !JsonNode.DeepEquals(original, item))
                    throw new InvalidDataException("An unavailable record now conflicts with a known record.");
                else merged.Add(item?.DeepClone());
            }
            foreach (var (key, item) in sourceMap)
                if (!knownMap.ContainsKey(key) && !nextMap.ContainsKey(key))
                {
                    merged.Add(item?.DeepClone());
                    missing++;
                }
            return merged;
        }
        if ((raw is JsonObject or JsonArray || baseline is JsonObject or JsonArray)
            && !JsonNode.DeepEquals(raw, baseline))
            throw new InvalidDataException("An unknown value cannot be mapped to its current projection.");
        return current?.DeepClone();
    }

    private static void AssertPreserved(JsonNode? raw, JsonNode? baseline, JsonNode? restored)
    {
        if (raw is JsonObject source && baseline is JsonObject known && restored is JsonObject next)
        {
            foreach (var (key, value) in source)
            {
                if (!next.TryGetPropertyValue(key, out var current))
                    throw new InvalidDataException("A progress field was lost during preparation.");
                if (known.TryGetPropertyValue(key, out var previous)) AssertPreserved(value, previous, current);
                else if (!JsonNode.DeepEquals(value, current))
                    throw new InvalidDataException("An unknown progress field was changed.");
            }
        }
        else if (raw is JsonArray sourceArray && baseline is JsonArray knownArray && restored is JsonArray nextArray)
        {
            var sourceMap = Index(sourceArray); var knownMap = Index(knownArray); var nextMap = Index(nextArray);
            if (sourceMap is null || knownMap is null || nextMap is null)
            {
                if (!JsonNode.DeepEquals(raw, baseline)) throw new InvalidDataException("Unknown array fields cannot be verified.");
                return;
            }
            foreach (var (key, value) in sourceMap)
            {
                if (!nextMap.TryGetValue(key, out var current)) throw new InvalidDataException("A progress record was lost.");
                if (knownMap.TryGetValue(key, out var previous)) AssertPreserved(value, previous, current);
                else if (!JsonNode.DeepEquals(value, current)) throw new InvalidDataException("An unavailable record was changed.");
            }
        }
    }

    private static Dictionary<string, JsonNode?>? Index(JsonArray array)
    {
        var result = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var row in array)
        {
            string? key = Identity(row);
            if (key is null) return null;
            if (!result.TryAdd(key, row)) throw new InvalidDataException("Duplicate progress record identities.");
        }
        return result;
    }

    private static string? Identity(JsonNode? node)
    {
        if (node is null) return "null";
        if (node is JsonValue value) return "value:" + value.ToJsonString();
        if (node is not JsonObject row) return null;
        foreach (string key in IdentityKeys)
            if (row[key] is JsonValue id && (id.TryGetValue<string>(out _) || id.TryGetValue<long>(out _)))
                return key + ":" + id.ToJsonString();
        return null;
    }
}
