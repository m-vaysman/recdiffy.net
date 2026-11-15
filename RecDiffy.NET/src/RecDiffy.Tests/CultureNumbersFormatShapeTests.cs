using RecDiffy.Rules;

namespace RecDiffy.Tests;

// #424: fixed texts read under every number format in CultureNumbersFormats, against the pre-change helpers. These are
// the shapes a random sweep reaches rarely: group sizes of none, zero and a trailing zero, four groups under two sizes,
// a separator of two characters, a sign after the number, and separators at the edges or doubled.
public class CultureNumbersFormatShapeTests
{
    private static readonly string NoBreakSpace = char.ConvertFromUtf32(0x00A0);
    private static readonly string NarrowNoBreakSpace = char.ConvertFromUtf32(0x202F);

    private static readonly string[] Texts =
    [
        "1234,567", "1,234,567", "1,23,45,678", "12,34,567", "1,234", "12,34", "1,2,3", "1234", "12345678",
        "1, 234", "1,  234", "1, 234, 567", "1 ,234",
        "1,234 -", "1,234-", "-1,234", "- 1,234", " 1,234 ", "+1,234",
        ",1", "1,", ",", "1,,234", "0,0", "0,000", "00,000",
        "1.234", "1.234.567", "1.23.456", "1,234.5", "1.234,5", "1,234.5,6",
        "1 234", "1 23", "1 234 567", "1 234,5",
        "1" + NoBreakSpace + "234", "1" + NoBreakSpace + "23", "1 234" + NoBreakSpace + "567", "1" + NoBreakSpace + "234,5",
        "1" + NarrowNoBreakSpace + "234", "1" + NarrowNoBreakSpace + "23", "1" + NarrowNoBreakSpace + "234,5",
    ];

    public static TheoryData<string, string> FormatsAndTexts()
    {
        var data = new TheoryData<string, string>();
        foreach (var name in CultureNumbersFormats.All.Keys.OrderBy(key => key, StringComparer.Ordinal))
        {
            foreach (var text in Texts)
                data.Add(name, text);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FormatsAndTexts))]
    public void ReadNumber_agrees_with_the_pre_change_helpers(string name, string text)
    {
        var format = CultureNumbersFormats.All[name];

        Assert.Equal(CultureNumbersOracle.Expected(text, format), text.ReadNumber(format));
    }

    // The oracle is only as good as the cases it decides differently. These pin, with literal expectations, that the
    // size rule is live under each built format: without them a comparison of two wrong answers would still pass.
    [Theory]
    [InlineData("sizes 3,2", "12,34,567", true)]
    [InlineData("sizes 3,2", "1,234,567", false)]
    [InlineData("sizes 3,0", "1234,567", true)]
    [InlineData("sizes 3,0", "1,234,567", false)]
    [InlineData("sizes 2", "1,23,45", true)]
    [InlineData("sizes 2", "1,234", false)]
    [InlineData("sizes none", "1,234", false)]
    [InlineData("sizes 0", "1,234", false)]
    [InlineData("separator of two characters", "1, 234", true)]
    [InlineData("separator of two characters", "1, 23", false)]
    [InlineData("separator full stop", "1.234,5", true)]
    [InlineData("separator full stop", "1.23,5", false)]
    public void The_size_rule_decides_under_each_built_format(string name, string text, bool isNumber) =>
        Assert.Equal(isNumber, text.ReadNumber(CultureNumbersFormats.All[name]) != NumberRead.Failed);
}
