using System.Globalization;
using RecDiffy.Results;

namespace RecDiffy.Tests;

// Spec v3 §10.1: "No decision reads RowKey", and after an ID scramble "the results must be identical" (AC-7). §5.3: a
// P3 group's projection follows §5.2's field rules: "No value ever comes from 'the first row'". The N:1 projection
// (ResultBuilder.Aggregate) starts from the first allocation by row key, so a condition on a raw column reads that
// allocation's column. Packages stopped doing this in #217.
public class GroupProjectionRawColumnTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "group-projection-raw-column",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "symbol.map", "from": "Cpty", "to": "Counterparty" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" } ]
            },
            "right": {
              "identify": [ { "rule": "symbol.map", "from": "Cpty", "to": "Counterparty" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" } ]
            }
          },
          "paths": [ {
            "id": "p", "route": { "rule": "route.all" },
            "passes": [ { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Counterparty"], "conserve": ["Quantity"] } ],
            "diff": { "fields": { "NetCash": { "applicableWhen": { "field": "Asset", "equals": "Swap" } } } }
          } ]
        }
        """;

    private static readonly SourceMap<Row> Map = new SourceMap<Row>()
        .Text("Id", row => row.Id).Text("Cpty", row => "JPM").Text("Asset", row => row.Asset).Text("Qty", row => row.Qty).Text("Cash", row => row.Cash);

    // Two allocations sum to the block's quantity. One says Swap in the raw Asset column and the other Bond; the block
    // says Bond. Their net cash (500 + 400 = 900) differs from the block's 1,000. Only the allocations' row keys differ.
    [Fact]
    public void A_condition_on_a_raw_column_of_a_group_does_not_depend_on_row_keys()
    {
        var original = Describe(Run([new("A1", "Swap", "50", "500"), new("A2", "Bond", "50", "400")]));
        var scrambled = Describe(Run([new("A2", "Swap", "50", "500"), new("A1", "Bond", "50", "400")]));

        Assert.True(string.Equals(original, scrambled, StringComparison.Ordinal), $"original: {original} || scrambled: {scrambled}");
    }

    private static string Describe(IReconciliationResult result) =>
        string.Join(" || ", result.Entries.Select(entry =>
            $"{entry.Alignment}/{entry.Outcome} [{string.Join(", ", entry.Diffs.Select(diff => $"{diff.Field.Name}:{diff.Outcome}:{diff.Delta.Number.ToString(CultureInfo.InvariantCulture)}"))}]"));

    private static IReconciliationResult Run(Row[] left) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", [new Row("B1", "Bond", "100", "1000")], Map),
            ReferenceDataSet.Empty);

    public sealed record Row(string Id, string Asset, string Qty, string Cash);
}
