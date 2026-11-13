using System.Globalization;

namespace RecDiffy.Tests;

// InvariantFormatting pins the culture of one interpolated value. Excel formulas, cell references and other
// machine-readable text must read the same on every machine, so a decimal never prints 0,5 and a date never
// prints in a local order.
public class InvariantFormattingTests
{
    [Fact]
    public void An_integer_formats_the_same_under_every_culture()
    {
        foreach (var culture in Cultures())
        {
            var text = Under(culture, () => 1234.ToInvariant());
            Assert.Equal("1234", text);
        }
    }

    [Fact]
    public void A_long_formats_the_same_under_every_culture()
    {
        foreach (var culture in Cultures())
        {
            var text = Under(culture, () => 4294967296L.ToInvariant());
            Assert.Equal("4294967296", text);
        }
    }

    [Fact]
    public void A_decimal_uses_a_dot_for_the_decimal_separator_under_a_comma_culture()
    {
        var german = Under("de-DE", () => 0.5m.ToInvariant());

        Assert.Equal("0.5", german);
    }

    [Fact]
    public void A_decimal_uses_a_dot_for_the_decimal_separator_under_a_french_culture()
    {
        var french = Under("fr-FR", () => 0.5m.ToInvariant());

        Assert.Equal("0.5", french);
    }

    [Fact]
    public void A_decimal_keeps_its_dot_through_a_whole_interpolated_formula()
    {
        var formula = Under("de-DE", () => $"IF(H{12.ToInvariant()}=0,0,ABS(G{12.ToInvariant()}/{0.5m.ToInvariant()}))");

        Assert.Equal("IF(H12=0,0,ABS(G12/0.5))", formula);
    }

    [Fact]
    public void An_integer_with_a_format_string_uses_that_format()
    {
        var text = Under("de-DE", () => 7.ToInvariant("D4"));

        Assert.Equal("0007", text);
    }

    [Fact]
    public void A_decimal_with_a_format_string_uses_invariant_digits()
    {
        var text = Under("de-DE", () => 1234.5m.ToInvariant("N2"));

        Assert.Equal("1,234.50", text);
    }

    [Fact]
    public void A_date_formats_in_the_invariant_order()
    {
        var text = Under("de-DE", () => new DateOnly(2026, 11, 13).ToInvariant("yyyy-MM-dd"));

        Assert.Equal("2026-11-13", text);
    }

    private static string[] Cultures() => ["en-US", "de-DE", "fr-FR", "ar-SA"];

    // The culture is set on the thread for the duration of the call, then restored, so no other test sees it.
    private static T Under<T>(string culture, Func<T> format)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            return format();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
