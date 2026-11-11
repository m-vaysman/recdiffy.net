using RecDiffy.Results;

namespace RecDiffy.Tests;

// #40: input-order invariance for the adversarial cases. Spec §10.1: the same rows, in any order, with the same profile,
// give the same membership, outcomes, deltas and diagnostic codes. One population holds a key collision pair, a shared
// reference on two rows, an ambiguous key, a group, an invalid twin, a rounding midpoint, blank references, conflicting
// copies of one row key and identical copies of another. Every rotation of each side, each reversed, and both sides
// shuffled together must give the baseline's result.
public class AdversarialRowOrderTests
{
    private const string Unit = "\u001f";

    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "adversarial-row-order",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Instrument", "type": "Symbol", "role": "Key" },
            { "name": "TradeRef", "type": "Identifier", "role": "Identity" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Price", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" },
                { "rule": "identifier.map", "from": "TradeRef", "to": "TradeRef", "scheme": "shared" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" }, { "rule": "decimal.parse", "from": "Price", "to": "Price" } ]
            },
            "right": {
              "identify": [
                { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" },
                { "rule": "identifier.map", "from": "TradeRef", "to": "TradeRef", "scheme": "shared" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" }, { "rule": "decimal.parse", "from": "Price", "to": "Price" } ]
            }
          },
          "paths": [ {
            "id": "trades",
            "route": { "rule": "route.all" },
            "passes": [
              { "id": "P1", "pass": "match.sharedRef" },
              { "id": "P2", "pass": "match.naturalKey", "key": ["Counterparty", "Instrument", "Quantity"] },
              { "id": "G", "pass": "match.groupSum", "many": "left", "block": ["Counterparty", "Instrument"], "conserve": ["Quantity"] },
              { "id": "P4", "pass": "match.reducedKey", "key": ["Counterparty", "Instrument"] }
            ],
            "diff": { "fields": { "Price": { "tolerance": { "absolute": "0.01" } } } }
          } ]
        }
        """;

    private static readonly Trade[] Left =
    [
        new("L-cx", "A" + Unit + "B", "C", null, "1", "1"),
        new("L-d1", "ACME", "BOND1", "U-1", "100", "10"),
        new("L-d2", "ACME", "BOND2", "U-1", "200", "10"),
        new("L-a1", "ZETA", "Z1", null, "5", "1"),
        new("L-a2", "ZETA", "Z1", null, "5", "1"),
        new("L-g1", "GRP", "G1", null, "4", "2"),
        new("L-g2", "GRP", "G1", null, "6", "2"),
        new("L-i1", "INV", "I1", null, "7", "3"),
        new("L-i2", "INV", "I1", null, "x7", "3"),
        new("L-r", "RND", "R1", null, "10.005", "1.005"),
        new("L-b", "BLK", "B1", "  ", "1", "1"),
        new("D", "DUP", "D1", null, "1", "1"),
        new("D", "DUP", "D1", null, "2", "1"),
        new("E", "EQ", "E1", null, "3", "1"),
        new("E", "EQ", "E1", null, "3", "1"),
    ];

    private static readonly Trade[] Right =
    [
        new("R-cx", "A", "B" + Unit + "C", null, "1", "1"),
        new("R-d", "ACME", "BOND2", "U-1", "200", "10"),
        new("R-a", "ZETA", "Z1", null, "5", "1"),
        new("R-g", "GRP", "G1", null, "10", "2"),
        new("R-i", "INV", "I1", null, "7", "3"),
        new("R-r", "RND", "R1", null, "10.00", "1.015"),
        new("R-b", "BLK", "B2", "", "9", "1"),
        new("R-D", "DUP", "D1", null, "1", "1"),
        new("R-E", "EQ", "E1", null, "3", "1"),
    ];

    private static readonly SourceMap<Trade> Map = new SourceMap<Trade>()
        .Text("Id", trade => trade.Id)
        .Text("Counterparty", trade => trade.Counterparty)
        .Text("Instrument", trade => trade.Instrument)
        .Text("TradeRef", trade => trade.TradeRef)
        .Text("Quantity", trade => trade.Quantity)
        .Text("Price", trade => trade.Price);

    private static readonly Lazy<CompiledProfile> Compiled = new(() => Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()));

    // The baseline is what each case is for, so a population that stops exercising a case fails here, not silently.
    [Fact]
    public void The_population_reaches_every_adversarial_case()
    {
        var rows = Rows(Run(Left, Right));

        Assert.Equal(
            [
                "left:D=Quarantined/Invalid/DuplicateConflict",
                "left:D=Quarantined/Invalid/DuplicateConflict",
                "left:E=Matched/ValidWithWarnings/DuplicateRowCollapsed",
                "left:E=Matched/ValidWithWarnings/DuplicateRowCollapsed",
                "left:L-a1=Ambiguous/Valid/",
                "left:L-a2=Ambiguous/Valid/",
                "left:L-b=LeftOnly/Valid/",
                "left:L-cx=LeftOnly/Valid/",
                "left:L-d1=LeftOnly/Valid/",
                "left:L-d2=Matched/Valid/",
                "left:L-g1=Matched/Valid/",
                "left:L-g2=Matched/Valid/",
                "left:L-i1=Matched/Valid/",
                "left:L-i2=Quarantined/Invalid/DecimalParse",
                "left:L-r=Matched/Valid/",
                "right:R-D=RightOnly/Valid/",
                "right:R-E=Matched/Valid/",
                "right:R-a=Ambiguous/Valid/",
                "right:R-b=RightOnly/Valid/",
                "right:R-cx=RightOnly/Valid/",
                "right:R-d=Matched/Valid/",
                "right:R-g=Matched/Valid/",
                "right:R-i=Matched/Valid/",
                "right:R-r=Matched/Valid/",
            ],
            rows);
    }

    [Fact]
    public void Every_rotation_reversal_and_shuffle_gives_the_same_result()
    {
        var baseline = Snapshot(Run(Left, Right));
        var orders = Rotations(Left).Select((left, index) => ($"left rotation {index}", left, Right))
            .Concat(Rotations(Right).Select((right, index) => ($"right rotation {index}", Left, right)))
            .Concat(Enumerable.Range(1, 20).Select(seed => ($"both shuffled, seed {seed}", Shuffle(Left, seed), Shuffle(Right, seed + 100))));

        var differing = orders
            .Select(order => (order.Item1, Snapshot: Snapshot(Run(order.Item2, order.Item3))))
            .Where(run => run.Snapshot != baseline)
            .Select(run => $"{run.Item1}:{Environment.NewLine}{run.Snapshot}")
            .ToArray();

        Assert.True(differing.Length == 0, $"baseline:{Environment.NewLine}{baseline}{Environment.NewLine}{string.Join(Environment.NewLine, differing)}");
    }

    private static ReconciliationResult Run(IReadOnlyList<Trade> left, IReadOnlyList<Trade> right) =>
        Reconciler.Run(Compiled.Value, RecordSources.FromObjects("left", left, Map), RecordSources.FromObjects("right", right, Map), ReferenceDataSet.Empty);

    // Row keys, not ordinals: an ordinal is the row's place in its file, which is what the test moves.
    private static string Snapshot(ReconciliationResult result) =>
        string.Join(Environment.NewLine, [.. Rows(result), .. Entries(result), result.Summary.ToString()]);

    private static string[] Rows(ReconciliationResult result) =>
        [.. result.Rows
            .Select(row => $"{row.Row.Source}:{row.Row.RowKey}={row.Disposition}/{row.Validity}/{string.Join("+", row.Diagnostics.Select(diagnostic => diagnostic.Code).Order(StringComparer.Ordinal))}")
            .Order(StringComparer.Ordinal)];

    private static IEnumerable<string> Entries(ReconciliationResult result) =>
        result.Entries
            .Select(entry => $"{entry.Alignment} {entry.Composition} {entry.Outcome} {entry.PathId} {entry.Explanation.CommittedBy} {Members(entry.Left)} | {Members(entry.Right)} {Diffs(entry)}")
            .Order(StringComparer.Ordinal);

    private static string Members(IReadOnlyList<SourceRef> members) =>
        string.Join(",", members.Select(member => $"{member.Source}:{member.RowKey}").Order(StringComparer.Ordinal));

    private static string Diffs(ResultEntry entry) =>
        string.Join(",", entry.Diffs.Select(diff => $"{diff.Field.Name}:{diff.Outcome}:{diff.Delta.Number}").Order(StringComparer.Ordinal));

    private static IEnumerable<Trade[]> Rotations(Trade[] rows) =>
        Enumerable.Range(0, rows.Length).SelectMany(shift => new[] { Rotate(rows, shift), [.. Rotate(rows, shift).Reverse()] });

    private static Trade[] Rotate(Trade[] rows, int shift) => [.. rows[shift..], .. rows[..shift]];

    private static Trade[] Shuffle(Trade[] rows, int seed)
    {
        var random = new Random(seed);
        return [.. rows.Select(row => (Row: row, Order: random.Next())).OrderBy(pair => pair.Order).Select(pair => pair.Row)];
    }

    private sealed record Trade(string Id, string Counterparty, string Instrument, string? TradeRef, string Quantity, string Price);
}
