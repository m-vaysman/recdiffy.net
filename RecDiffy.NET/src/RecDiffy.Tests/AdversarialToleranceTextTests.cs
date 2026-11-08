using RecDiffy.Results;

namespace RecDiffy.Tests;

// Adversarial pass on main. Profile JSON is RecDiffy's own invariant format (spec §3.5 rule 6). A tolerance written as
// the string "0,05" is not an invariant number. .NET's parser reads it as 5, the "1,5 is 15" case §3.5 rule 4 rejects.
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
            "diff": { "fields": { "Quantity": { "tolerance": { "absolute": "0,05" } } } } } ]
        }
        """;

    [Fact(Skip = "🔴 #168 (#174 D): a tolerance string is not parsed strictly invariant")]
    public void A_tolerance_string_that_is_not_an_invariant_number_is_rejected()
    {
        var parsed = Record.Exception(() => ProfileDocument.Parse(Profile)) ?? Record.Exception(() => Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()));

        Assert.True(parsed is ProfileCompileException, $"\"0,05\" was accepted; tolerance read as {Absolute()}.");
    }

    [Fact(Skip = "🔴 #168 (#174 D): a tolerance string is not parsed strictly invariant")]
    public void A_four_unit_quantity_difference_is_not_within_a_tolerance_written_as_0_comma_05()
    {
        var map = new SourceMap<(string Id, string Qty)>().Text("Id", row => row.Id).Text("Qty", row => row.Qty);
        var compiled = Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore());
        var result = Reconciler.Run(
            compiled,
            RecordSources.FromObjects<(string Id, string Qty)>("left", [("T1", "100.00")], map),
            RecordSources.FromObjects<(string Id, string Qty)>("right", [("T1", "104.00")], map),
            ReferenceDataSet.Empty);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(FieldOutcome.Break, entry.Outcome);
    }

    private static decimal? Absolute() =>
        ProfileDocument.Parse(Profile).Paths[0].Diff!.Fields["Quantity"].Tolerance.Absolute;
}
