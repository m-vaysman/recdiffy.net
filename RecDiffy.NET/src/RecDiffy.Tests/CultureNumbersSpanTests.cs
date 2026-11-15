using System.Globalization;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// #424: the span-based Grouped in CultureNumbers must agree with the pre-change helpers in CultureNumbersOracle for
// every text and culture the engine reads, and the long-sign culture must not overflow the capped stack buffer.
public class CultureNumbersSpanTests
{
    private static readonly string[] Cultures = ["en-US", "sv-SE", "fr-FR", "de-DE", "en-IN", "de-CH", "ar-SA", ""];

    private static readonly string[] Texts =
    [
        "1234.56",
        "1,234,567.89",
        "1,5",
        "12,34",
        "-1,234",
        " 1,234 ",
        "+1,234.5",
        "1 234,5",
        "1\u00A0234,5",
        "1.234,56",
        "12,34,567.89",
        "1,23,456",
        ",123",
        "123,",
        "abc",
        "(1,234)",
        "1,234-",
        "−1,234",
        "1,,234",
        "0,000,000,000,000,000,000,000,000,000,000,000,000,000",
        "1234567",
        "0",
        "-0.5",
        "1.234.567",
        "1 234 567,8",
        "1'234.5",
        "- 1,234",
        "1,234,",
        ",",
        " ",
    ];

    public static TheoryData<string, string> TextsAndCultures()
    {
        var data = new TheoryData<string, string>();
        foreach (var culture in Cultures)
            foreach (var text in Texts)
                data.Add(culture, text);
        return data;
    }

    [Theory]
    [MemberData(nameof(TextsAndCultures))]
    public void ReadNumber_agrees_with_the_pre_change_oracle_for_every_text_in_every_culture(string cultureName, string text)
    {
        var format = string.IsNullOrEmpty(cultureName) ? NumberFormatInfo.InvariantInfo : CultureInfo.GetCultureInfo(cultureName).NumberFormat;

        Assert.Equal(CultureNumbersOracle.Expected(text, format), text.ReadNumber(format));
    }

    [Fact]
    public void A_culture_whose_signs_exceed_the_stack_buffer_is_read_but_never_allocated_on_the_stack()
    {
        var sign = new string('~', 40);
        var format = new NumberFormatInfo
        {
            NegativeSign = sign,
            PositiveSign = "+",
            NumberDecimalSeparator = ".",
            NumberGroupSeparator = ",",
            NumberGroupSizes = [3],
        };

        Assert.Equal(NumberRead.Of(-1234m), (sign + "1234").ReadNumber(format));
        Assert.Equal(NumberRead.Of(-1234m), (sign + "1,234").ReadNumber(format));
        Assert.Equal(NumberRead.Failed, (sign + "1,5").ReadNumber(format));
    }

    [Fact]
    public void A_culture_with_no_group_separator_accepts_or_rejects_a_plain_number_without_reading_group_sizes()
    {
        var format = new NumberFormatInfo
        {
            NegativeSign = "-",
            PositiveSign = "+",
            NumberDecimalSeparator = ".",
            NumberGroupSeparator = string.Empty,
            NumberGroupSizes = [3],
        };

        Assert.Equal(NumberRead.Of(1234m), "1234".ReadNumber(format));
        Assert.Equal(NumberRead.Failed, "1,234".ReadNumber(format));
    }
}
