using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

public class ArgonSniffTests
{
    [Fact]
    public void Packages_Pack_quarantining_a_multi_leg_package_does_not_throw_duplicate_key()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse("""
            {
              "formatVersion": 1,
              "profileId": "pkg-inconsistent",
              "version": "1.0.0",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "PackageRef", "type": "Text", "role": "Package" },
                { "name": "LegRole", "type": "Symbol", "role": "Package" },
                { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
              ],
              "normalization": {
                "left": {
                  "identify": [
                    { "rule": "text.copy", "from": "PackageRef", "to": "PackageRef" },
                    { "rule": "symbol.map", "from": "LegRole", "to": "LegRole" }
                  ],
                  "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
                }
              },
              "consolidation": {
                "left": {
                  "packages": [ {
                    "link": "PackageRef",
                    "role": "LegRole",
                    "requiredRoles": ["PAY", "REC"],
                    "aggregate": { "Quantity": "Same" }
                  } ]
                }
              },
              "paths": [ { "id": "p", "route": { "rule": "route.all" } } ]
            }
            """), RuleRegistry.CreateCore());

        var map = new SourceMap<(string Id, string Link, string Role, string Qty)>()
            .Text("Id", r => r.Id)
            .Text("PackageRef", r => r.Link)
            .Text("LegRole", r => r.Role)
            .Text("Quantity", r => r.Qty);

        var leftRows = new[]
        {
            ("L1", "PKG-1", "PAY", "100"),
            ("L2", "PKG-1", "REC", "200"),
        };

        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", leftRows, map),
            RecordSources.FromObjects("right", Array.Empty<(string, string, string, string)>(), map),
            ReferenceDataSet.Empty);

        Assert.All(result.Rows, row =>
        {
            Assert.Equal(Disposition.Quarantined, row.Disposition);
            Assert.Contains(row.Diagnostics, d => d.Code == "PackageInconsistent");
        });
    }

    [Fact]
    public void DataReaderSource_and_DecimalParse_do_not_throw_on_NaN_infinity_or_huge_double()
    {
        var row = RowContext.Create("L1", Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal) { ["Quantity"] = double.NaN });
#pragma warning disable CS0618 // DecimalParseRule is obsolete; this test still covers it.
        var rule = new DecimalParseRule("Quantity", "Quantity", 2, MidpointMode.ToEven);
#pragma warning restore CS0618
        var updated = row;
        rule.Apply(ref updated);

        Assert.Contains(updated.Diagnostics, d => d.Code == "DecimalParse");
    }
}
