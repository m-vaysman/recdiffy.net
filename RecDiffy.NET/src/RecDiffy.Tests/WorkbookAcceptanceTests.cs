using System.Globalization;
using RecDiffy.Results;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

public class WorkbookAcceptanceTests
{
    private static readonly string Fixtures = Path.Combine(FindRepo(), "RecDiffy.NET", "test-data", "trade-reconciliation-fixtures");

    [Fact]
    public void Accepts_fixture_v1_membership()
    {
        var book = Path.Combine(Fixtures, "RecDiffy.NET-fixture-v1.xlsx");
        var left = Acceptance.XlsxSheet.Read(book, 1);
        var right = Acceptance.XlsxSheet.Read(book, 2);
        var expected = Acceptance.XlsxSheet.Read(book, 3);
        var profile = Reconciler.Compile(ProfileDocument.Parse(V1Profile), RuleRegistry.CreateCore().Let(TradingRulePack.AddTo));
        var result = Reconciler.Run(profile, Rows("left", left), Rows("right", right), V1Reference());
        var byKey = result.Rows.ToDictionary(row => row.Row.RowKey, StringComparer.Ordinal);

        Assert.Equal(left.Count + right.Count, result.Rows.Length);
        Assert.Equal(result.Rows.Length, result.Rows.Select(row => row.Row).Distinct().Count());
        foreach (var row in expected)
        {
            var outcome = row["ExpectedOutcome"]?.ToString();
            var internalIds = Ids(row["InternalRowIDs"]);
            var counterpartyIds = Ids(row["CounterpartyRowIDs"]);
            switch (outcome)
            {
                case "EXACT" or "NORMALIZED":
                    AssertMatched(result, byKey, internalIds, counterpartyIds, FieldOutcome.Exact);
                    break;
                case "FIELD_BREAK":
                    AssertMatched(result, byKey, internalIds, counterpartyIds, FieldOutcome.Break);
                    break;
                case "INTERNAL_ONLY":
                    Assert.All(internalIds, id => Assert.Equal(Disposition.LeftOnly, byKey[id].Disposition));
                    break;
                case "COUNTERPARTY_ONLY":
                    Assert.All(counterpartyIds, id => Assert.Equal(Disposition.RightOnly, byKey[id].Disposition));
                    break;
                case "AMBIGUOUS":
                    Assert.All(internalIds.Concat(counterpartyIds), id => Assert.Equal(Disposition.Ambiguous, byKey[id].Disposition));
                    break;
            }
        }
    }

    private static void AssertMatched(
        IReconciliationResult result,
        Dictionary<string, RowResult> byKey,
        string[] left,
        string[] right,
        FieldOutcome outcome)
    {
        var entry = result.Entries.Single(item => item.Left.Any(member => member.RowKey == left[0]));
        Assert.Equal(Alignment.Matched, entry.Alignment);
        Assert.True(entry.Outcome == outcome, $"{left[0]} -> {string.Join(",", entry.Right.Select(member => member.RowKey))} {entry.Outcome} diffs {string.Join(",", entry.Diffs.Select(diff => diff.Field.Name))}");
        Assert.Equal(left.OrderBy(id => id, StringComparer.Ordinal), entry.Left.Select(member => member.RowKey).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(right.OrderBy(id => id, StringComparer.Ordinal), entry.Right.Select(member => member.RowKey).OrderBy(id => id, StringComparer.Ordinal));
        Assert.All(left.Concat(right), id => Assert.Equal(Disposition.Matched, byKey[id].Disposition));
    }

    private static string[] Ids(object? cell) =>
        cell?.ToString() is { Length: > 0 } text
            ? [.. text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
            : [];

    private static RecordSource Rows(string name, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) =>
        RecordSources.FromObjects(name, rows, Map());

    private static SourceMap<IReadOnlyDictionary<string, object?>> Map() =>
        new SourceMap<IReadOnlyDictionary<string, object?>>()
            .Text("RowID", row => Text(row, "RowID"))
            .Text("ExecutionRef", row => Text(row, "ExecutionRef"))
            .Text("CounterpartyName", row => Text(row, "CounterpartyName"))
            .Text("AssetClass", row => Text(row, "AssetClass"))
            .Text("InstrumentID", row => Text(row, "InstrumentID"))
            .Text("TradeDate", row => Text(row, "TradeDate"))
            .Text("SettleDate", row => Text(row, "SettleDate"))
            .Text("MaturityDate", row => Text(row, "MaturityDate"))
            .Text("Direction", row => Text(row, "Direction"))
            .Text("Perspective", row => Text(row, "Perspective"))
            .Text("Currency", row => Text(row, "Currency"))
            .Decimal("Quantity", row => Decimal(row, "Quantity"))
            .Decimal("PriceOrRate", row => Decimal(row, "PriceOrRate"))
            .Decimal("FixedRate", row => Decimal(row, "FixedRate"));

    private static string? Text(IReadOnlyDictionary<string, object?> row, string column) =>
        row.TryGetValue(column, out var value) ? value?.ToString() : null;

    private static decimal? Decimal(IReadOnlyDictionary<string, object?> row, string column) =>
        row.TryGetValue(column, out var value) && value is decimal number ? number
        : row.TryGetValue(column, out value) && decimal.TryParse(value?.ToString(), CultureInfo.InvariantCulture, out var parsed) ? parsed
        : null;

    private static ReferenceDataSet V1Reference()
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

    private static string FindRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && File.Exists(Path.Combine(dir.FullName, "RecDiffy.NET", "test-data", "trade-reconciliation-fixtures", "RecDiffy.NET-fixture-v1.xlsx")) is false)
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }

    private const string V1Profile = """
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
