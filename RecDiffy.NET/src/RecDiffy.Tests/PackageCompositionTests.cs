using RecDiffy.Results;

namespace RecDiffy.Tests;

// Spec v3 §5.3: allocations linked as a package and allocations found by a group pass "both produce the same result
// shape (ManyToOne)". §6.5 maps two legs against one street row to ManyToOne + Package. Found by the acceptance-fixture
// matrix (#39): fixture 2.0's 12 linked allocations and 8 swaps, and Sonnet's 8 swaps, come out OneToOne + Package.
public class PackageCompositionTests
{
    [Fact(Skip = "🔴 #308: a package of several rows matched to one row is OneToOne + Package, not ManyToOne + Package")]
    public void Two_legs_against_one_street_row_are_ManyToOne_and_Package()
    {
        var result = Run(
            [new("L1", "SWAP-1", "LEG_1_PAY", "1000"), new("L2", "SWAP-1", "LEG_2_RECEIVE", "1000")],
            [new("R1", null, null, "1000")],
            Legs);

        var entry = Assert.Single(result.Entries);

        Assert.Equal(Alignment.Matched, entry.Alignment);
        Assert.Equal(Composition.ManyToOne | Composition.Package, entry.Composition);
    }

    [Fact(Skip = "🔴 #308: a package of several rows matched to one row is OneToOne + Package, not ManyToOne + Package")]
    public void Three_linked_allocations_against_one_block_are_ManyToOne_and_Package()
    {
        var result = Run(
            [new("A1", "BLOCK-1", "ALLOCATION", "300"), new("A2", "BLOCK-1", "ALLOCATION", "200"), new("A3", "BLOCK-1", "ALLOCATION", "500")],
            [new("B1", null, null, "1000")],
            Allocations);

        var entry = Assert.Single(result.Entries);

        Assert.Equal(Alignment.Matched, entry.Alignment);
        Assert.Equal(Composition.ManyToOne | Composition.Package, entry.Composition);
    }

    private sealed record Row(string Id, string? Link, string? Role, string Quantity);

    private static ReconciliationResult Run(IReadOnlyList<Row> left, IReadOnlyList<Row> right, string profile) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map()),
            RecordSources.FromObjects("right", right, Map()),
            ReferenceDataSet.Empty);

    private static SourceMap<Row> Map() =>
        new SourceMap<Row>()
            .Text("Id", row => row.Id)
            .Text("Trade", _ => "T1")
            .Text("Link", row => row.Link)
            .Text("Role", row => row.Role)
            .Text("Quantity", row => row.Quantity);

    private const string Legs = """
        {
          "formatVersion": 1,
          "profileId": "package-composition-legs",
          "version": "1.0.0",
          "culture": "en-US",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Trade", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Key" },
            { "name": "Link", "type": "Identifier", "role": "Package" },
            { "name": "Role", "type": "Symbol", "role": "Package" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "symbol.map", "from": "Trade", "to": "Trade" },
                { "rule": "identifier.map", "from": "Link", "to": "Link", "scheme": "LINK" },
                { "rule": "symbol.map", "from": "Role", "to": "Role" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            },
            "right": {
              "identify": [ { "rule": "symbol.map", "from": "Trade", "to": "Trade" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            }
          },
          "consolidation": { "left": { "packages": [ { "link": "Link", "role": "Role", "requiredRoles": ["LEG_1_PAY", "LEG_2_RECEIVE"], "aggregate": { "Quantity": "Same" } } ] } },
          "paths": [ { "id": "trades", "route": { "rule": "route.all" }, "passes": [
            { "id": "P2", "pass": "match.naturalKey", "key": ["Trade", "Quantity"] }
          ] } ]
        }
        """;

    private const string Allocations = """
        {
          "formatVersion": 1,
          "profileId": "package-composition-allocations",
          "version": "1.0.0",
          "culture": "en-US",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Trade", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Key" },
            { "name": "Link", "type": "Identifier", "role": "Package" },
            { "name": "Role", "type": "Symbol", "role": "Package" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "symbol.map", "from": "Trade", "to": "Trade" },
                { "rule": "identifier.map", "from": "Link", "to": "Link", "scheme": "LINK" },
                { "rule": "symbol.map", "from": "Role", "to": "Role" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            },
            "right": {
              "identify": [ { "rule": "symbol.map", "from": "Trade", "to": "Trade" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            }
          },
          "consolidation": { "left": { "packages": [ { "link": "Link", "role": "Role", "aggregate": { "Quantity": "Sum" } } ] } },
          "paths": [ { "id": "trades", "route": { "rule": "route.all" }, "passes": [
            { "id": "P2", "pass": "match.naturalKey", "key": ["Trade", "Quantity"] }
          ] } ]
        }
        """;
}
