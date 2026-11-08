using RecDiffy.Results;

namespace RecDiffy.Tests;

// Adversarial pass on main. Two decimals with the same value and a different scale, such as 100 and 100.00, are one
// number. Spec §3.2: copies are identical when every mapped raw value is equal, "numbers as decimal". Spec §5.2: a
// Same aggregator needs the members to agree.
public class AdversarialDecimalScaleTests
{
    private const string DuplicateProfile = """
        {
          "formatVersion": 1,
          "profileId": "adv-duplicate-scale",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Id", "type": "Identifier", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "id" } ], "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ] },
            "right": { "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "id" } ], "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ] }
          },
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Id"] } ] } ]
        }
        """;

    private const string PackageProfile = """
        {
          "formatVersion": 1,
          "profileId": "adv-package-same-scale",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "PackageRef", "type": "Text", "role": "Package" },
            { "name": "LegRole", "type": "Symbol", "role": "Package" },
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "text.copy", "from": "PackageRef", "to": "PackageRef" },
                { "rule": "symbol.map", "from": "LegRole", "to": "LegRole" },
                { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            }
          },
          "consolidation": {
            "left": { "packages": [ { "link": "PackageRef", "role": "LegRole", "requiredRoles": ["PAY", "REC"], "aggregate": { "Quantity": "Same" } } ] }
          },
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ] } ]
        }
        """;

    [Fact]
    public void Duplicate_copies_whose_decimals_differ_only_in_scale_are_identical()
    {
        var map = new SourceMap<(string Id, decimal Qty)>().Text("Id", row => row.Id).Decimal("Qty", row => row.Qty);
        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(DuplicateProfile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects<(string Id, decimal Qty)>("left", [("T1", 100m), ("T1", 100.00m)], map),
            RecordSources.FromObjects<(string Id, decimal Qty)>("right", [("T1", 100m)], map),
            ReferenceDataSet.Empty);

        var left = result.Rows.Where(row => row.Row.Source == "left").ToArray();
        Assert.All(left, row => Assert.Equal(Disposition.Matched, row.Disposition));
        Assert.All(left, row => Assert.Contains(row.Diagnostics, diagnostic => diagnostic.Code == "DuplicateRowCollapsed"));
    }

    [Fact]
    public void Package_legs_whose_quantities_differ_only_in_scale_agree_under_Same()
    {
        var map = new SourceMap<(string Id, string Link, string Role, string Ref, string Qty)>()
            .Text("Id", row => row.Id).Text("PackageRef", row => row.Link).Text("LegRole", row => row.Role).Text("Ref", row => row.Ref).Text("Quantity", row => row.Qty);
        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(PackageProfile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects<(string, string, string, string, string)>("left", [("L1", "PLK-1", "PAY", "T1", "13000000"), ("L2", "PLK-1", "REC", "T1", "13000000.00")], map),
            RecordSources.FromObjects<(string, string, string, string, string)>("right", [("R1", "", "", "T1", "13000000.00")], map),
            ReferenceDataSet.Empty);

        Assert.All(result.Rows, row => Assert.True(row.Disposition == Disposition.Matched, $"{row.Row.RowKey}: {row.Disposition} {string.Join(",", row.Diagnostics.Select(d => d.Code))}"));
    }
}
