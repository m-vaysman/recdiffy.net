using RecDiffy.Results;

namespace RecDiffy.Tests;

// #40, for #16. Real collision pairs: two different key tuples whose parts join to the same text when a part is not
// escaped. The plain pair joins alike when nothing is escaped. The backslash pair joins alike when the separator is
// escaped but backslash is not. Each pair runs through a compiled profile, so the keys are the ones the engine builds.
public class CompositeKeyCollisionTests
{
    private const string Unit = "\u001f";
    private const string Record = "\u001e";

    private const string PairProfile = """
        {
          "formatVersion": 1,
          "profileId": "collision-pairs",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Desk", "type": "Symbol", "role": "Key" },
            { "name": "Book", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "symbol.map", "from": "Desk", "to": "Desk" }, { "rule": "symbol.map", "from": "Book", "to": "Book" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            },
            "right": {
              "identify": [ { "rule": "symbol.map", "from": "Desk", "to": "Desk" }, { "rule": "symbol.map", "from": "Book", "to": "Book" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            }
          },
          "paths": [ { "id": "pairs", "route": { "rule": "route.all" }, "passes": [ { "id": "P2", "pass": "match.naturalKey", "key": ["Desk", "Book"] } ] } ]
        }
        """;

    // The book is an identifier, so its key part is the scheme, a record separator, then the value.
    private const string IdentifierProfile = """
        {
          "formatVersion": 1,
          "profileId": "collision-identifier",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Desk", "type": "Symbol", "role": "Key" },
            { "name": "Book", "type": "Identifier", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "symbol.map", "from": "Desk", "to": "Desk" }, { "rule": "identifier.map", "from": "Book", "to": "Book", "scheme": "book" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            },
            "right": {
              "identify": [ { "rule": "symbol.map", "from": "Desk", "to": "Desk" }, { "rule": "identifier.map", "from": "Book", "to": "Book", "scheme": "book" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            }
          },
          "paths": [ { "id": "pairs", "route": { "rule": "route.all" }, "passes": [ { "id": "P2", "pass": "match.naturalKey", "key": ["Desk", "Book"] } ] } ]
        }
        """;

    // The group pass blocks on the same two fields, so a block-key collision would pull a row into another row's block.
    private const string GroupProfile = """
        {
          "formatVersion": 1,
          "profileId": "collision-blocks",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Desk", "type": "Symbol", "role": "Key" },
            { "name": "Book", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "symbol.map", "from": "Desk", "to": "Desk" }, { "rule": "symbol.map", "from": "Book", "to": "Book" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            },
            "right": {
              "identify": [ { "rule": "symbol.map", "from": "Desk", "to": "Desk" }, { "rule": "symbol.map", "from": "Book", "to": "Book" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            }
          },
          "paths": [ { "id": "blocks", "route": { "rule": "route.all" }, "passes": [
            { "id": "G", "pass": "match.groupSum", "many": "left", "block": ["Desk", "Book"], "conserve": ["Quantity"] },
            { "id": "P2", "pass": "match.naturalKey", "key": ["Desk", "Book"] }
          ] } ]
        }
        """;

    private static readonly SourceMap<Row> Map = new SourceMap<Row>()
        .Text("Id", row => row.Id)
        .Text("Desk", row => row.Desk)
        .Text("Book", row => row.Book)
        .Text("Quantity", row => row.Quantity);

