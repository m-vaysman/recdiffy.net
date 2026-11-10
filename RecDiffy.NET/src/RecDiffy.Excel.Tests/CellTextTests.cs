using System.Xml;
using RecDiffy.Excel.Writing;

namespace RecDiffy.Excel.Tests;

// CellText.Clean (#256): every character XML 1.0 cannot carry, an unpaired surrogate included, becomes U+FFFD, and the
// 32,767-character cut never splits a pair. Code units are passed as numbers: a lone surrogate does not survive test data serialization.
public class CellTextTests
{
    private const int MaxCellText = 32_767;
    private const int Replacement = 0xFFFD;

    [Theory]
    [InlineData(new[] { 0x41, 0xD800, 0x42 }, new[] { 0x41, Replacement, 0x42 })]
    [InlineData(new[] { 0x41, 0xDC00, 0x42 }, new[] { 0x41, Replacement, 0x42 })]
    [InlineData(new[] { 0x41, 0xDBFF }, new[] { 0x41, Replacement })]
    [InlineData(new[] { 0xDFFF, 0x41 }, new[] { Replacement, 0x41 })]
    [InlineData(new[] { 0xDC00, 0xD800 }, new[] { Replacement, Replacement })]
    [InlineData(new[] { 0xD800, 0xD800, 0xDC00 }, new[] { Replacement, 0xD800, 0xDC00 })]
    [InlineData(new[] { 0xD800, 0xDC00, 0xDC00 }, new[] { 0xD800, 0xDC00, Replacement })]
    [InlineData(new[] { 0xD83D, 0xDE00 }, new[] { 0xD83D, 0xDE00 })]
    [InlineData(new[] { 0x01, 0x41, 0x07 }, new[] { Replacement, 0x41, Replacement })]
    [InlineData(new[] { 0xFFFE, 0xFFFF }, new[] { Replacement, Replacement })]
    [InlineData(new[] { 0x09, 0x0A, 0x0D, 0x20 }, new[] { 0x09, 0x0A, 0x0D, 0x20 })]
    public void Characters_xml_cannot_carry_become_the_replacement_character(int[] input, int[] expected) =>
        Assert.Equal(Text(expected), CellText.Clean(Text(input)));

    // Every string of up to three code units from a set of awkward ones: the result is XML, the same length, changed only to U+FFFD,
    // and text that is already XML is returned as it is.
    [Fact]
    public void Clean_returns_xml_and_changes_only_what_xml_cannot_carry()
    {
        int[] units = [0x41, 0x09, 0x01, 0xD800, 0xDBFF, 0xDC00, 0xDFFF, 0xFFFE, Replacement];
        var inputs = Enumerable.Range(0, 4).SelectMany(length => Strings(units, length)).ToArray();

        var results = inputs.Select(input => (Input: input, Cleaned: CellText.Clean(input))).ToArray();

        Assert.Equal(820, results.Length);
        Assert.All(results, result =>
        {
            Assert.True(IsXml(result.Cleaned), $"Not XML: {Units(result.Cleaned)}");
            Assert.Equal(result.Input.Length, result.Cleaned.Length);
            Assert.All(result.Input.Zip(result.Cleaned), pair => Assert.True(pair.First == pair.Second || pair.Second == '\uFFFD'));
            Assert.True(IsXml(result.Input) is false || result.Cleaned == result.Input, $"Changed valid text: {Units(result.Input)}");
        });
    }

    [Fact]
    public void A_pair_that_the_cut_would_split_is_dropped_whole()
    {
        var text = new string('A', MaxCellText - 1) + "\U0001F600" + "B";

        var cleaned = CellText.Clean(text);

        Assert.Equal(new string('A', MaxCellText - 1), cleaned);
    }

    [Fact]
    public void A_pair_that_ends_at_the_limit_is_kept()
    {
        var text = new string('A', MaxCellText - 2) + "\U0001F600" + "B";

        var cleaned = CellText.Clean(text);

        Assert.Equal(new string('A', MaxCellText - 2) + "\U0001F600", cleaned);
    }

    [Fact]
    public void An_unpaired_high_surrogate_at_the_limit_is_replaced_and_kept()
    {
        var text = new string('A', MaxCellText - 1) + (char)0xD800 + "B";

        var cleaned = CellText.Clean(text);

        Assert.Equal(new string('A', MaxCellText - 1) + '\uFFFD', cleaned);
    }

    [Fact]
    public void Text_longer_than_a_cell_holds_is_cut_to_the_limit()
    {
        var cleaned = CellText.Clean(new string('A', MaxCellText + 10));

        Assert.Equal(MaxCellText, cleaned.Length);
    }

    private static IEnumerable<string> Strings(int[] units, int length) =>
        Enumerable.Range(0, length).Aggregate(
            (IEnumerable<string>)[""],
            (prefixes, _) => prefixes.SelectMany(prefix => units.Select(unit => prefix + (char)unit)));

    private static bool IsXml(string text) => Record.Exception(() => XmlConvert.VerifyXmlChars(text)) is null;

    private static string Text(int[] units) => new([.. units.Select(unit => (char)unit)]);

    private static string Units(string text) => string.Join(" ", text.Select(character => ((int)character).ToString("X4", System.Globalization.CultureInfo.InvariantCulture)));
}
