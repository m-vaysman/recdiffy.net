using RecDiffy.Results;
using RecDiffy.Rules;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

public class CultureQuantityRuleTests
{
    [Theory]
    [InlineData("", "int", "-100")]
    [InlineData("", "long", "-9223372036854775808")]
    [InlineData("", "decimal", "-100.25")]
    [InlineData("", "double", "-2.5")]
    [InlineData("", "float", "-2.5")]
    [InlineData("", "double", "NaN")]
    [InlineData("", "text", "-1,000.50")]
    [InlineData("", "text", "1,5")]
    [InlineData("", "text", "1234,5")]
    [InlineData("", "text", "")]
    [InlineData("", "null", "")]
    [InlineData("", "text", "abc")]
    [InlineData("", "bool", "true")]
    [InlineData("", "date", "2026-01-02")]
    [InlineData("de-DE", "text", "-1.000,50")]
    [InlineData("de-DE", "text", "1,5")]
    [InlineData("de-DE", "text", "1.5")]
    [InlineData("sv-SE", "text", "−1 234,5")]
    public void Every_raw_value_is_read_the_same_way_as_decimal_parse_reads_it(string culture, string kind, string raw)
    {
        var value = RawValue.Of(kind, raw);
        var parsed = Row(value);
        CultureProfile.Rules(culture).Build("decimal.parse", Columns("Parsed")).Apply(ref parsed);
        var absolute = Row(value);
        Absolute(culture).Apply(ref absolute);

        Assert.Equal(parsed.Values["Parsed"].IsNull, absolute.Values["Quantity"].IsNull);
        Assert.Equal(decimal.Abs(parsed.Values["Parsed"].Number), absolute.Values["Quantity"].Number);
        Assert.Equal(
            parsed.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Severity)),
            absolute.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Severity)));
    }

    [Fact]
    public void A_float_raw_quantity_uses_an_earlier_rules_value_when_there_is_one()
    {
        var row = Row(2.5d).Write("Qty", CanonicalCell.FromDecimal(-7m), "earlier", "-7", "-7");

        Absolute("en-US").Apply(ref row);

        Assert.Equal(7m, row.Values["Quantity"].Number);
        Assert.Empty(row.Diagnostics);
    }

    // quantity.absolute now builds ScaledQuantityRule (#170). These tests keep checking the type they were written for;
    // the registration is checked in ScaledRuleTests.
#pragma warning disable CS0618
    private static INormalizationRule Absolute(string culture) => new CultureQuantityRule("Qty", "Quantity", System.Globalization.CultureInfo.GetCultureInfo(culture).NumberFormat);
#pragma warning restore CS0618

    private static Dictionary<string, string> Columns(string to) => new(StringComparer.Ordinal) { ["from"] = "Qty", ["to"] = to };

    private static RowContext Row(object? quantity) =>
        RowContext.Create("L1", Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal) { ["Qty"] = quantity });
}
