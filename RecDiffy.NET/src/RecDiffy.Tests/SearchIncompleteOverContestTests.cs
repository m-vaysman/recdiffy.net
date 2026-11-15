using RecDiffy.Results;

namespace RecDiffy.Tests;

// 🔴 #427. Spec v3.1 §6.2 ranks Ambiguous above SearchIncomplete: a row "in a contest recomputed on the final residue" is
// Ambiguous, and SearchIncomplete is for a row "in no residue contest". The engine gives the rows to the earliest pass
// that still claims them, so a group pass that ran out of nodes wins over a later pass that holds the same rows in a
// contest, and they are reported SearchIncomplete.
public class SearchIncompleteOverContestTests
{
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
            "passes": [
              { "id": "G", "pass": "match.groupSum", "many": "right", "block": ["Block"], "conserve": ["Quantity"] },
              { "id": "R", "pass": "match.reducedKey", "key": ["Block"] }
            ]
          } ],
          "limits": { "maxNodesPerTarget": 1 }
        }
        """;

    // One row on the left and two on the right share the block. The group search stops at its one node, and the reduced
    // key then holds one row against two: a contest on the final residue.
    [Fact(Skip = "🔴 #427. Fails: the entry is SearchIncomplete. Spec v3.1 §6.2 says Ambiguous. A spec mismatch to decide, not to fix here.")]
    public void A_row_in_a_residue_contest_is_ambiguous_even_when_an_earlier_group_search_was_cut_short()
    {
        var map = new SourceMap<(string Id, string Qty)>().Text("Id", row => row.Id).Text("Block", _ => "B").Text("Qty", row => row.Qty);

        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", [(Id: "L", Qty: "5")], map),
            RecordSources.FromObjects("right", [(Id: "R1", Qty: "2"), (Id: "R2", Qty: "3")], map),
            ReferenceDataSet.Empty);

        var entry = Assert.Single(result.Entries);
        Assert.Equal((1, 2), (entry.Left.Length, entry.Right.Length));
        Assert.Equal(Alignment.Ambiguous, entry.Alignment);
        Assert.All(result.Rows, row => Assert.Equal(Disposition.Ambiguous, row.Disposition));
    }
}
