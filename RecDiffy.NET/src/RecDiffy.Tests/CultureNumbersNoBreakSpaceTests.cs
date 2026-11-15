using System.Globalization;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// #424: when a culture groups digits with a no-break space (U+00A0), a plain space typed in its place is a group
// separator too, so its groups are checked like any others. The format is built here, not taken from a named culture,
// because which cultures use U+00A0 depends on the machine's culture data.
public class CultureNumbersNoBreakSpaceTests
{
    private static readonly NumberFormatInfo NoBreakSpaceGroups = Format("\u00a0");

    [Theory]
    [InlineData("1 234,5", "1234.5")]
    [InlineData("1\u00a0234,5", "1234.5")]
    [InlineData("1 234 567", "1234567")]
    [InlineData("1 234\u00a0567,8", "1234567.8")]
    [InlineData("1234,5", "1234.5")]
    public void A_plain_space_in_the_right_place_is_read_as_the_group_separator(string text, string expected) =>
        Assert.Equal(NumberRead.Of(decimal.Parse(expected, CultureInfo.InvariantCulture)), text.ReadNumber(NoBreakSpaceGroups));

    [Theory]
    [InlineData("1 23,5")]
    [InlineData("12 34")]
    [InlineData("1 2345")]
    [InlineData("1\u00a023,5")]
    [InlineData("1 234 56")]
    public void A_plain_space_in_the_wrong_place_is_not_a_number(string text) =>
        Assert.Equal(NumberRead.Failed, text.ReadNumber(NoBreakSpaceGroups));

    [Theory]
    [InlineData("1 23,5")]
    [InlineData("12 34")]
    [InlineData("1 234,5")]
    [InlineData("1\u00a023,5")]
    public void The_engine_agrees_with_the_pre_change_helpers(string text) =>
        Assert.Equal(CultureNumbersOracle.Expected(text, NoBreakSpaceGroups), text.ReadNumber(NoBreakSpaceGroups));

    // A culture that groups with a full stop does not take a plain space as a separator: the space is not checked as a
    // group, and .NET decides what the text is.
    [Fact]
    public void A_plain_space_is_not_a_separator_for_a_culture_that_groups_with_something_else()
    {
        var fullStopGroups = Format(".");

        Assert.Equal(CultureNumbersOracle.Expected("1 23,5", fullStopGroups), "1 23,5".ReadNumber(fullStopGroups));
    }

    private static NumberFormatInfo Format(string groupSeparator)
    {
        var format = (NumberFormatInfo)NumberFormatInfo.InvariantInfo.Clone();
        format.NumberGroupSeparator = groupSeparator;
        format.NumberDecimalSeparator = ",";
        format.NumberGroupSizes = [3];
        return format;
    }
}
