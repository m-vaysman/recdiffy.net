using RecDiffy.Results;

namespace RecDiffy.Tests;

// #236: an N:1 projection keeps only the raw columns its allocations agree on, and warns on a used one they disagree on.
public class GroupProjectionRawAgreementTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "group-projection-raw-agreement",
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
        .Text("Id", row => row.Id).Text("Cpty", row => "JPM").Text("Asset", row => row.Asset).Text("Note", row => row.Note)
        .Text("Qty", row => row.Qty).Text("Cash", row => row.Cash);

    // Both allocations say Swap, so the condition holds on the projection and the 100 net cash gap is a break.
    [Fact]
    public void A_raw_column_every_allocation_agrees_on_stays_on_the_projection()
    {
        var result = Run([new("A1", "Swap", "x", "50", "500"), new("A2", "Swap", "x", "50", "400")]);

        var entry = Assert.Single(result.Entries);
        Assert.Equal((Alignment.Matched, FieldOutcome.Break), (entry.Alignment, entry.Outcome));
        Assert.Equal(100m, decimal.Abs(Assert.Single(entry.Diffs).Delta.Number));
        Assert.DoesNotContain(result.Rows.SelectMany(row => row.Diagnostics), diagnostic => diagnostic.Code == "MembersDisagree");
    }

    // A text that differs only by surrounding spaces reads the same in a condition, so the allocations agree.
    [Fact]
    public void A_raw_column_that_differs_only_by_surrounding_spaces_is_agreed()
    {
        var result = Run([new("A1", " Swap", "x", "50", "500"), new("A2", "Swap ", "x", "50", "400")]);

        Assert.Equal(FieldOutcome.Break, Assert.Single(result.Entries).Outcome);
    }

    // The allocations disagree on Asset, which a condition reads, and the block says Bond: the column is unknown on the
    // projection, so neither side holds the condition and NetCash is not compared. Each allocation row names Asset in a
    // MembersDisagree warning.
    [Fact]
    public void A_used_raw_column_the_allocations_disagree_on_is_unknown_and_named_in_a_warning()
    {
        var result = Run([new("A1", "Swap", "x", "50", "500"), new("A2", "Bond", "x", "50", "400")], block: "Bond");

        var entry = Assert.Single(result.Entries);
        Assert.Equal((Alignment.Matched, FieldOutcome.Exact), (entry.Alignment, entry.Outcome));
        var allocations = result.Rows.Where(row => row.Row.Source == "left").ToArray();
        Assert.Equal(2, allocations.Length);
        Assert.All(allocations, row => Assert.Contains(row.Diagnostics, diagnostic => diagnostic is { Code: "MembersDisagree", Field: "Asset", Severity: DiagnosticSeverity.Warning }));
        Assert.DoesNotContain(result.Rows.Single(row => row.Row.Source == "right").Diagnostics, diagnostic => diagnostic.Code == "MembersDisagree");
    }

    // Note is read by nothing, so allocations that disagree on it raise no warning.
    [Fact]
    public void An_unused_raw_column_the_allocations_disagree_on_raises_no_warning()
    {
        var result = Run([new("A1", "Swap", "first", "50", "500"), new("A2", "Swap", "second", "50", "400")]);

        Assert.Equal(FieldOutcome.Break, Assert.Single(result.Entries).Outcome);
        Assert.DoesNotContain(result.Rows.SelectMany(row => row.Diagnostics), diagnostic => diagnostic.Code == "MembersDisagree");
    }

    private static ReconciliationResult Run(Row[] left, string block = "Swap") =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", [new Row("B1", block, "x", "100", "1000")], Map),
            ReferenceDataSet.Empty);

    public sealed record Row(string Id, string Asset, string Note, string Qty, string Cash);
}
