using RecDiffy.Results;

namespace RecDiffy.Tests;

// Adversarial pass on main. A package field aggregated with Same or FromRole is copied from a leg. For an Identifier,
// the copy is re-interned under scheme "PACKAGE" (Packages.InternIdentifier). Pair keys include the scheme
// (MatchKeys.IdentifierText, since #133), so the package's key never equals the other side's key.
public class AdversarialPackageIdentifierTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "adv-package-identifier",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "PackageRef", "type": "Text", "role": "Package" },
            { "name": "LegRole", "type": "Symbol", "role": "Package" },
            { "name": "Counterparty", "type": "Identifier", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Key" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "text.copy", "from": "PackageRef", "to": "PackageRef" },
                { "rule": "symbol.map", "from": "LegRole", "to": "LegRole" },
                { "rule": "identifier.map", "from": "Cpty", "to": "Counterparty", "scheme": "PARTY" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Cpty", "to": "Counterparty", "scheme": "PARTY" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ]
            }
          },
          "consolidation": {
            "left": { "packages": [ { "link": "PackageRef", "role": "LegRole", "requiredRoles": ["PAY", "REC"],
                                      "aggregate": { "Counterparty": "Same", "Quantity": "Same" } } ] }
          },
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P2", "pass": "match.naturalKey", "key": ["Counterparty", "Quantity"] } ] } ]
        }
        """;

    [Theory]
    [InlineData("Same")]
    [InlineData("FromRole(PAY)")]
    public void A_package_matches_the_other_side_on_an_identifier_its_legs_agree_on(string aggregator)
    {
        var map = new SourceMap<(string Id, string Link, string Role, string Cpty, string Qty)>()
            .Text("Id", row => row.Id).Text("PackageRef", row => row.Link).Text("LegRole", row => row.Role).Text("Cpty", row => row.Cpty).Text("Qty", row => row.Qty);
        var json = Profile.Replace("\"Counterparty\": \"Same\"", $"\"Counterparty\": \"{aggregator}\"", StringComparison.Ordinal);
        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(json), RuleRegistry.CreateCore()),
            RecordSources.FromObjects<(string, string, string, string, string)>("left", [("L1", "PLK-1", "PAY", "JPM", "13000000"), ("L2", "PLK-1", "REC", "JPM", "13000000")], map),
            RecordSources.FromObjects<(string, string, string, string, string)>("right", [("R1", "", "", "JPM", "13000000")], map),
            ReferenceDataSet.Empty);

        Assert.All(result.Rows, row => Assert.True(row.Disposition == Disposition.Matched, $"{aggregator}: {row.Row.RowKey} is {row.Disposition}"));
    }
}
