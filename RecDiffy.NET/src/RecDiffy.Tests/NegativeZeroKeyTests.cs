using System.Globalization;
using RecDiffy.Results;

namespace RecDiffy.Tests;

// #116 "untested but holds" item 5: a decimal zero with its sign bit set, at any scale, is the same key as zero.
// Amount is in the natural key. Fee is compared after the match.
public class NegativeZeroKeyTests
{
    [Theory]
    [InlineData(true, 4, false, 0)]
    [InlineData(true, 0, false, 2)]
    [InlineData(false, 2, true, 6)]
    [InlineData(true, 2, true, 0)]
    public void A_negative_zero_amount_matches_zero_on_the_natural_key(bool leftNegative, byte leftScale, bool rightNegative, byte rightScale)
    {
        var left = new decimal(0, 0, 0, leftNegative, leftScale);
        var right = new decimal(0, 0, 0, rightNegative, rightScale);

        var result = Run(Numbers("left", left, left), Numbers("right", right, right));

        AssertMatchedWithExactFee(result);
    }

    [Theory]
    [InlineData("-0.0000", "0")]
    [InlineData("-0", "0.00")]
    [InlineData("0", "-0.00")]
    [InlineData("-0.001", "0")]
    public void A_negative_zero_read_from_text_matches_zero_on_the_natural_key(string left, string right)
    {
        var result = Run(Texts("left", left, left), Texts("right", right, right));

        AssertMatchedWithExactFee(result);
    }

    // The control: Fee is compared, so a fee that is not zero breaks on the same negative-zero key.
    [Fact]
    public void A_fee_that_is_not_zero_still_breaks_on_a_negative_zero_key()
    {
        var result = Run(Texts("left", "-0.00", "0.01"), Texts("right", "0", "-0"));

        var entry = Assert.Single(result.Entries);
        Assert.Equal(Alignment.Matched, entry.Alignment);
        Assert.Equal(FieldOutcome.Break, Assert.Single(entry.Diffs, diff => diff.Field.Name == "Fee").Outcome);
    }

    // An Exact field is left out of Diffs, so an Exact entry has none.
    private static void AssertMatchedWithExactFee(ReconciliationResult result)
    {
        var entry = Assert.Single(result.Entries);
        Assert.Equal(Alignment.Matched, entry.Alignment);
        Assert.Equal("P1", entry.Explanation.CommittedBy);
        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
        Assert.Empty(entry.Diffs);
        Assert.All(result.Rows, row => Assert.Empty(row.Diagnostics));
    }

    private static ReconciliationResult Run(RecordSource left, RecordSource right) =>
        Reconciler.Run(CultureProfile.Compile(Profile, CultureInfo.InvariantCulture), left, right, ReferenceDataSet.Empty);

    private static RecordSource Numbers(string side, decimal amount, decimal fee) =>
        RecordSources.FromObjects(
            side,
            [(Id: "T1", Amount: amount, Fee: fee)],
            new SourceMap<(string Id, decimal Amount, decimal Fee)>()
                .Text("Id", row => row.Id)
                .Text("Book", _ => "B1")
                .Decimal("Amount", row => row.Amount)
                .Decimal("Fee", row => row.Fee));

    private static RecordSource Texts(string side, string amount, string fee) =>
        RecordSources.FromObjects(
            side,
            [(Id: "T1", Amount: amount, Fee: fee)],
            new SourceMap<(string Id, string Amount, string Fee)>()
                .Text("Id", row => row.Id)
                .Text("Book", _ => "B1")
                .Text("Amount", row => row.Amount)
                .Text("Fee", row => row.Fee));

    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "negative-zero-key",
          "version": "1.0.0",
          "culture": "en-US",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Id", "type": "Identifier", "role": "Key" },
            { "name": "Book", "type": "Symbol", "role": "Key" },
            { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Key" },
            { "name": "Fee", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "Id" }, { "rule": "symbol.map", "from": "Book", "to": "Book" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" }, { "rule": "decimal.parse", "from": "Fee", "to": "Fee" } ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "Id" }, { "rule": "symbol.map", "from": "Book", "to": "Book" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" }, { "rule": "decimal.parse", "from": "Fee", "to": "Fee" } ]
            }
          },
          "paths": [ { "id": "all", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": [ "Book", "Amount" ] } ] } ]
        }
        """;
}
