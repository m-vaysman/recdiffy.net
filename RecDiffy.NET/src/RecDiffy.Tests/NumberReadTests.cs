using System.Globalization;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

public class NumberReadTests
{
    [Theory]
    [InlineData("decimal", "-100.25", "-100.25")]
    [InlineData("int", "-100", "-100")]
    [InlineData("long", "-100", "-100")]
    [InlineData("double", "-2.5", "-2.5")]
    [InlineData("float", "-2.5", "-2.5")]
    [InlineData("text", "-1,000.50", "-1000.50")]
    [InlineData("text", " 42 ", "42")]
    [InlineData("int", "-2147483648", "-2147483648")]
    [InlineData("long", "-9223372036854775808", "-9223372036854775808")]
    [InlineData("long", "9223372036854775807", "9223372036854775807")]
    public void A_number_of_every_intake_type_is_read(string kind, string raw, string expected) =>
        Assert.Equal(NumberRead.Of(RawValue.Number(expected)), RawValue.Of(kind, raw).CanBeNumber());

    [Theory]
    [InlineData("1,5")]
    [InlineData("12,34")]
    [InlineData("1,50")]
    [InlineData("1234,5")]
    [InlineData("1,2345")]
    [InlineData(",123")]
    [InlineData("1,,234")]
    [InlineData("1.234,5")]
    public void A_comma_that_does_not_group_thousands_fails(string raw) =>
        Assert.Equal(NumberRead.Failed, raw.CanBeNumber());

    [Theory]
    [InlineData("1,234.5", "1234.5")]
    [InlineData("-1,000.50", "-1000.50")]
    [InlineData("12,345,678", "12345678")]
    [InlineData(" 1,234 ", "1234")]
    [InlineData("999", "999")]
    public void A_comma_that_groups_thousands_is_read(string raw, string expected) =>
        Assert.Equal(NumberRead.Of(RawValue.Number(expected)), raw.CanBeNumber());

    [Theory]
    [InlineData("null", "")]
    [InlineData("text", "")]
    public void Null_and_empty_text_are_missing(string kind, string raw) =>
        Assert.Equal(NumberRead.Missing, RawValue.Of(kind, raw).CanBeNumber());

    [Theory]
    [InlineData("text", "abc")]
    [InlineData("text", "1,000.5x")]
    [InlineData("bool", "true")]
    [InlineData("date", "2026-01-02")]
    [InlineData("double", "NaN")]
    [InlineData("double", "Infinity")]
    [InlineData("float", "NaN")]
    public void A_value_that_is_not_a_number_fails(string kind, string raw) =>
        Assert.Equal(NumberRead.Failed, RawValue.Of(kind, raw).CanBeNumber());

    [Fact]
    public void Text_is_read_with_the_invariant_culture_whatever_the_current_culture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            Assert.Equal(NumberRead.Of(1.5m), "1.5".CanBeNumber());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Convert_calls_only_the_branch_for_its_kind()
    {
        Assert.Equal(7m, NumberRead.Of(7m).Convert(value => value, () => -1m, () => -2m));
        Assert.Equal(-1m, NumberRead.Missing.Convert(value => value, () => -1m, () => -2m));
        Assert.Equal(-2m, NumberRead.Failed.Convert(value => value, () => -1m, () => -2m));
    }
}

internal static class RawValue
{
    public static object? Of(string kind, string text) => kind switch
    {
        "null" => null,
        "text" => text,
        "decimal" => Number(text),
        "int" => int.Parse(text, CultureInfo.InvariantCulture),
        "long" => long.Parse(text, CultureInfo.InvariantCulture),
        "double" => double.Parse(text, CultureInfo.InvariantCulture),
        "float" => float.Parse(text, CultureInfo.InvariantCulture),
        "bool" => bool.Parse(text),
        "date" => DateOnly.Parse(text, CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown raw value kind."),
    };

    public static decimal Number(string text) => decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);
}
