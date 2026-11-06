using System.Globalization;
using RecDiffy.Results;
using RecDiffy.RulePacks.Trading;

using RecDiffy.Tests.Acceptance;

namespace RecDiffy.IO.Tests;

// Runs a source through the engine with a profile that only normalizes, so a test sees what reached the profile:
// one row result per input row, in file order, with its canonical values.
internal static class Probe
{
    public static IReadOnlyList<CanonicalRow> Left(RecordSource left, string fields, string rules)
    {
        var result = Run(left, Empty(), fields, rules);
        return [.. result.Rows.Where(row => row.Row.Source == "left").OrderBy(row => row.Row.Ordinal).Select(row => result.Canonical(row.Row))];
    }

    public static ReconciliationResult Run(RecordSource left, RecordSource right, string fields, string rules) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(ProbeProfile(fields, rules)), RuleRegistry.CreateCore()),
            left,
            right,
            ReferenceDataSet.Empty);

    public static RecordSource Empty() => RecordSources.FromObjects("empty", Array.Empty<string>(), new SourceMap<string>());

    private static string ProbeProfile(string fields, string rules) => $$"""
        {
          "formatVersion": 1,
          "profileId": "probe",
          "version": "1.0.0",
          "fields": [ {{fields}} ],
          "normalization": { "left": { {{rules}} }, "right": { {{rules}} } },
          "paths": [ { "id": "all", "route": { "rule": "route.all" } } ]
        }
        """;
}

