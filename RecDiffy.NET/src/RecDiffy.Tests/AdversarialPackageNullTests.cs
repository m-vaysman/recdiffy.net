using RecDiffy.Results;

namespace RecDiffy.Tests;

// Adversarial pass on main: a package Sum or DifferenceByRole must not turn an unknown (null) leg amount into zero.
// Spec §7.5: "Null as zero: never by default; a profile must name the field."
public class AdversarialPackageNullTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "adv-package-null",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "PackageRef", "type": "Text", "role": "Package" },
            { "name": "LegRole", "type": "Symbol", "role": "Package" },
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "text.copy", "from": "PackageRef", "to": "PackageRef" },
                { "rule": "symbol.map", "from": "LegRole", "to": "LegRole" },
                { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "NetCash", "to": "NetCash" } ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" } ],
              "parse": [ { "rule": "decimal.parse", "from": "NetCash", "to": "NetCash" } ]
            }
          },
          "consolidation": {
            "left": {
              "packages": [ {
                "link": "PackageRef",
                "role": "LegRole",
                "requiredRoles": ["PAY", "REC"],
                "aggregate": { "NetCash": "Sum" }
              } ]
            }
          },
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ] } ]
        }
        """;

    private static readonly SourceMap<Row> Map = new SourceMap<Row>()
        .Text("Id", row => row.Id)
        .Text("PackageRef", row => row.Link)
        .Text("LegRole", row => row.Role)
        .Text("Ref", row => row.Ref)
        .Text("NetCash", row => row.NetCash);

    [Theory]
    [InlineData("Sum")]
    [InlineData("DifferenceByRole(PAY,REC)")]
    public void A_package_with_a_leg_whose_amount_is_unknown_has_an_unknown_total(string aggregator)
    {
        // The REC leg's net cash is blank: unknown, not zero.
        Row[] left = [new("L1", "PKG-1", "PAY", "T1", "100.00"), new("L2", "PKG-1", "REC", "T1", "")];
        Row[] right = [new("R1", "", "", "T1", "100.00")];

        var result = Run(aggregator, left, right);
        var entry = Assert.Single(result.Entries, entry => entry.Alignment == Alignment.Matched);
        var package = result.Projection(entry.Id, Side.Left);

        Assert.True(package.IsNull("NetCash"), $"{aggregator}: package NetCash should be unknown, but is {package.Text("NetCash")}; entry outcome {entry.Outcome}.");
    }

    [Fact]
    public void A_package_whose_legs_all_have_unknown_amounts_does_not_sum_to_zero()
    {
        Row[] left = [new("L1", "PKG-1", "PAY", "T1", ""), new("L2", "PKG-1", "REC", "T1", "")];
        Row[] right = [new("R1", "", "", "T1", "")];

        var result = Run("Sum", left, right);
        var entry = Assert.Single(result.Entries, entry => entry.Alignment == Alignment.Matched);

        Assert.True(result.Projection(entry.Id, Side.Left).IsNull("NetCash"), $"package NetCash is {result.Projection(entry.Id, Side.Left).Text("NetCash")}");
        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
    }

    private static ReconciliationResult Run(string aggregator, Row[] left, Row[] right) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile.Replace("\"NetCash\": \"Sum\"", $"\"NetCash\": \"{aggregator}\"", StringComparison.Ordinal)), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", right, Map),
            ReferenceDataSet.Empty);

    public sealed record Row(string Id, string Link, string Role, string Ref, string NetCash);
}
