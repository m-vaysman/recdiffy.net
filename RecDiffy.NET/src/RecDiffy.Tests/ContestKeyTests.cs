using RecDiffy.Results;

namespace RecDiffy.Tests;

public class ContestKeyTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "contest-key",
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
        .Text("Id", row => row.Id).Text("Ref", row => row.Ref).Text("Amount", row => row.Amount);

    [Fact(Skip = "🔴 #257: an ambiguous entry's Contest attempt has no key; ResidueCluster has nowhere to keep it and ResultBuilder.Open writes null.")]
    public void An_ambiguous_entry_names_the_key_its_records_share_as_a_matched_entry_does()
    {
        var result = Run(
            [("L1", "T-1", "100"), ("L2", "T-2", "250")],
            [("R1", "T-1", "100"), ("R2", "T-2", "250"), ("R3", "T-2", "250")]);

        var matched = Assert.Single(Assert.Single(result.Entries, entry => entry.Alignment is Alignment.Matched).Explanation.Trail);
        var contest = Assert.Single(Assert.Single(result.Entries, entry => entry.Alignment is Alignment.Ambiguous).Explanation.Trail);
        Assert.Equal(("P1", PassVerdict.Committed, "ref\u001eT-1"), (matched.PassId, matched.Verdict, matched.Key));
        Assert.Equal(("P1", PassVerdict.Contest, 3), (contest.PassId, contest.Verdict, contest.Candidates.Count));
        Assert.Equal("ref\u001eT-2", contest.Key);
    }

    private static ReconciliationResult Run((string Id, string Ref, string Amount)[] left, (string Id, string Ref, string Amount)[] right) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", right, Map),
            ReferenceDataSet.Empty);
}
