using RecDiffy.Rules;

namespace RecDiffy.Tests;

// quantity.absolute, as the trading pack registers it today (ScaledQuantityRule): it reads a quantity as decimal.parse
// does, and falls back to an earlier canonical value of the same name, without repeating that rule's diagnostic.
public class QuantityAbsoluteInputTests
{
    private static readonly RuleBench Absolute = new RuleBench("quantity.absolute").With("from", "Qty").With("to", "Quantity");

    [Theory]
    [InlineData("", "-1,000.50", "1000.50")]
    [InlineData("de-DE", "-1.000,50", "1000.50")]
    [InlineData("sv-SE", "−5", "5")]
    [InlineData("en-US", "5-", "5")]
    public void Text_is_read_with_the_sides_culture(string culture, string raw, string expected)
    {
        var row = Absolute.In(culture).Apply(("Qty", raw));

        Assert.Equal(RawValue.Number(expected), row.Values["Quantity"].Number);
        Assert.Empty(row.Diagnostics);
    }

    [Theory]
    [InlineData("int", "-2147483648", "2147483648")]
    [InlineData("long", "-9223372036854775808", "9223372036854775808")]
    [InlineData("decimal", "-0.125", "0.12")]
    public void A_typed_number_is_read_whatever_the_culture(string kind, string raw, string expected)
    {
        var row = Absolute.In("de-DE").Apply(("Qty", RawValue.Of(kind, raw)));

        Assert.Equal(RawValue.Number(expected), row.Values["Quantity"].Number);
        Assert.Empty(row.Diagnostics);
    }

    [Theory]
    [InlineData("double", "-2.5", "FloatValue")]
    [InlineData("float", "-2.5", "FloatValue")]
    [InlineData("double", "NaN", "FloatValue")]
    [InlineData("text", "abc", "DecimalParse")]
    [InlineData("text", "1,5", "DecimalParse")]
    [InlineData("text", "1E3", "DecimalParse")]
    public void An_unreadable_quantity_is_null_and_Invalid(string kind, string raw, string code)
    {
        var row = Absolute.Apply(("Qty", RawValue.Of(kind, raw)));

        Assert.True(row.Values["Quantity"].IsNull);
        var diagnostic = Assert.Single(row.Diagnostics);
        Assert.Equal((code, DiagnosticSeverity.Invalid, "quantity.absolute", "Quantity", raw), (diagnostic.Code, diagnostic.Severity, diagnostic.RuleId, diagnostic.Field, diagnostic.RawText));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("absent")]
    public void A_blank_quantity_is_a_silent_null(string kind)
    {
        var row = kind switch
        {
            "null" => Absolute.Apply(("Qty", null)),
            "empty" => Absolute.Apply(("Qty", "")),
            _ => Absolute.Apply(("Other", "5")),
        };

        Assert.True(row.Values["Quantity"].IsNull);
        Assert.Empty(row.Diagnostics);
    }

    [Fact]
    public void An_earlier_canonical_quantity_stands_in_for_a_missing_raw_column()
    {
        var row = Absolute.Apply(Earlier(-7.125m));

        Assert.Equal(7.12m, row.Values["Quantity"].Number);
        Assert.Empty(row.Diagnostics);
    }

    // The rule that wrote the earlier value has already read the raw cell, so its diagnostic, if any, is not repeated.
    [Theory]
    [InlineData("double")]
    [InlineData("text")]
    public void An_earlier_canonical_quantity_stands_in_for_an_unreadable_raw_cell(string kind)
    {
        var raw = kind == "double" ? (object)(-2.5d) : "abc";

        var row = Absolute.Apply(Earlier(-7m) with { Raw = new Dictionary<string, object?>(StringComparer.Ordinal) { ["Qty"] = raw } });

        Assert.Equal(7m, row.Values["Quantity"].Number);
        Assert.Empty(row.Diagnostics);
    }

    [Fact]
    public void A_readable_raw_quantity_wins_over_an_earlier_canonical_one()
    {
        var row = Absolute.Apply(Earlier(-7m) with { Raw = new Dictionary<string, object?>(StringComparer.Ordinal) { ["Qty"] = "-3" } });

        Assert.Equal(3m, row.Values["Quantity"].Number);
    }

    private static RowContext Earlier(decimal quantity) =>
        RuleBench.Row().Write("Qty", CanonicalCell.FromDecimal(quantity), "decimal.parse", null, null);
}
