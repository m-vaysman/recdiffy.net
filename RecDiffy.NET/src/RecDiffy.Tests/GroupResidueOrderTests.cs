using RecDiffy.Results;

namespace RecDiffy.Tests;

// #426. The entries a group pass leaves ambiguous or search-incomplete are listed by block key, in ordinal order, as
// PairIndex.Residue lists its keys. GroupIndex.Residue once listed its Decisions dictionary as it came: the dictionary
// is keyed by the block's key text and text hashes are seeded per process, so the same rows gave Entries in a different
// order in every run. Spec v3.1 §10.1: "No decision reads RowKey, row order, ordinals, string.GetHashCode, dictionary
// order or the clock."
public class GroupResidueOrderTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "group-residue-order",
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
            "passes": [ { "id": "G", "pass": "match.groupSum", "many": "left", "block": ["Block"], "conserve": ["Quantity"] } ]
          } ]
        }
        """;

    private static readonly string[] Blocks = ["B01", "B02", "B03", "B04", "B05", "B06", "B07", "B08", "B09", "B10", "B11", "B12"];

    // Twelve blocks, each with three equal rows on the left and a target of two on the right: any two of the three would
    // do, so each block is one contested, ambiguous entry. Listed in key order, as a pair pass lists its contests, the
    // entries read B01 to B12. Hash order would give B01 to B12 in one process in 479,001,600 (12 factorial).
    [Fact]
    public void The_contested_blocks_of_a_group_pass_are_listed_in_the_same_order_in_every_process()
    {
        var map = new SourceMap<(string Id, string Block, string Qty)>().Text("Id", row => row.Id).Text("Block", row => row.Block).Text("Qty", row => row.Qty);
        var left = Blocks.SelectMany(block => new[] { 1, 2, 3 }.Select(copy => (Id: $"L-{block}-{copy.ToInvariant()}", Block: block, Qty: "1")));
        var right = Blocks.Select(block => (Id: $"R-{block}", Block: block, Qty: "2"));

        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, map),
            RecordSources.FromObjects("right", right, map),
            ReferenceDataSet.Empty);

        Assert.All(result.Entries, entry => Assert.Equal((Alignment.Ambiguous, 3, 1), (entry.Alignment, entry.Left.Length, entry.Right.Length)));
        Assert.Equal(Blocks, result.Entries.Select(entry => entry.Right[0].RowKey[2..]), StringComparer.Ordinal);
    }
}
