using RecDiffy.Results;

namespace RecDiffy.Tests;

// #40: invalid values, through a compiled profile. A value that cannot be read makes its row Invalid and quarantined.
// The row takes no part in matching: it does not match, it is not a rival that makes a key ambiguous, and its value is
// not moved to a nearby one that would match.
public class InvalidValueTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "invalid-values",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Sku", "type": "Symbol", "role": "Key" },
            { "name": "TradeDate", "type": "Date", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" }, { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] } ]
            },
            "right": {
              "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" }, { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] } ]
            }
          },
          "paths": [ { "id": "pairs", "route": { "rule": "route.all" }, "passes": [ { "id": "P2", "pass": "match.naturalKey", "key": ["Sku", "TradeDate"] } ] } ]
        }
        """;

    private const string MaxValue = "79228162514264337593543950335";

    private static readonly SourceMap<Row> Map = new SourceMap<Row>()
        .Text("Id", row => row.Id)
        .Text("Sku", row => row.Sku)
        .Text("TradeDate", row => row.TradeDate)
        .Text("Quantity", row => row.Quantity);

    // The twin shares the valid row's key. Were it in the match, the key would hold two rows on one side and be ambiguous.
    // "1,5" puts a group separator where the invariant culture has none; the last value is one past decimal.MaxValue.
    [Theory]
    [InlineData("left", "abc")]
    [InlineData("left", "1,5")]
    [InlineData("left", "79228162514264337593543950336")]
    [InlineData("right", "abc")]
    [InlineData("right", "79228162514264337593543950336")]
    public void An_invalid_twin_is_quarantined_and_does_not_make_the_valid_pair_ambiguous(string side, string quantity)
    {
        Row[] pair = [new("V", "S1", "2025-06-11", "100")];
        Row[] twin = [.. pair, new("X", "S1", "2025-06-11", quantity)];
        var result = side == "left" ? Run(twin, [new("P", "S1", "2025-06-11", "100")]) : Run([new("P", "S1", "2025-06-11", "100")], twin);

        var entry = Assert.Single(result.Entries, entry => entry.Alignment == Alignment.Matched);
        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
        var invalid = result.Rows.Single(row => row.Row.RowKey == "X");
        Assert.Equal((Disposition.Quarantined, InputValidity.Invalid), (invalid.Disposition, invalid.Validity));
        Assert.Contains(invalid.Diagnostics, diagnostic => diagnostic.Code == "DecimalParse");
        Assert.DoesNotContain(result.Rows, row => row.Disposition == Disposition.Ambiguous);
    }

    // 29 February 2025 does not exist. It is not read as 28 February or 1 March, so neither right row has a partner.
    [Fact]
    public void A_date_that_does_not_exist_is_not_moved_to_a_neighbouring_day()
    {
        var result = Run(
            [new("L", "S1", "2025-02-29", "100")],
            [new("R28", "S1", "2025-02-28", "100"), new("R01", "S1", "2025-03-01", "100")]);

        Assert.Equal(
            ["left:L=Quarantined[DateParse]", "right:R01=RightOnly[]", "right:R28=RightOnly[]"],
            result.Rows.Select(row => $"{row.Row.Source}:{row.Row.RowKey}={row.Disposition}[{string.Join(",", row.Diagnostics.Select(diagnostic => diagnostic.Code))}]").Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    // decimal.MaxValue itself is a number. Written with invariant group separators on one side, it is the same number.
    [Fact]
    public void The_largest_decimal_is_read_and_compares_exactly()
    {
        var result = Run([new("L", "S1", "2025-06-11", MaxValue)], [new("R", "S1", "2025-06-11", "79,228,162,514,264,337,593,543,950,335")]);

        var entry = Assert.Single(result.Entries);
        Assert.Equal((Alignment.Matched, FieldOutcome.Exact), (entry.Alignment, entry.Outcome));
        Assert.All(result.Rows, row => Assert.Equal(InputValidity.Valid, row.Validity));
    }

    private static IReconciliationResult Run(Row[] left, Row[] right) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", right, Map),
            ReferenceDataSet.Empty);

    private sealed record Row(string Id, string Sku, string TradeDate, string Quantity);
}