// fixture-v1 run from files: the same profile, reference data and 310 expected cases as WorkbookAcceptanceTests.
internal static class FixtureV1
{
    public static ReconciliationResult Run(RecordSource left, RecordSource right) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore().Let(TradingRulePack.AddTo)),
            left,
            right,
            Reference());

    // Returns how many expected cases the result satisfies; every case is checked.
    public static int Satisfied(ReconciliationResult result)
    {
        var byKey = result.Rows.ToDictionary(row => row.Row.RowKey, StringComparer.Ordinal);
        var expected = XlsxSheet.Read(TestFiles.FixtureV1, 3);
        return expected.Count(row => Holds(result, byKey, row["ExpectedOutcome"]?.ToString(), Ids(row["InternalRowIDs"]), Ids(row["CounterpartyRowIDs"])));
    }

    public static IReadOnlyList<string> Fingerprint(ReconciliationResult result) =>
    [
        .. result.Entries.Select(entry => string.Join(
            "|",
            entry.Id,
            entry.Alignment,
            entry.Composition,
            entry.Outcome,
            string.Join(",", entry.Left.Select(member => member.RowKey).Order(StringComparer.Ordinal)),
            string.Join(",", entry.Right.Select(member => member.RowKey).Order(StringComparer.Ordinal)),
            string.Join(",", entry.Diffs.Select(diff => $"{diff.Field.Name}:{diff.Outcome}:{diff.Delta.Number.ToString(CultureInfo.InvariantCulture)}:{diff.Delta.Days.ToString(CultureInfo.InvariantCulture)}")))).Order(StringComparer.Ordinal),
        .. result.Rows.Select(row => $"{row.Row.Source}:{row.Row.RowKey}|{row.Disposition}|{row.Validity}|{string.Join(",", row.Diagnostics.Select(diagnostic => diagnostic.Code))}").Order(StringComparer.Ordinal),
    ];

    private static bool Holds(ReconciliationResult result, Dictionary<string, RowResult> byKey, string? outcome, string[] left, string[] right) => outcome switch
    {
        "EXACT" or "NORMALIZED" => Matched(result, byKey, left, right, FieldOutcome.Exact),
        "FIELD_BREAK" => Matched(result, byKey, left, right, FieldOutcome.Break),
        "INTERNAL_ONLY" => left.All(id => byKey[id].Disposition == Disposition.LeftOnly),
        "COUNTERPARTY_ONLY" => right.All(id => byKey[id].Disposition == Disposition.RightOnly),
        "AMBIGUOUS" => left.Concat(right).All(id => byKey[id].Disposition == Disposition.Ambiguous),
        _ => false,
    };

    private static bool Matched(ReconciliationResult result, Dictionary<string, RowResult> byKey, string[] left, string[] right, FieldOutcome outcome) =>
        result.Entries.SingleOrDefault(item => item.Left.Any(member => member.RowKey == left[0])) is { Alignment: Alignment.Matched } entry
        && entry.Outcome == outcome
        && entry.Left.Select(member => member.RowKey).Order(StringComparer.Ordinal).SequenceEqual(left.Order(StringComparer.Ordinal), StringComparer.Ordinal)
        && entry.Right.Select(member => member.RowKey).Order(StringComparer.Ordinal).SequenceEqual(right.Order(StringComparer.Ordinal), StringComparer.Ordinal)
        && left.Concat(right).All(id => byKey[id].Disposition == Disposition.Matched);

    private static string[] Ids(object? cell) =>
        cell?.ToString() is { Length: > 0 } text ? [.. text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)] : [];

    private static ReferenceDataSet Reference()
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Elm International Bank"] = "ELM",
            ["ELM INTL"] = "ELM",
            ["Granite Markets PLC"] = "GRANITE",
            ["GRANITE MKT"] = "GRANITE",
            ["Falcon Asset Partners"] = "FALCON",
            ["Falcon AP"] = "FALCON",
            ["Beacon Capital LLP"] = "BEACON",
            ["Beacon Cap"] = "BEACON",
            ["Harbor Hedge Fund LP"] = "HARBOR",
            ["Harbor Fund"] = "HARBOR",
            ["Alder Bank NA"] = "ALDER",
            ["ALDER BK"] = "ALDER",
            ["Cedar Securities Ltd"] = "CEDAR",
            ["CEDAR SEC"] = "CEDAR",
            ["Driftwood Fund LP"] = "DRIFTWOOD",
            ["Driftwood HF"] = "DRIFTWOOD",
        };
        return ReferenceDataSet.Create("fixture-v1", new DateOnly(2026, 11, 4), "fixture-v1", aliases, aliases, new Dictionary<string, string>(StringComparer.Ordinal));
    }

    private static RuleRegistry Let(this RuleRegistry registry, Func<RuleRegistry, RuleRegistry> add) => add(registry);

    public const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "fixture-v1",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "RowID" }, "right": { "rowKey": "RowID" } },
          "fields": [
            { "name": "Counterparty", "type": "Identifier", "role": "Key" },
            { "name": "Instrument", "type": "Identifier", "role": "Key" },
            { "name": "Asset", "type": "Symbol", "role": "Key" },
            { "name": "Side", "type": "Symbol", "role": "Key" },
            { "name": "TradeDate", "type": "Date", "role": "Key" },
            { "name": "SettleDate", "type": "Date", "role": "Economic" },
            { "name": "MaturityDate", "type": "Date", "role": "Economic" },
            { "name": "Currency", "type": "Symbol", "role": "Economic" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Price", "type": "Decimal", "scale": 6, "role": "Economic" },
            { "name": "FixedRate", "type": "Decimal", "scale": 6, "role": "Economic" },
            { "name": "ExecRef", "type": "Identifier", "role": "Identity" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "party.resolveAlias", "from": "CounterpartyName", "to": "Counterparty" },
                { "rule": "identifier.map", "from": "InstrumentID", "to": "Instrument", "scheme": "INST" },
                { "rule": "symbol.map", "from": "AssetClass", "to": "Asset" },
                { "rule": "symbol.map", "from": "Currency", "to": "Currency" },
                { "rule": "identifier.map", "from": "ExecutionRef", "to": "ExecRef", "scheme": "EXEC" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
                { "rule": "date.parse", "from": "SettleDate", "to": "SettleDate", "formats": ["yyyy-MM-dd"] },
                { "rule": "date.parse", "from": "MaturityDate", "to": "MaturityDate", "formats": ["yyyy-MM-dd"] },
                { "rule": "decimal.parse", "from": "PriceOrRate", "to": "Price" },
                { "rule": "decimal.parse", "from": "FixedRate", "to": "FixedRate" }
              ],
              "derive": [
                { "rule": "side.resolve", "code": "Direction", "quantity": "Quantity", "asset": "Asset" },
                { "rule": "quantity.absolute", "from": "Quantity", "to": "Quantity" }
              ]
            },
            "right": {
              "identify": [
                { "rule": "party.resolveAlias", "from": "CounterpartyName", "to": "Counterparty" },
                { "rule": "identifier.map", "from": "InstrumentID", "to": "Instrument", "scheme": "INST" },
                { "rule": "symbol.map", "from": "AssetClass", "to": "Asset" },
                { "rule": "symbol.map", "from": "Currency", "to": "Currency" },
                { "rule": "identifier.map", "from": "ExecutionRef", "to": "ExecRef", "scheme": "EXEC" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
                { "rule": "date.parse", "from": "SettleDate", "to": "SettleDate", "formats": ["yyyy-MM-dd"] },
                { "rule": "date.parse", "from": "MaturityDate", "to": "MaturityDate", "formats": ["yyyy-MM-dd"] },
                { "rule": "decimal.parse", "from": "PriceOrRate", "to": "Price" },
                { "rule": "decimal.parse", "from": "FixedRate", "to": "FixedRate" }
              ],
              "derive": [
                { "rule": "side.resolve", "code": "Direction", "quantity": "Quantity", "perspective": "Perspective", "invertWhen": "Sender", "asset": "Asset" },
                { "rule": "quantity.absolute", "from": "Quantity", "to": "Quantity" }
              ]
            }
          },
          "paths": [ { "id": "trades", "route": { "rule": "route.all" }, "passes": [
            { "id": "shared", "pass": "match.naturalKey", "key": ["ExecRef"] },
            { "id": "natural", "pass": "match.naturalKey", "key": ["Counterparty", "Instrument", "Asset", "TradeDate", "Side", "Quantity"] }
          ] } ]
        }
        """;
}
