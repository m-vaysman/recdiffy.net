using RecDiffy.Results;

namespace RecDiffy.Tests;

// #426. What a group pass leaves open is listed by block key in ordinal order, whatever the kind of entry and whatever
// order the rows were fed in. GroupResidueOrderTests covers contested blocks alone; here contested and oversized blocks
// alternate, and the same rows are fed in three orders. Spec v3.1 §10.1.
public class GroupResidueKeyOrderTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "group-residue-key-order",
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
          "limits": { "maxBlockItems": 3 },
          "paths": [ {
            "id": "all",
            "route": { "rule": "route.all" },
            "passes": [ { "id": "G", "pass": "match.groupSum", "many": "left", "block": ["Block"], "conserve": ["Quantity"] } ]
          } ]
        }
        """;

    private static readonly string[] Blocks = ["B01", "B02", "B03", "B04", "B05", "B06", "B07", "B08", "B09", "B10"];

    private static readonly (string Id, string Block, string Qty)[] Left =
        [.. Blocks.SelectMany((block, at) => Enumerable.Range(1, Oversized(at) ? 4 : 3).Select(copy => (Id: $"L-{copy.ToInvariant()}-{block}", Block: block, Qty: "1")))];

    private static readonly (string Id, string Block, string Qty)[] Right =
        [.. Blocks.Select((block, at) => (Id: $"R-{block}", Block: block, Qty: Oversized(at) ? "3" : "2"))];

    public static TheoryData<string> Orders => ["as written", "reversed", "shuffled"];

    // Every second block holds four rows against a limit of three, so it is not searched and is SearchIncomplete. The
    // others hold three equal rows for a target of two, so they are contested and Ambiguous.
    [Theory]
    [MemberData(nameof(Orders))]
    public void Open_blocks_are_listed_by_block_key_for_rows_in_any_order(string order)
    {
        var map = new SourceMap<(string Id, string Block, string Qty)>().Text("Id", row => row.Id).Text("Block", row => row.Block).Text("Qty", row => row.Qty);

        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", In(order, Left, 17), map),
            RecordSources.FromObjects("right", In(order, Right, 23), map),
            ReferenceDataSet.Empty);

        Assert.Equal(Blocks, result.Entries.Select(entry => entry.Right[0].RowKey[2..]), StringComparer.Ordinal);
        Assert.Equal(
            Blocks.Select((_, at) => Oversized(at) ? Alignment.SearchIncomplete : Alignment.Ambiguous),
            result.Entries.Select(entry => entry.Alignment));
    }

    private static bool Oversized(int at) => at % 2 is 1;

    private static T[] In<T>(string order, T[] rows, int seed) => order switch
    {
        "reversed" => [.. rows.Reverse()],
        "shuffled" => Shuffled(rows, seed),
        _ => rows,
    };

    // A fixed seed, so the shuffled order is the same in every run.
    private static T[] Shuffled<T>(T[] rows, int seed)
    {
        var random = new Random(seed);
        return [.. rows.Select(row => (Row: row, At: random.Next())).OrderBy(pair => pair.At).Select(pair => pair.Row)];
    }
}
