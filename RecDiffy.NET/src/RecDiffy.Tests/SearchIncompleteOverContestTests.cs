using RecDiffy.Results;

namespace RecDiffy.Tests;

// 🔴 #427. Spec v3.1 §6.2 ranks Ambiguous above SearchIncomplete: a row "in a contest recomputed on the final residue" is
// Ambiguous, and SearchIncomplete is for a row "in no residue contest". The engine does not rank the two. Every pass
// lists its own residue, and CascadeMatcher.FoldResidue walks the passes in profile order and gives a row to the first
// pass that lists it. So a group pass that ran out of nodes wins over a later pass that holds the same rows in a
// contest, and the pass order, not the rank, decides what the rows are called.
public class SearchIncompleteOverContestTests
{
    private const string GroupPass = "{ \"id\": \"G\", \"pass\": \"match.groupSum\", \"many\": \"right\", \"block\": [\"Block\"], \"conserve\": [\"Quantity\"] }";

    private const string PairPass = "{ \"id\": \"R\", \"pass\": \"match.reducedKey\", \"key\": [\"Block\"] }";

    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "search-incomplete-over-contest",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Block", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "symbol.map", "from": "Block", "to": "Block" } ], "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ] },
            "right": { "identify": [ { "rule": "symbol.map", "from": "Block", "to": "Block" } ], "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ] }
          },
          "paths": [ {
            "id": "all",
            "route": { "rule": "route.all" },
            "passes": [ { "id": "G", "pass": "match.groupSum", "many": "right", "block": ["Block"], "conserve": ["Quantity"] }, { "id": "R", "pass": "match.reducedKey", "key": ["Block"] } ]
          } ],
          "limits": { "maxNodesPerTarget": 1 }
        }
        """;

    // One row on the left and two on the right share the block. The group search stops at its one node; with more nodes
    // it would match 2 + 3 to 5. The reduced key then holds one row against two: a contest on the final residue.
    [Fact(Skip = "🔴 #427. Fails: the entry is SearchIncomplete, because the group pass comes first in the profile. Spec v3.1 §6.2 says Ambiguous. A spec mismatch to decide, not to fix here.")]
    public void A_row_in_a_residue_contest_is_ambiguous_even_when_an_earlier_group_search_was_cut_short()
    {
        var entry = Assert.Single(Run(Profile).Entries);

        Assert.Equal((1, 2), (entry.Left.Length, entry.Right.Length));
        Assert.Equal(Alignment.Ambiguous, entry.Alignment);
    }

    // What the engine does today, and why: the same rows, with only the order of the two passes changed.
    [Fact]
    public void The_first_pass_in_the_profile_that_lists_the_rows_names_them()
    {
        var pairFirst = Profile.Replace($"{GroupPass}, {PairPass}", $"{PairPass}, {GroupPass}", StringComparison.Ordinal);
        Assert.False(string.Equals(Profile, pairFirst, StringComparison.Ordinal));

        var groupThenPair = Assert.Single(Run(Profile).Entries);
        var pairThenGroup = Assert.Single(Run(pairFirst).Entries);

        Assert.Equal((Alignment.SearchIncomplete, "G", PassVerdict.SearchIncomplete), (groupThenPair.Alignment, groupThenPair.Explanation.CommittedBy, groupThenPair.Explanation.Trail[0].Verdict));
        Assert.Equal((Alignment.Ambiguous, "R", PassVerdict.Contest), (pairThenGroup.Alignment, pairThenGroup.Explanation.CommittedBy, pairThenGroup.Explanation.Trail[0].Verdict));
        Assert.Equal(groupThenPair.Id, pairThenGroup.Id);
    }

    private static IReconciliationResult Run(string profile)
    {
        var map = new SourceMap<(string Id, string Qty)>().Text("Id", row => row.Id).Text("Block", _ => "B").Text("Qty", row => row.Qty);
        return Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", [(Id: "L", Qty: "5")], map),
            RecordSources.FromObjects("right", [(Id: "R1", Qty: "2"), (Id: "R2", Qty: "3")], map),
            ReferenceDataSet.Empty);
    }
}
