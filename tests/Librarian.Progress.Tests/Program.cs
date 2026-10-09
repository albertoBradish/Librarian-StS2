using System.Text.Json.Nodes;
using Librarian.Mechanics;

int checks = 0;
JsonObject Parse(string json) => (JsonObject)JsonNode.Parse(json)!;
void Require(bool value, string name)
{
    if (!value) throw new Exception(name);
    checks++;
    Console.WriteLine("PASS " + name);
}
LibrarianProgressDocument Prepare(string raw, string known)
{
    var document = new LibrarianProgressDocument(Parse(raw), Parse(known));
    document.Verify();
    return document;
}
void Reject(Action action, string name)
{
    try { action(); }
    catch (InvalidDataException) { Require(true, name); return; }
    throw new Exception("Expected rejection: " + name);
}

const string known = """{"unique_id":"fixture","character_stats":[{"id":"CHARACTER.LIBRARIAN-LIBRARIAN_CHARACTER","max_ascension":0}],"card_stats":[],"encounter_stats":[],"enemy_stats":[],"discovered_cards":[]}""";
const string raw = """{"unique_id":"fixture","extension":{"other_mod":7},"character_stats":[{"id":"CHARACTER.LIBRARIAN-LIBRARIAN_CHARACTER","max_ascension":0,"custom":9}],"card_stats":[{"id":"CARD.ABSENT-CARD","times_played":3,"custom":4}],"encounter_stats":[{"encounter_id":"ENCOUNTER.ABSENT","fight_stats":[{"character":"CHARACTER.IRONCLAD","extra":5}]}],"enemy_stats":[{"enemy_id":"ENEMY.ABSENT","count":2}],"discovered_cards":["CARD.ABSENT-CARD"]}""";
var shadow = Prepare(raw, known);
Require(shadow.MissingRecords == 4, "four unavailable records counted");
Require(shadow.Matches(known), "same document accepted");
Require(!shadow.Matches(known.Replace("fixture", "other-profile")), "different document isolated");
var changed = Parse(known);
changed["character_stats"]![0]!["max_ascension"] = 10;
string updated = shadow.Preserve(changed.ToJsonString(), true);
var merged = Parse(updated);
Require(merged["character_stats"]![0]!["max_ascension"]!.GetValue<int>() == 10, "known ascension change applied");
Require(merged["character_stats"]![0]!["custom"]!.GetValue<int>() == 9, "unknown field on known row preserved");
Require(merged["extension"]!["other_mod"]!.GetValue<int>() == 7, "unknown root object preserved");
Require(JsonNode.DeepEquals(merged["card_stats"], Parse(raw)["card_stats"]), "unavailable card fully preserved");
Require(JsonNode.DeepEquals(merged["encounter_stats"], Parse(raw)["encounter_stats"]), "unavailable encounter and nested data preserved");
Require(JsonNode.DeepEquals(merged["enemy_stats"], Parse(raw)["enemy_stats"]), "unavailable enemy preserved");
Require(JsonNode.DeepEquals(merged["discovered_cards"], Parse(raw)["discovered_cards"]), "unavailable discovery preserved");
Require(JsonNode.DeepEquals(Parse(shadow.Preserve(changed.ToJsonString(), true)), merged), "second ordinary save does not duplicate records");
Require(JsonNode.DeepEquals(Parse(shadow.Preserve(changed.ToJsonString(), false)), merged), "mirror preview preserves data without advancing");
var afterReload = Prepare(updated, changed.ToJsonString());
Require(JsonNode.DeepEquals(Parse(afterReload.Preserve(changed.ToJsonString(), true)), merged), "new process shadow reconstruction preserves data");
var deleted = Parse(changed.ToJsonString());
deleted["character_stats"] = new JsonArray();
var afterDelete = Parse(shadow.Preserve(deleted.ToJsonString(), true));
Require(((JsonArray)afterDelete["character_stats"]!).Count == 0, "intentional known record deletion does not resurrect it");
Require(((JsonArray)afterDelete["card_stats"]!).Count == 1, "unavailable other-mod record survives known deletion");
var duplicate = Parse(raw);
((JsonArray)duplicate["card_stats"]!).Add(duplicate["card_stats"]![0]!.DeepClone());
Reject(() => Prepare(duplicate.ToJsonString(), known), "duplicate record identities rejected before commit");
var conflict = Parse(changed.ToJsonString());
conflict["extension"] = new JsonObject { ["other_mod"] = 8 };
Reject(() => afterReload.Preserve(conflict.ToJsonString(), true), "newly known field conflict rejected");
Reject(() => Prepare("""{"unique_id":"fixture","records":[{"extra":1}]}""", """{"unique_id":"fixture","records":[{}]}"""), "unknown structured array without identity rejected");
var keyedRaw = """{"unique_id":"fixture","encounter_stats":[{"encounter_id":"E","extra":7}],"enemy_stats":[{"enemy_id":"N","extra":8}],"ancient_stats":[{"ancient_id":"A","extra":9}]}""";
var keyedKnown = """{"unique_id":"fixture","encounter_stats":[{"encounter_id":"E"}],"enemy_stats":[{"enemy_id":"N"}],"ancient_stats":[{"ancient_id":"A"}]}""";
var keyed = Parse(Prepare(keyedRaw,keyedKnown).Preserve(keyedKnown,true));
Require(keyed["encounter_stats"]![0]!["extra"]!.GetValue<int>() == 7, "encounter_id matches unknown fields");
Require(keyed["enemy_stats"]![0]!["extra"]!.GetValue<int>() == 8, "enemy_id matches unknown fields");
Require(keyed["ancient_stats"]![0]!["extra"]!.GetValue<int>() == 9, "ancient_id matches unknown fields");
var reorderedRaw = """{"unique_id":"fixture","rows":[{"id":"A","extra":1},{"id":"B","extra":2}]}""";
var reorderedKnown = """{"unique_id":"fixture","rows":[{"id":"A"},{"id":"B"}]}""";
var reorderedNext = """{"unique_id":"fixture","rows":[{"id":"B"},{"id":"A"}]}""";
var reorder = Parse(Prepare(reorderedRaw,reorderedKnown).Preserve(reorderedNext,true));
Require(reorder["rows"]![0]!["extra"]!.GetValue<int>() == 2 && reorder["rows"]![1]!["extra"]!.GetValue<int>() == 1, "row reordering uses identity instead of position");
Console.WriteLine($"RESULT {checks}/{checks} passed");
