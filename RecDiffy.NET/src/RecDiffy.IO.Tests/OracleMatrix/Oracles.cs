using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using RecDiffy.Tests.Acceptance;

namespace RecDiffy.IO.Tests.OracleMatrix;

// One adapter per oracle shape (spec v3 §12.2). Each reads the file as written and keeps the oracle's own labels and
// field names; it never decides an outcome. OracleLabels maps the labels, and fails on one it doesn't know.
// A stated difference becomes an OracleDelta only when it is a number: dates and codes are named, not measured.
internal static class Oracles
{
    public static IReadOnlyList<OracleCase> Sonnet(string path) =>
        [.. Array(Json(path), "groups").Select(group => SonnetCase(group, group["details"]!))];

    private static OracleCase SonnetCase(JsonNode group, JsonNode details) =>
        new(Text(group, "group_id"), Text(group, "expected_outcome"), Strings(group, "internal_trade_ids"), Strings(group, "cpty_report_ids"))
        {
            Survivor = Optional(details, "surviving_cpty_report_id"),
            BreakFields = Optional(details, "break_type") is null ? [] : [Text(details, "field")],
            Deltas = Number(details["delta"]) is { } delta ? [new(Text(details, "field"), delta)] : [],
        };

    // Fixture 2.0 (schema 2.0). MATCH_AGGREGATED covers allocations and swaps, so §6.5 qualifies it by what is aggregated.
    public static IReadOnlyList<OracleCase> Fixture20(string path) =>
        [.. Array(Json(path), "cases").Select(item => new OracleCase(Text(item, "case_id"), Fixture20Label(item), Strings(item, "internal_ids"), Strings(item, "counterparty_ids"))
        {
            Survivor = Optional(item, "active_report_id"),
            BreakFields = Optional(item, "root_cause") is { } root ? [root] : [],
            Deltas = Differences(item, "expected_differences", difference => Number(difference["delta"])),
        })];

    private static string Fixture20Label(JsonNode item) => (Text(item, "expected_outcome"), Optional(item, "aggregation_rule")) switch
    {
        ("MATCH_AGGREGATED", "SUM_ALLOCATIONS") => "MATCH_AGGREGATED (allocations)",
        ("MATCH_AGGREGATED", "SWAP_DISTINCT_NOTIONAL_SUM_CASH") => "MATCH_AGGREGATED (swaps)",
        ("MATCH_AGGREGATED", var rule) => $"MATCH_AGGREGATED ({rule})",
        (var outcome, _) => outcome,
    };

    // Fixture v1: the Expected Results tab, the third sheet of the workbook.
    public static IReadOnlyList<OracleCase> FixtureV1(string workbook) =>
        [.. XlsxSheet.Read(workbook, 3)
            .Where(row => row.GetValueOrDefault("CaseID") is not null)
            .Select(row => new OracleCase(Cell(row, "CaseID")!, Cell(row, "ExpectedOutcome") ?? "", Ids(Cell(row, "InternalRowIDs")), Ids(Cell(row, "CounterpartyRowIDs")))
            {
                BreakFields = Cell(row, "BreakField") is { } field ? [field] : [],
            })];

    public static IReadOnlyList<OracleCase> Opus(string path)
    {
        var root = Json(path);
        var known = Array(root, "known_exact_matches").Select((item, index) => OpusCase($"known_exact_matches[{index}]", item));
        var blind = root["blind_scenarios"]!.AsObject()
            .SelectMany(section => section.Value!.AsArray().Select((item, index) => OpusCase($"{section.Key}[{index}]", item!)));
        return [.. known, .. blind];
    }

    // An Opus break lists its root field first, then the fields that root moves, such as accrued and net cash.
    private static OracleCase OpusCase(string id, JsonNode item) =>
        new(id, Text(item, "expected_status"), OneOrMany(item, "internal_trade_id", "internal_trade_ids"), OpusRight(item))
        {
            Survivor = Optional(item, "matching_report_id"),
            BreakFields = Optional(item, "break_type") is null
                ? []
                : [.. Array(item, "differences").Take(1).Select(difference => Text(difference, "internal_field"))],
            Deltas = Optional(item, "tolerance_type") is null
                ? []
                : Differences(item, "differences", difference => Number(difference["counterparty_value"]) - Number(difference["internal_value"]), "internal_field"),
        };

    private static IReadOnlyList<string> OpusRight(JsonNode item) =>
        Optional(item, "new_report_id") is { } created
            ? [created, Text(item, "canc_report_id"), Text(item, "corr_report_id")]
            : OneOrMany(item, "cpty_report_id", "cpty_report_ids");

