using RecDiffy.Results;

namespace RecDiffy.Tests;

// Spec v3 §5.2: a field without an aggregator takes the legs' agreed value. Legs that disagree make a used field unknown,
// with MembersDisagree; an unused field needs nothing. A profile declares FromRole when one leg should represent the package.
public class PackageFieldRuleTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "package-field-rules",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "PackageRef", "type": "Text", "role": "Package" },
            { "name": "LegRole", "type": "Symbol", "role": "Package" },
            { "name": "Uti", "type": "Identifier", "role": "Key" },
            { "name": "SettleDate", "type": "Date", "role": "Economic" },
            { "name": "Note", "type": "Text", "role": "Text" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "text.copy", "from": "PackageRef", "to": "PackageRef" },
                { "rule": "symbol.map", "from": "LegRole", "to": "LegRole" },
                { "rule": "identifier.map", "from": "Uti", "to": "Uti", "scheme": "uti" },
                { "rule": "text.copy", "from": "Note", "to": "Note" }
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
        .Text("Note", row => row.Note)
        .Text("Quantity", row => row.Quantity);

    private static readonly Row Near = new("L-NEAR", "SWAP-1", "NEAR", "UTI-NEAR", "2025-06-11", "near leg", "1000000");
    private static readonly Row Far = new("L-FAR", "SWAP-1", "FAR", "UTI-FAR", "2025-09-11", "far leg", "1000000");
    private static readonly Row Street = new("R1", "", "", "UTI-NEAR", "2025-06-11", "", "1000000");

    [Fact]
    public void A_used_field_the_legs_disagree_on_is_unknown_with_MembersDisagree()
    {
        var result = Run(Profile, [Far, Near], []);
        var leg = result.Rows.Single(row => row.Row.RowKey == "L-NEAR");
        var item = ((ReconciliationResult)result).ConsolidatedItem(leg.Row);

        Assert.Equal((true, true), (item.IsNull("Uti"), item.IsNull("SettleDate")));
        Assert.Equal(InputValidity.ValidWithWarnings, leg.Validity);
        Assert.Equal(["SettleDate", "Uti"], leg.Diagnostics.Where(diagnostic => diagnostic.Code == "MembersDisagree").Select(diagnostic => diagnostic.Field ?? "").Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    [Fact]
    public void An_unused_field_the_legs_disagree_on_needs_nothing()
    {
        var result = Run(Profile, [Near, Far], []);
        var leg = result.Rows.Single(row => row.Row.RowKey == "L-FAR");

        Assert.True(((ReconciliationResult)result).ConsolidatedItem(leg.Row).IsNull("Note"));
        Assert.DoesNotContain(leg.Diagnostics, diagnostic => diagnostic is { Code: "MembersDisagree", Field: "Note" });
    }

    [Fact]
    public void FromRole_decides_which_leg_represents_the_package_whatever_the_leg_order()
    {
        var declared = Profile.Replace("\"aggregate\": { \"Quantity\": \"Same\" }", "\"aggregate\": { \"Quantity\": \"Same\", \"Uti\": \"FromRole(NEAR)\", \"SettleDate\": \"FromRole(NEAR)\" }", StringComparison.Ordinal);

        foreach (Row[] legs in (Row[][])[[Near, Far], [Far, Near]])
        {
            var result = Run(declared, legs, [Street]);

            Assert.All(result.Rows, row => Assert.Equal(Disposition.Matched, row.Disposition));
            Assert.DoesNotContain(result.Rows.SelectMany(row => row.Diagnostics), diagnostic => diagnostic.Code == "MembersDisagree" && diagnostic.Field is "Uti" or "SettleDate");
        }
    }

    private static IReconciliationResult Run(string profile, Row[] left, Row[] right) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", right, Map),
            ReferenceDataSet.Empty);

    public sealed record Row(string Id, string Link, string Role, string Uti, string Settle, string Note, string Quantity);
}
