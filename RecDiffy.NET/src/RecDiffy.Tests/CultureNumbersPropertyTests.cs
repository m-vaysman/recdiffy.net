using System.Globalization;
using System.Text;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// #424: seeded sweeps that the span-based Grouped reads every text as the pre-change helpers did. Most texts are built as
// digit groups joined by separators, because a text of random characters is rejected by decimal.TryParse on both sides
// and so never reaches the grouping rule. The seeds are constants, so a failure repeats on every machine and every run.
public class CultureNumbersPropertyTests
{
    private const int Samples = 5_000;
    private const int Seed = 20_261_115;

    // No-break space, narrow no-break space and the minus sign, built from their code points so no editor or tool can
    // swap one for a look-alike character.
    private static readonly string NoBreakSpace = char.ConvertFromUtf32(0x00A0);
    private static readonly string NarrowNoBreakSpace = char.ConvertFromUtf32(0x202F);
    private static readonly string Minus = char.ConvertFromUtf32(0x2212);

    private static readonly string[] Digits = ["0", "1", "2", "3", "4", "5", "6", "7", "8", "9"];

    public static TheoryData<string> Formats() => [.. CultureNumbersFormats.All.Keys.OrderBy(name => name, StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Formats))]
    public void Grouped_digit_texts_are_read_the_same_as_the_oracle(string name)
    {
        var format = CultureNumbersFormats.All[name];
        var random = new Random(Seed + CultureNumbersFormats.Index(name));
        string[] separators = [format.NumberGroupSeparator, format.NumberGroupSeparator, format.NumberGroupSeparator, " ", NoBreakSpace, NarrowNoBreakSpace, ",", ".", "'"];
        string[] signs = ["", "", "", "-", "+", Minus, format.NegativeSign, " ", "- "];

        for (var sample = 0; sample < Samples; sample++)
        {
            var text = GroupedText(random, format, separators, signs);
            var expected = CultureNumbersOracle.Expected(text, format);
            var actual = text.ReadNumber(format);
            Assert.True(actual == expected, $"\"{text}\" in {name}: expected {expected}, got {actual}.");
        }
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void Random_character_texts_are_read_the_same_as_the_oracle(string name)
    {
        var format = CultureNumbersFormats.All[name];
        var random = new Random(Seed + 1_000 + CultureNumbersFormats.Index(name));
        string[] alphabet = [.. Digits, ",", ".", " ", NoBreakSpace, "'", "-", "+", format.NumberGroupSeparator, format.NumberDecimalSeparator];

        for (var sample = 0; sample < Samples; sample++)
        {
            var text = string.Concat(Enumerable.Range(0, random.Next(1, 24)).Select(_ => alphabet[random.Next(alphabet.Length)]));
            var expected = CultureNumbersOracle.Expected(text, format);
            var actual = text.ReadNumber(format);
            Assert.True(actual == expected, $"\"{text}\" in {name}: expected {expected}, got {actual}.");
        }
    }

    // One to six groups of one to four digits, joined by a separator that is usually the culture's own, with an optional
    // sign in front or behind and an optional decimal part. Group lengths of two and three are the common case, so the
    // size rule is exercised both ways for the usual group sizes.
    private static string GroupedText(Random random, NumberFormatInfo format, string[] separators, string[] signs)
    {
        var text = new StringBuilder();
        text.Append(signs[random.Next(signs.Length)]);
        var groups = random.Next(1, 7);
        var separator = separators[random.Next(separators.Length)];
        for (var group = 0; group < groups; group++)
        {
            if (group > 0)
                text.Append(random.Next(10) is 0 ? separators[random.Next(separators.Length)] : separator);
            var length = random.Next(10) switch { < 5 => 3, < 8 => 2, 8 => 1, _ => 4 };
            for (var digit = 0; digit < length; digit++)
                text.Append(Digits[random.Next(Digits.Length)]);
        }

        if (random.Next(3) is 0)
            text.Append(format.NumberDecimalSeparator).Append(Digits[random.Next(Digits.Length)]).Append(Digits[random.Next(Digits.Length)]);
        if (random.Next(8) is 0)
            text.Append(signs[random.Next(signs.Length)]);
        return text.ToString();
    }
}
