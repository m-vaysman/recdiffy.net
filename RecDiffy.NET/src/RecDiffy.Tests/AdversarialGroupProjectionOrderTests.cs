using RecDiffy.Results;

namespace RecDiffy.Tests;

// Adversarial pass on main. Spec §10.1: the same rows in any order give the same outcomes and deltas. A group-sum
// projection keeps every non-decimal field of its first row (ResultBuilder.Aggregate); when two allocations share a
// conserved class, which one is first depends on input order.
public class AdversarialGroupProjectionOrderTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "adv-group-projection-order",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "SettleDate", "type": "Date", "role": "Economic" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "symbol.map", "from": "Cpty", "to": "Counterparty" } ],
              "parse": [ { "rule": "date.parse", "from": "Settle", "to": "SettleDate", "formats": ["yyyy-MM-dd"] }, { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" } ]
            },
            "right": {
              "identify": [ { "rule": "symbol.map", "from": "Cpty", "to": "Counterparty" } ],
              "parse": [ { "rule": "date.parse", "from": "Settle", "to": "SettleDate", "formats": ["yyyy-MM-dd"] }, { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" } ]
            }
          },
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [
            { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Counterparty"], "conserve": ["Quantity", "NetCash"] } ] } ]
        }
        """;

    private static readonly SourceMap<Row> Map = new SourceMap<Row>()
        .Text("Id", row => row.Id).Text("Cpty", row => "JPM").Text("Settle", row => row.Settle).Text("Qty", row => row.Qty).Text("Cash", row => row.Cash);

    // Two equal allocations that settle on different days, against one block.
    private static readonly Row First = new("A1", "2025-06-11", "50", "500");
    private static readonly Row Second = new("A2", "2025-06-12", "50", "500");
    private static readonly Row Block = new("B1", "2025-06-11", "100", "1000");

    [Fact(Skip = "🔴 #167 (#174 A): an N:1 projection takes fields from its first row")]
    public void A_group_sum_outcome_does_not_depend_on_allocation_order()
    {
        var inOrder = Run([First, Second]);
        var reversed = Run([Second, First]);

        Assert.Equal(Describe(inOrder), Describe(reversed));
    }

    private static string Describe(ReconciliationResult result) =>
        string.Join("; ", result.Entries.Select(entry =>
            $"{entry.Alignment}/{entry.Outcome} [{string.Join(",", entry.Diffs.Select(diff => $"{diff.Field}:{diff.Outcome}:{diff.Delta.Days}"))}]"));

    private static ReconciliationResult Run(Row[] left) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", [Block], Map),
            ReferenceDataSet.Empty);

    public sealed record Row(string Id, string Settle, string Qty, string Cash);
}
