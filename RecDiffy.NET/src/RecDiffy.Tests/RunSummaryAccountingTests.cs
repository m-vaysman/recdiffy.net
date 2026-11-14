using RecDiffy.Results;

namespace RecDiffy.Tests;

// #92: every unmatched row has an entry, so RunSummary counts it. A row no route accepts gets its own LeftOnly or
// RightOnly entry with no path and the code Unrouted.
public class RunSummaryAccountingTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "summary-accounting",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Id", "type": "Identifier", "role": "Key" },
            { "name": "Asset", "type": "Symbol", "role": "Key" },
            { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "id" }, { "rule": "symbol.map", "from": "Asset", "to": "Asset" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "id" }, { "rule": "symbol.map", "from": "Asset", "to": "Asset" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ]
            }
          },
          "paths": [ { "id": "equity", "route": { "rule": "route.byField", "field": "Asset", "equals": "Equity" },
            "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Id"] } ] } ]
        }
        """;

    private static readonly SourceMap<(string Id, string Asset, string Amount)> Map = new SourceMap<(string Id, string Asset, string Amount)>()
        .Text("Id", row => row.Id).Text("Asset", row => row.Asset).Text("Amount", row => row.Amount);

    [Fact]
    public void Every_unmatched_row_has_an_entry_and_RunSummary_counts_it()
    {
        var result = Run([("T1", "Equity", "100"), ("T2", "Bond", "500"), ("T3", "Equity", "7")], [("T1", "Equity", "100"), ("T4", "Bond", "900")]);

        Assert.Equal((1, 2, 1), (result.Summary.Matched, result.Summary.LeftOnly, result.Summary.RightOnly));
        Assert.Equal(result.Rows.Count(row => row.Disposition == Disposition.LeftOnly), result.Summary.LeftOnly);
        Assert.Equal(result.Rows.Count(row => row.Disposition == Disposition.RightOnly), result.Summary.RightOnly);
        Assert.All(result.Rows.Where(row => row.Disposition is Disposition.LeftOnly or Disposition.RightOnly), row => Assert.NotNull(row.EntryId));
    }

    [Fact]
    public void An_unrouted_row_is_explained_as_Unrouted_with_no_path_and_no_committing_pass()
    {
        var result = Run([("T2", "Bond", "500")], []);

        var entry = Assert.Single(result.Entries);
        var attempt = Assert.Single(entry.Explanation.Trail);
        Assert.Equal((Alignment.LeftOnly, "", "Unrouted", (string?)null), (entry.Alignment, entry.PathId, attempt.Detail, entry.Explanation.CommittedBy));
    }

    private static IReconciliationResult Run((string Id, string Asset, string Amount)[] left, (string Id, string Asset, string Amount)[] right) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", right, Map),
            ReferenceDataSet.Empty);
}
