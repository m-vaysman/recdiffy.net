using RecDiffy.Results;

namespace RecDiffy.Tests;

// Adversarial pass on main. Spec §10.1: the same rows, in any order, give the same groups, alignments, outcomes and
// deltas. A package field that has no aggregator is copied from whichever leg comes first in the input.
public class AdversarialPackageOrderTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "adv-package-order",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "PackageRef", "type": "Text", "role": "Package" },
            { "name": "LegRole", "type": "Symbol", "role": "Package" },
            { "name": "Uti", "type": "Identifier", "role": "Key" },
            { "name": "SettleDate", "type": "Date", "role": "Economic" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "text.copy", "from": "PackageRef", "to": "PackageRef" },
                { "rule": "symbol.map", "from": "LegRole", "to": "LegRole" },
                { "rule": "identifier.map", "from": "Uti", "to": "Uti", "scheme": "uti" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "SettleDate", "to": "SettleDate", "formats": ["yyyy-MM-dd"] },
                { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" }
              ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Uti", "to": "Uti", "scheme": "uti" } ],
              "parse": [
                { "rule": "date.parse", "from": "SettleDate", "to": "SettleDate", "formats": ["yyyy-MM-dd"] },
                { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" }
              ]
            }
          },
          "consolidation": {
            "left": { "packages": [ { "link": "PackageRef", "role": "LegRole", "requiredRoles": ["NEAR", "FAR"], "aggregate": { "Quantity": "Same" } } ] }
          },
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Uti"] } ] } ]
        }
        """;

    private static readonly SourceMap<Row> Map = new SourceMap<Row>()
        .Text("Id", row => row.Id)
        .Text("PackageRef", row => row.Link)
        .Text("LegRole", row => row.Role)
        .Text("Uti", row => row.Uti)
        .Text("SettleDate", row => row.Settle)
        .Text("Quantity", row => row.Quantity);

    // An FX swap booked as two legs. Each leg has its own UTI and settle date; the street reports the near leg.
    private static readonly Row Near = new("L-NEAR", "SWAP-1", "NEAR", "UTI-NEAR", "2025-06-11", "1000000");
    private static readonly Row Far = new("L-FAR", "SWAP-1", "FAR", "UTI-FAR", "2025-09-11", "1000000");
    private static readonly Row Street = new("R1", "", "", "UTI-NEAR", "2025-06-11", "1000000");

    [Fact(Skip = "🔴 #164 (#174 A): unaggregated fields come from the first leg")]
    public void Package_row_dispositions_do_not_depend_on_leg_order()
    {
        var nearFirst = Run([Near, Far], [Street]);
        var farFirst = Run([Far, Near], [Street]);

        Assert.Equal(Dispositions(nearFirst), Dispositions(farFirst));
    }

    [Fact(Skip = "🔴 #164 (#174 A): unaggregated fields come from the first leg")]
    public void Package_projection_does_not_depend_on_leg_order()
    {
        var nearFirst = Run([Near, Far], []);
        var farFirst = Run([Far, Near], []);

        Assert.Equal(Projected(nearFirst), Projected(farFirst));
    }

    private static string Dispositions(ReconciliationResult result) =>
        string.Join("; ", result.Rows.OrderBy(row => row.Row.RowKey, StringComparer.Ordinal).Select(row => $"{row.Row.RowKey}={row.Disposition}"));

    // The package item as the engine sees it, through the consolidated row behind each leg.
    private static string Projected(ReconciliationResult result) =>
        string.Join("; ", result.Rows.Where(row => row.Row.Source == "left").OrderBy(row => row.Row.RowKey, StringComparer.Ordinal)
            .Select(row => $"{row.Row.RowKey}: Uti={result.ConsolidatedItem(row.Row).Display("Uti")} Settle={result.ConsolidatedItem(row.Row).Display("SettleDate")}"));

    private static ReconciliationResult Run(Row[] left, Row[] right) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", right, Map),
            ReferenceDataSet.Empty);

    public sealed record Row(string Id, string Link, string Role, string Uti, string Settle, string Quantity);
}
