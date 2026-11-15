using System.Globalization;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// #424: a seeded sweep of random digit-and-punctuation texts that the span-based Grouped matches the pre-change oracle.
public class CultureNumbersPropertyTests
{
    private const int Samples = 5_000;

    private static readonly char[] Alphabet = ['0', '1', '2', '3', '4', '5', '6', '7', '8', '9', ',', '.', ' ', '\u00A0', '\'', '-', '+'];

    [Theory]
    [InlineData("en-US")]
    [InlineData("sv-SE")]
    [InlineData("en-IN")]
    public void Random_texts_are_read_the_same_as_the_oracle_in_the_culture(string cultureName)
    {
        var format = CultureInfo.GetCultureInfo(cultureName).NumberFormat;
        var random = new Random(StringComparer.Ordinal.GetHashCode(cultureName));

        for (var index = 0; index < Samples; index++)
        {
            var text = Text(random);
            var actual = text.ReadNumber(format);
            var expected = CultureNumbersOracle.Expected(text, format);
            Assert.True(actual == expected, $"\"{text}\" in {cultureName}: expected {expected}, got {actual}.");
        }
    }

    private static string Text(Random random)
    {
        var length = random.Next(1, 24);
        Span<char> buffer = stackalloc char[length];
        for (var index = 0; index < length; index++)
            buffer[index] = Alphabet[random.Next(Alphabet.Length)];
        return new string(buffer);
    }
}