    public static IReadOnlyList<OracleCase> V2(string path) =>
        [.. Array(Json(path), "ground_truth_cases").Select(item => new OracleCase(Text(item, "scenario_id"), Text(item, "expected_match_status"), Strings(item, "internal_trade_ids"), Strings(item, "cpty_report_ids"))
        {
            Survivor = Optional(item, "active_surviving_cpty_id"),
            BreakFields = [.. Array(item, "expected_breaks").Where(difference => Flag(difference, "within_tolerance") is false).Select(difference => Text(difference, "field"))],
            Deltas = Differences(item, "expected_breaks", difference => Flag(difference, "within_tolerance") ? Number(difference["delta"]) : null),
        })];

    public static IReadOnlyList<OracleCase> Qwen(string path)
    {
        var root = Json(path);
        var known = Array(root, "known_exact_matches")
            .Select((item, index) => new OracleCase($"known_exact_matches[{index}]", "known_exact_matches", [Text(item, "internal_trade_id")], [Text(item, "cpty_report_id")]));
        var blind = root["blind_scenarios"]!.AsObject()
            .SelectMany(section => section.Value!.AsArray().Select((item, index) => QwenCase($"{section.Key}[{index}]", item!)));
        return [.. known, .. blind];
    }

    private static OracleCase QwenCase(string id, JsonNode item) =>
        new(id, Text(item, "scenario"), OneOrMany(item, "internal_trade_id", "internal_trade_ids"), QwenRight(item))
        {
            Survivor = Optional(item, "expected_final_match_report_id"),
            BreakFields = item["expected_break"] is { } broken ? [Text(broken, "field")] : [],
            Deltas = item["expected_difference"] is { } near && Number(near["difference"]) is { } delta ? [new(Text(near, "field"), delta)] : [],
        };

    private static IReadOnlyList<string> QwenRight(JsonNode item) =>
        Optional(item, "original_new_report_id") is { } created
            ? [created, Text(item, "cancel_report_id"), Text(item, "corrected_report_id")]
            : OneOrMany(item, "cpty_report_id", "cpty_report_ids");

    // DeepSeek has no outcome labels: the section name is the label, so a section the map doesn't know fails the test.
    public static IReadOnlyList<OracleCase> DeepSeek(string path) =>
        [.. Json(path).AsObject().SelectMany(section => section.Value!.AsArray().Select((item, index) => DeepSeekCase($"{section.Key}[{index}]", section.Key, item!)))];

    private static OracleCase DeepSeekCase(string id, string section, JsonNode item) => (item.GetValueKind(), section) switch
    {
        (JsonValueKind.String, "unmatched_counterparty") => new(id, section, [], [item.GetValue<string>()]),
        (JsonValueKind.String, _) => new(id, section, [item.GetValue<string>()], []),
        _ => new(id, section, OneOrMany(item, "Internal_Trade_ID", "Internal_Trade_IDs"), OneOrMany(item, "Cpty_Report_ID", "Cpty_Report_IDs"))
        {
            Survivor = Optional(item, "Matching_CP_ID"),
            BreakFields = Optional(item, "break_type") is { } field ? [field] : [],
        },
    };

    private static JsonNode Json(string path) => JsonNode.Parse(File.ReadAllBytes(path)) ?? throw new InvalidDataException(path);

    private static IEnumerable<JsonNode> Array(JsonNode item, string name) => item[name]?.AsArray().OfType<JsonNode>() ?? [];

    private static IReadOnlyList<OracleDelta> Differences(JsonNode item, string name, Func<JsonNode, decimal?> delta, string field = "field") =>
        [.. Array(item, name).Select(difference => (Field: Text(difference, field), Delta: delta(difference))).Where(pair => pair.Delta is not null).Select(pair => new OracleDelta(pair.Field, pair.Delta!.Value))];

    private static string Text(JsonNode item, string name) => Optional(item, name) ?? throw new InvalidDataException($"'{name}' is missing or not text.");

    private static string? Optional(JsonNode item, string name) =>
        item[name] is { } value && value.GetValueKind() is JsonValueKind.String ? value.GetValue<string>() : null;

    private static bool Flag(JsonNode item, string name) => item[name] is { } value && value.GetValueKind() is JsonValueKind.True;

    private static IReadOnlyList<string> Strings(JsonNode item, string name) => [.. Array(item, name).Select(value => value.GetValue<string>())];

    private static IReadOnlyList<string> OneOrMany(JsonNode item, string one, string many) =>
        Optional(item, one) is { } single ? [single] : Strings(item, many);

    // A JSON number, or a string that reads as an invariant number (fixture 2.0 writes its decimals as strings).
    private static decimal? Number(JsonNode? value) => value?.GetValueKind() switch
    {
        JsonValueKind.Number => value.GetValue<decimal>(),
        JsonValueKind.String when decimal.TryParse(value.GetValue<string>(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number) => number,
        _ => null,
    };

    private static string? Cell(IReadOnlyDictionary<string, object?> row, string column) =>
        row.GetValueOrDefault(column) switch
        {
            null => null,
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            var value => value.ToString(),
        };

    private static IReadOnlyList<string> Ids(string? cell) =>
        cell is { Length: > 0 } text ? [.. text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)] : [];
}
