using RecDiffy.Results;

namespace RecDiffy.Tests;

// Adversarial pass on main. Profile JSON is RecDiffy's own invariant format (spec §3.5 rule 6). A tolerance written as
// the string "0,05" is not an invariant number, so the profile is rejected (#168). .NET would read it as 5. The template holds
// the valid "0.05", so the profile corpus can round-trip it; the rejected variant is made in the test.
public class AdversarialToleranceTextTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "adv-tolerance-text",
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
          "paths": [ { "id": "p", "route": { "rule": "route.all" },
            "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Id"] } ],
            "diff": { "fields": { "Quantity": { "tolerance": { "absolute": "0.05" } } } } } ]
        }
        """;

    [Fact]
    public void A_tolerance_string_that_is_not_an_invariant_number_is_rejected()
    {
        var failure = Assert.Throws<ProfileCompileException>(() => ProfileDocument.Parse(Profile.Replace("\"0.05\"", "\"0,05\"", StringComparison.Ordinal)));

        Assert.Equal("InvalidParameter", Assert.Single(failure.Errors).Code);
    }

    [Fact]
    public void A_tolerance_written_as_the_invariant_string_0_05_is_0_05_and_a_four_unit_difference_breaks()
    {
        var map = new SourceMap<(string Id, string Qty)>().Text("Id", row => row.Id).Text("Qty", row => row.Qty);
        var document = ProfileDocument.Parse(Profile);
        var result = Reconciler.Run(
            Reconciler.Compile(document, RuleRegistry.CreateCore()),
            RecordSources.FromObjects<(string Id, string Qty)>("left", [("T1", "100.00")], map),
            RecordSources.FromObjects<(string Id, string Qty)>("right", [("T1", "104.00")], map),
            ReferenceDataSet.Empty);

        Assert.Equal(0.05m, document.Paths[0].Diff!.Fields["Quantity"].Tolerance.Absolute);
        Assert.Equal(FieldOutcome.Break, Assert.Single(result.Entries).Outcome);
    }
}
