using System.Globalization;
using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

public class CultureDecimalRuleTests
{
    [Fact]
    public void Decimal_parse_is_registered_to_the_culture_rule() =>
        Assert.IsType<CultureDecimalRule>(Rule("en-US"));

    [Theory]
    [InlineData("", "1,234.5", "1234.5")]
    [InlineData("en-US", "1,234.5", "1234.5")]
    [InlineData("en-US", "-1,000.50", "-1000.50")]
    [InlineData("en-US", " 42 ", "42")]
    [InlineData("de-DE", "1.234,5", "1234.5")]
    [InlineData("de-DE", "1,5", "1.5")]
    [InlineData("sv-SE", "1 234,5", "1234.5")]
    [InlineData("sv-SE", "1 234,5", "1234.5")]
    [InlineData("sv-SE", "−5", "-5")]
    [InlineData("hi-IN", "1,23,456.5", "123456.5")]
    public void Text_is_read_with_the_sides_culture(string culture, string raw, string expected)
    {
        var row = Apply(Rule(culture), raw);

        Assert.Equal(CanonicalCell.FromDecimal(RawValue.Number(expected)), row.Values["Quantity"]);
        Assert.Empty(row.Diagnostics);
    }

    // .NET's own parser reads every one of these as a number (1,5 is 15 in en-US). A group separator must sit where the culture puts one.
    [Theory]
    [InlineData("", "1,5")]
    [InlineData("en-US", "1,5")]
    [InlineData("en-US", "12,34")]
    [InlineData("en-US", "1234,5")]
    [InlineData("de-DE", "1.5")]
    [InlineData("de-DE", "1,234.5")]
    [InlineData("hi-IN", "123,456")]
    public void A_group_separator_where_the_culture_puts_none_is_Invalid(string culture, string raw)
    {
        var row = Apply(Rule(culture), raw);

        Assert.True(row.Values["Quantity"].IsNull);
        var diagnostic = Assert.Single(row.Diagnostics);
        Assert.Equal(("DecimalParse", DiagnosticSeverity.Invalid, raw), (diagnostic.Code, diagnostic.Severity, diagnostic.RawText));
    }

    [Theory]
    [InlineData("double")]
    [InlineData("float")]
    [InlineData("half")]
    public void Floating_point_is_Invalid_and_never_converted(string kind)
    {
        object value = kind switch
        {
            "double" => 2.5d,
            "float" => 2.5f,
            _ => (Half)2.5f,
        };

        var row = Apply(Rule("en-US"), value);

        Assert.True(row.Values["Quantity"].IsNull);
        var diagnostic = Assert.Single(row.Diagnostics);
        Assert.Equal(("FloatValue", DiagnosticSeverity.Invalid, "decimal.parse", "Quantity", "2.5"), (diagnostic.Code, diagnostic.Severity, diagnostic.RuleId, diagnostic.Field, diagnostic.RawText));
    }

    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(long))]
    [InlineData(typeof(short))]
    [InlineData(typeof(byte))]
    [InlineData(typeof(sbyte))]
    [InlineData(typeof(ushort))]
    [InlineData(typeof(uint))]
    [InlineData(typeof(ulong))]
    [InlineData(typeof(decimal))]
    public void Every_integer_type_and_decimal_is_read_whatever_the_culture(Type type)
    {
        var row = Apply(Rule("sv-SE"), Convert.ChangeType(100, type, CultureInfo.InvariantCulture));

        Assert.Equal(100m, row.Values["Quantity"].Number);
        Assert.Empty(row.Diagnostics);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_blank_value_is_a_silent_null(string? raw)
    {
        var row = Apply(Rule("en-US"), raw);

        Assert.True(row.Values["Quantity"].IsNull);
        Assert.Empty(row.Diagnostics);
    }

    [Theory]
    [InlineData("text", "abc")]
    [InlineData("bool", "true")]
    [InlineData("date", "2026-01-02")]
    public void An_unreadable_value_is_null_and_Invalid(string kind, string raw)
    {
        var row = Apply(Rule("en-US"), RawValue.Of(kind, raw));

        Assert.True(row.Values["Quantity"].IsNull);
        Assert.Equal("DecimalParse", Assert.Single(row.Diagnostics).Code);
    }

    private static INormalizationRule Rule(string culture) =>
        CultureProfile.Rules(culture).Build("decimal.parse", new Dictionary<string, string>(StringComparer.Ordinal) { ["from"] = "Qty", ["to"] = "Quantity" });

    private static RowContext Apply(INormalizationRule rule, object? quantity)
    {
        var row = RowContext.Create("L1", Side.Left, new Dictionary<string, object?>(StringComparer.Ordinal) { ["Qty"] = quantity });
        rule.Apply(ref row);
        return row;
    }
}
