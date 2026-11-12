using RecDiffy.Results;

namespace RecDiffy.Tests;

public class TrailKeyValueFormTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "trail-key-value-form",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" } ], "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ] },
            "right": { "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" } ], "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ] }
          },
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ] } ]
        }
        """;

    private static readonly SourceMap<(string Id, string Ref, string Amount)> Map = new SourceMap<(string Id, string Ref, string Amount)>()
        .Text("Id", row => row.Id)
        .Text("Ref", row => row.Ref)
        .Text("Amount", row => row.Amount);

    [Fact(Skip = "#338: the trail key holds the scheme and its separator. R2 says the value; ContestKeyTests and IdentifierKeyVsValueTests pin the key form. Unskip if the ruling is the value form, delete otherwise.")]
    public void A_trail_key_names_an_identifier_by_its_value_for_a_matched_and_a_contested_entry()
    {
        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", [("L1", "T-1", "100"), ("L2", "T-2", "250")], Map),
            RecordSources.FromObjects("right", [("R1", "T-1", "100"), ("R2", "T-2", "250"), ("R3", "T-2", "250")], Map),
            ReferenceDataSet.Empty);

        var keys = result.Entries
            .SelectMany(entry => entry.Explanation.Trail)
            .OrderBy(attempt => attempt.Verdict)
            .Select(attempt => (attempt.Verdict, attempt.Key))
            .ToArray();

        Assert.Equal([(PassVerdict.Committed, "T-1"), (PassVerdict.Contest, "T-2")], keys);
    }
}
