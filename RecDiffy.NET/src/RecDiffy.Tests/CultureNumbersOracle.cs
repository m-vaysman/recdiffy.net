using System.Globalization;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// A verbatim copy of the CultureNumbers grouping helpers as they were before #424, kept as the oracle the new
// span-based helpers in CultureNumbers must agree with. It must stay untouched, or the comparison tests lose their anchor.
internal static class CultureNumbersOracle
{
    public static bool Grouped(string text, NumberFormatInfo format)
    {
        var groups = IntegerPart(text, format).Split(GroupSeparators(format), StringSplitOptions.None);
        return groups is [_] || SizesFit(groups, format.NumberGroupSizes);
    }

    private static string[] GroupSeparators(NumberFormatInfo format) =>
        format.NumberGroupSeparator is " " ? [format.NumberGroupSeparator, " "] : [format.NumberGroupSeparator];

    private static string IntegerPart(string text, NumberFormatInfo format)
    {
        var cut = text.IndexOf(format.NumberDecimalSeparator, StringComparison.Ordinal);
        var integer = cut < 0 ? text : text[..cut];
        char[] signs = [.. format.NegativeSign, .. format.PositiveSign, '-', '+', '−'];
        return integer.Trim().Trim(signs).Trim();
    }

    private static bool SizesFit(string[] groups, int[] sizes)
    {
        var inner = groups[1..].Reverse().Select((group, index) => (Length: group.Length, Size: SizeAt(sizes, index)));
        var leading = SizeAt(sizes, groups.Length - 1);
        var leadingFits = groups[0].Length >= 1 && (leading == 0 || groups[0].Length <= leading);
        return leadingFits && inner.All(group => group.Size > 0 && group.Length == group.Size);
    }

    private static int SizeAt(int[] sizes, int index) => sizes.Length == 0 ? 0 : sizes[Math.Min(index, sizes.Length - 1)];

    public static NumberRead Expected(string text, NumberFormatInfo format) => text.Length == 0
        ? NumberRead.Missing
        : Grouped(text, format) && decimal.TryParse(text, NumberStyles.Number, format, out var parsed)
            ? NumberRead.Of(parsed)
            : NumberRead.Failed;
}
