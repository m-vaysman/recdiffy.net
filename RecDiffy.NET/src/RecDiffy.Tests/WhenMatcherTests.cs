using RecDiffy.Rules;

namespace RecDiffy.Tests;

// WhenMatcher decides whether a rule, a tolerance or a comparison applies to a row. It reads a field's canonical display,
// or the trimmed raw column when the field has no value, and compares ordinally.
public class WhenMatcherTests
{
    private static readonly RowContext Buy = RuleBench.Row(("Dir", " BUY "), ("Desk", "rates"));

    [Fact]
    public void No_clause_holds() => Assert.True(WhenMatcher.Holds(null, Buy));

    [Fact]
    public void A_clause_with_no_test_holds_even_for_a_missing_field() =>
        Assert.True(WhenMatcher.Holds(new WhenClause("Nothing", null, null, null), Buy));

    [Theory]
    [InlineData("BUY", true)]
    [InlineData("buy", false)]
    [InlineData(" BUY ", false)]
    [InlineData("SELL", false)]
    public void Equals_compares_the_trimmed_text_ordinally(string expected, bool holds) =>
        Assert.Equal(holds, WhenMatcher.Holds(new WhenClause("Dir", expected, null, null), Buy));

    [Theory]
    [InlineData("BUY", false)]
    [InlineData("buy", true)]
    [InlineData("SELL", true)]
    public void Not_equals_holds_for_any_other_text(string excluded, bool holds) =>
        Assert.Equal(holds, WhenMatcher.Holds(new WhenClause("Dir", null, excluded, null), Buy));

    [Theory]
    [InlineData("SELL|BUY", true)]
    [InlineData("BUY", true)]
    [InlineData("buy|Buy", false)]
    [InlineData("SELL", false)]
    public void In_holds_when_the_list_names_the_text(string values, bool holds) =>
        Assert.Equal(holds, WhenMatcher.Holds(new WhenClause("Dir", null, null, values.Split('|')), Buy));

    [Fact]
    public void A_missing_field_fails_equals_and_in_and_passes_not_equals()
    {
        Assert.False(WhenMatcher.Holds(new WhenClause("Region", "EU", null, null), Buy));
        Assert.False(WhenMatcher.Holds(new WhenClause("Region", null, null, ["EU"]), Buy));
        Assert.True(WhenMatcher.Holds(new WhenClause("Region", null, "EU", null), Buy));
    }

    [Fact]
    public void A_canonical_value_wins_over_the_raw_column_of_the_same_name()
    {
        var row = new RuleBench("symbol.map").With("from", "Desk").With("to", "Dir").WithMap("map", ("rates", "SELL")).Apply(Buy);

        Assert.Equal("SELL", WhenMatcher.Text(row, "Dir"));
        Assert.True(WhenMatcher.Holds(new WhenClause("Dir", "SELL", null, null), row));
    }

    [Fact]
    public void A_canonical_null_falls_back_to_the_raw_column()
    {
        var row = Buy.Write("Dir", CanonicalCell.Null(FieldType.Symbol), "test", null, null);

        Assert.Equal("BUY", WhenMatcher.Text(row, "Dir"));
    }

    [Fact]
    public void A_canonical_number_and_date_read_as_invariant_text()
    {
        var row = Buy
            .Write("Quantity", CanonicalCell.FromDecimal(-1234.5m), "test", null, null)
            .Write("Day", CanonicalCell.FromDate(new DateOnly(2025, 6, 11)), "test", null, null);

        Assert.True(WhenMatcher.Holds(new WhenClause("Quantity", "-1234.5", null, null), row));
        Assert.True(WhenMatcher.Holds(new WhenClause("Day", "2025-06-11", null, null), row));
    }
}
