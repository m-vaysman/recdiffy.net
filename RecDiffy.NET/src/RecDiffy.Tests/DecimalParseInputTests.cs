using RecDiffy.Rules;

namespace RecDiffy.Tests;

// decimal.parse, as the registry builds it today (CultureDecimalRule), for the inputs #148 lists: blank cells, every intake
// type, floating point that cannot be a decimal, invariant text with signs and exponents, and rounding to the field scale.
public class DecimalParseInputTests
{
    private static readonly RuleBench Parse = new RuleBench("decimal.parse").With("from", "Qty").With("to", "Quantity");

    [Fact]
    public void A_column_the_row_does_not_have_is_a_silent_null()
    {
        var row = Parse.Apply(("Other", "5"));

        Assert.True(row.Values["Quantity"].IsNull);
        Assert.Empty(row.Diagnostics);
    }

    // Floating point is never converted (#158), so a value decimal could hold and one it could not are reported alike.
    [Theory]
    [InlineData(double.NaN, "NaN")]
    [InlineData(double.PositiveInfinity, "Infinity")]
    [InlineData(double.NegativeInfinity, "-Infinity")]
    [InlineData(1e300, "1E+300")]
    [InlineData(-1e29, "-1E+29")]
    [InlineData(double.Epsilon, "5E-324")]
    [InlineData(0d, "0")]
    public void Every_double_is_FloatValue_whatever_its_value(double value, string rawText) =>
        AssertFloatValue(Parse.Apply(("Qty", value)), rawText);

    [Theory]
    [InlineData(float.NaN, "NaN")]
    [InlineData(float.PositiveInfinity, "Infinity")]
    [InlineData(float.MaxValue, "3.4028235E+38")]
    [InlineData(-1f, "-1")]
    public void Every_float_is_FloatValue_whatever_its_value(float value, string rawText) =>
        AssertFloatValue(Parse.Apply(("Qty", value)), rawText);

    [Theory]
    [InlineData("int", "-2147483648")]
    [InlineData("int", "2147483647")]
    [InlineData("long", "-9223372036854775808")]
    [InlineData("long", "9223372036854775807")]
    public void Integer_extremes_are_read_exactly(string kind, string raw)
    {
        var row = Parse.Apply(("Qty", RawValue.Of(kind, raw)));

        Assert.Equal(RawValue.Number(raw), row.Values["Quantity"].Number);
        Assert.Empty(row.Diagnostics);
    }

    [Fact]
    public void The_largest_unsigned_long_and_the_decimal_range_are_read_exactly()
    {
        Assert.Equal(18446744073709551615m, Parse.Apply(("Qty", ulong.MaxValue)).Values["Quantity"].Number);
        Assert.Equal(decimal.MaxValue, Parse.Apply(("Qty", decimal.MaxValue)).Values["Quantity"].Number);
        Assert.Equal(decimal.MinValue, Parse.Apply(("Qty", decimal.MinValue)).Values["Quantity"].Number);
    }

    [Theory]
    [InlineData("+5", "5")]
    [InlineData("-5", "-5")]
    [InlineData("5-", "-5")]
    [InlineData("-0", "0")]
    [InlineData(".5", "0.5")]
    [InlineData("5.", "5")]
    [InlineData("0001.10", "1.10")]
    [InlineData("-1,234,567.89", "-1234567.89")]
    [InlineData("79228162514264337593543950335", "79228162514264337593543950335")]
    public void Invariant_text_with_a_sign_or_a_bare_point_is_read(string raw, string expected)
    {
        var row = Parse.Apply(("Qty", raw));

        Assert.Equal(RawValue.Number(expected), row.Values["Quantity"].Number);
        Assert.Empty(row.Diagnostics);
    }

    // An exponent, accounting brackets, a currency or percent sign, and a number past decimal's range are not read.
    [Theory]
    [InlineData("1E3")]
    [InlineData("1.5e-2")]
    [InlineData("(5)")]
    [InlineData("$5")]
    [InlineData("5%")]
    [InlineData("--5")]
    [InlineData("+-5")]
    [InlineData("1.2.3")]
    [InlineData("1 000")]
    [InlineData("0x1F")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("79228162514264337593543950336")]
    [InlineData("-79228162514264337593543950336")]
    public void Text_that_is_not_an_invariant_number_is_Invalid(string raw)
    {
        var row = Parse.Apply(("Qty", raw));

        Assert.True(row.Values["Quantity"].IsNull);
        var diagnostic = Assert.Single(row.Diagnostics);
        Assert.Equal(("DecimalParse", DiagnosticSeverity.Invalid, "decimal.parse", "Quantity", raw), (diagnostic.Code, diagnostic.Severity, diagnostic.RuleId, diagnostic.Field, diagnostic.RawText));
    }

    [Theory]
    [InlineData("2.345", 2, MidpointMode.ToEven, "2.34")]
    [InlineData("2.345", 2, MidpointMode.AwayFromZero, "2.35")]
    [InlineData("2.355", 2, MidpointMode.ToEven, "2.36")]
    [InlineData("-2.345", 2, MidpointMode.ToEven, "-2.34")]
    [InlineData("-2.345", 2, MidpointMode.AwayFromZero, "-2.35")]
    [InlineData("2.3449", 2, MidpointMode.AwayFromZero, "2.34")]
    [InlineData("2.5", 0, MidpointMode.ToEven, "2")]
    [InlineData("3.5", 0, MidpointMode.ToEven, "4")]
    [InlineData("2.5", 0, MidpointMode.AwayFromZero, "3")]
    [InlineData("-2.5", 0, MidpointMode.AwayFromZero, "-3")]
    [InlineData("0.00005", 4, MidpointMode.ToEven, "0.0000")]
    [InlineData("0.00015", 4, MidpointMode.ToEven, "0.0002")]
    [InlineData("1.5", 4, MidpointMode.ToEven, "1.5")]
    public void Text_is_rounded_to_the_field_scale_with_the_profile_midpoint(string raw, int scale, MidpointMode midpoint, string expected)
    {
        var row = (Parse with { Scale = scale, Midpoint = midpoint }).Apply(("Qty", raw));

        Assert.Equal(RawValue.Number(expected), row.Values["Quantity"].Number);
        Assert.Equal(expected, Assert.Single(row.Traces).Canonical);
    }

    [Theory]
    [InlineData(MidpointMode.ToEven, "1.00")]
    [InlineData(MidpointMode.AwayFromZero, "1.01")]
    public void A_typed_decimal_is_rounded_as_text_is(MidpointMode midpoint, string expected)
    {
        var row = (Parse with { Midpoint = midpoint }).Apply(("Qty", 1.005m));

        Assert.Equal(expected, Assert.Single(row.Traces).Canonical);
    }

    [Fact]
    public void The_trace_keeps_the_raw_text_and_the_canonical_text()
    {
        var row = Parse.Apply(("Qty", "-1,234.567"));

        Assert.Equal(new TraceStep("decimal.parse", "Quantity", "-1,234.567", "-1234.57"), Assert.Single(row.Traces));
    }

    private static void AssertFloatValue(RowContext row, string rawText)
    {
        Assert.True(row.Values["Quantity"].IsNull);
        var diagnostic = Assert.Single(row.Diagnostics);
        Assert.Equal(("FloatValue", DiagnosticSeverity.Invalid, "decimal.parse", "Quantity", rawText), (diagnostic.Code, diagnostic.Severity, diagnostic.RuleId, diagnostic.Field, diagnostic.RawText));
    }
}
