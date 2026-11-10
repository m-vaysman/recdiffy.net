using RecDiffy.Rules;

namespace RecDiffy.Tests;

// price.from32nds, as the trading pack registers it today (Scaled32ndsRule over Treasury32nds): a quote is whole points,
// a dash, exactly two digits of 32nds from 00 to 31, and an optional '+' for half a 32nd.
public class PriceFrom32ndsInputTests
{
    private static readonly RuleBench Price = new RuleBench("price.from32nds").With("from", "Px").With("to", "Price") with { Scale = 6 };

    [Theory]
    [InlineData("100-00", "100")]
    [InlineData("100-31", "100.96875")]
    [InlineData("100-31+", "100.984375")]
    [InlineData("100-00+", "100.015625")]
    [InlineData("0-01", "0.03125")]
    [InlineData("1-16", "1.5")]
    [InlineData("0099-08", "99.25")]
    [InlineData(" 99-16 ", "99.5")]
    public void A_quote_in_32nds_is_read(string raw, string expected)
    {
        var row = Price.Apply(("Px", raw));

        Assert.Equal(RawValue.Number(expected), row.Values["Price"].Number);
        Assert.Equal(raw.Trim(), Assert.Single(row.Traces).Raw);
        Assert.Empty(row.Diagnostics);
    }

    [Theory]
    [InlineData("100-32")]
    [InlineData("100-99")]
    [InlineData("100-1")]
    [InlineData("100-001")]
    [InlineData("100-1+")]
    [InlineData("100-01++")]
    [InlineData("100")]
    [InlineData("100+")]
    [InlineData("100.5")]
    [InlineData("100-")]
    [InlineData("-01")]
    [InlineData("-100-01")]
    [InlineData("+100-01")]
    [InlineData("100--1")]
    [InlineData("100-0a")]
    [InlineData("1 00-01")]
    [InlineData("99")]
    [InlineData("abc")]
    public void Anything_else_is_PriceParse(string raw)
    {
        var row = Price.Apply(("Px", raw));

        Assert.True(row.Values["Price"].IsNull);
        var diagnostic = Assert.Single(row.Diagnostics);
        Assert.Equal(("PriceParse", DiagnosticSeverity.Invalid, "price.from32nds", "Price", raw), (diagnostic.Code, diagnostic.Severity, diagnostic.RuleId, diagnostic.Field, diagnostic.RawText));
    }

    // A price already in decimal is not a quote in 32nds. decimal.parse reads it.
    [Fact]
    public void A_typed_decimal_price_is_PriceParse() =>
        Assert.Equal("PriceParse", Assert.Single(Price.Apply(("Px", 100.5m)).Diagnostics).Code);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_null_or_absent_cell_is_a_silent_null(bool absent)
    {
        var row = absent ? Price.Apply(("Other", "100-01")) : Price.Apply(("Px", null));

        Assert.True(row.Values["Price"].IsNull);
        Assert.Empty(row.Diagnostics);
    }
}
