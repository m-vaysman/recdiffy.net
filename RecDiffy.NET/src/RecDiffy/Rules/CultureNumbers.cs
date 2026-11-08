using System.Globalization;

namespace RecDiffy.Rules;

// Reads a raw value as a number in one declared format. Floating point is not a number here: the rule reports it before calling this.
internal static class CultureNumbers
{
    public static NumberRead ReadNumber(this object? raw, NumberFormatInfo format) => raw switch
    {
        null or "" => NumberRead.Missing,
        decimal value => NumberRead.Of(value),
        int value => NumberRead.Of(value),
        long value => NumberRead.Of(value),
        short value => NumberRead.Of(value),
        byte value => NumberRead.Of(value),
        sbyte value => NumberRead.Of(value),
        ushort value => NumberRead.Of(value),
        uint value => NumberRead.Of(value),
        ulong value => NumberRead.Of(value),
        string text when Grouped(text, format) && decimal.TryParse(text, NumberStyles.Number, format, out var parsed) => NumberRead.Of(parsed),
        _ => NumberRead.Failed,
    };

    // .NET accepts a group separator anywhere, so "1,5" is 15 in en-US. Groups must sit where the culture puts them.
    private static bool Grouped(string text, NumberFormatInfo format)
    {
        var groups = IntegerPart(text, format).Split(GroupSeparators(format), StringSplitOptions.None);
        return groups is [_] || SizesFit(groups, format.NumberGroupSizes);
    }

    // .NET reads a plain space as the separator when the culture's separator is a no-break space (sv-SE, fr-FR).
    private static string[] GroupSeparators(NumberFormatInfo format) =>
        format.NumberGroupSeparator is " " ? [format.NumberGroupSeparator, " "] : [format.NumberGroupSeparator];

    private static string IntegerPart(string text, NumberFormatInfo format)
    {
        var cut = text.IndexOf(format.NumberDecimalSeparator, StringComparison.Ordinal);
        var integer = cut < 0 ? text : text[..cut];
        char[] signs = [.. format.NegativeSign, .. format.PositiveSign, '-', '+', '−'];
        return integer.Trim().Trim(signs).Trim();
    }

    // Groups are checked from the right: the first size applies to the group nearest the decimal point, and the last size repeats.
    private static bool SizesFit(string[] groups, int[] sizes)
    {
        var inner = groups[1..].Reverse().Select((group, index) => (Length: group.Length, Size: SizeAt(sizes, index)));
        var leading = SizeAt(sizes, groups.Length - 1);
        var leadingFits = groups[0].Length >= 1 && (leading == 0 || groups[0].Length <= leading);
        return leadingFits && inner.All(group => group.Size > 0 && group.Length == group.Size);
    }

    private static int SizeAt(int[] sizes, int index) => sizes.Length == 0 ? 0 : sizes[Math.Min(index, sizes.Length - 1)];
}