    // L1 and R1 are the colliding pair. R3 carries L1's own tuple, so L1 has one true partner and the twin must not
    // become its rival. L2 and R2 are a plain control pair. The third pair puts the separator at the edge of a part.
    [Theory]
    [InlineData("A" + Unit + "B", "C", "A", "B" + Unit + "C")]
    [InlineData("X\\", "Y" + Unit + "Z", "X" + Unit + "Y\\", "Z")]
    [InlineData("A" + Unit, "B", "A", Unit + "B")]
    public void A_key_tuple_that_only_joins_like_another_does_not_match_it(string leftDesk, string leftBook, string rightDesk, string rightBook)
    {
        var result = Run(
            PairProfile,
            [new("L1", leftDesk, leftBook, "5"), new("L2", "X", "Y", "1")],
            [new("R1", rightDesk, rightBook, "5"), new("R2", "X", "Y", "1"), new("R3", leftDesk, leftBook, "5")]);

        Assert.Equal(["left:L1|right:R3", "left:L2|right:R2"], MatchedPairs(result));
        Assert.Equal(Disposition.RightOnly, DispositionOf(result, "right", "R1"));
    }

    // Under a plain join, "A" + US + "book" + RS + "B" and "C" against "A" and "B" + US + "book" + RS + "C" both read
    // A, US, book, RS, B, US, book, RS, C.
    [Fact]
    public void An_identifier_part_that_only_joins_like_another_does_not_match_it()
    {
        var leftDesk = "A" + Unit + "book" + Record + "B";
        var rightBook = "B" + Unit + "book" + Record + "C";
        var result = Run(
            IdentifierProfile,
            [new("L1", leftDesk, "C", "5")],
            [new("R1", "A", rightBook, "5"), new("R3", leftDesk, "C", "5")]);

        Assert.Equal(["left:L1|right:R3"], MatchedPairs(result));
        Assert.Equal(Disposition.RightOnly, DispositionOf(result, "right", "R1"));
    }

    // 4 + 6 = 10 only if L1 shares R1's block. It does not, so no group commits; L2 then pairs one to one and breaks.
    [Fact]
    public void A_block_key_that_only_joins_like_another_does_not_pull_a_row_into_that_block()
    {
        var result = Run(
            GroupProfile,
            [new("L1", "A" + Unit + "B", "C", "4"), new("L2", "A", "B" + Unit + "C", "6")],
            [new("R1", "A", "B" + Unit + "C", "10")]);

        Assert.DoesNotContain(result.Entries, entry => entry.Composition.HasFlag(Composition.ManyToOne));
        Assert.Equal(["left:L2|right:R1"], MatchedPairs(result));
        Assert.Equal(Disposition.LeftOnly, DispositionOf(result, "left", "L1"));
    }

    // Two copies of row D share a row key. Their fingerprints join Book, Desk, Id and Quantity as name=value parts; under
    // a plain join both copies read "Book=x", US, "Desk=y", US, "Desk=z", so different copies would collapse as one.
    [Fact]
    public void Two_copies_whose_cells_only_join_alike_are_a_conflict_not_one_row()
    {
        var result = Run(
            PairProfile,
            [new("D", "z", "x" + Unit + "Desk=y", "5"), new("D", "y" + Unit + "Desk=z", "x", "5")],
            []);

        Assert.Equal(2, result.Rows.Count);
        Assert.All(result.Rows, row =>
        {
            Assert.Equal(Disposition.Quarantined, row.Disposition);
            Assert.Contains(row.Diagnostics, diagnostic => diagnostic.Code == "DuplicateConflict");
        });
    }

    private static ReconciliationResult Run(string profile, Row[] left, Row[] right) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", right, Map),
            ReferenceDataSet.Empty);

    private static string[] MatchedPairs(ReconciliationResult result) =>
        [.. result.Entries
            .Where(entry => entry.Alignment == Alignment.Matched)
            .Select(entry => string.Join("|", entry.Left.Concat(entry.Right).Select(member => $"{member.Source}:{member.RowKey}").Order(StringComparer.Ordinal)))
            .Order(StringComparer.Ordinal)];

    private static Disposition DispositionOf(ReconciliationResult result, string source, string rowKey) =>
        result.Rows.Single(row => row.Row.Source == source && row.Row.RowKey == rowKey).Disposition;

    private sealed record Row(string Id, string Desk, string Book, string Quantity);
}
