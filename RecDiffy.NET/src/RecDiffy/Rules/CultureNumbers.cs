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

    // #424: Grouped and its helpers have no seam outside this class, so this is a CHANGE of CultureNumbers.
    // The spans stay inside the helpers and the walk over groups is the measured win, which AGENTS.md permits.
    // .NET accepts a group separator anywhere, so "1,5" is 15 in en-US. Groups must sit where the culture puts them.
    private static bool Grouped(ReadOnlySpan<char> text, NumberFormatInfo format)
    {
        // The sign characters to trim, in a buffer on the stack unless a culture's signs are very long.
        var signLength = format.NegativeSign.Length + format.PositiveSign.Length + 3;
        Span<char> signs = signLength <= 32 ? stackalloc char[signLength] : new char[signLength];
        format.NegativeSign.CopyTo(signs);
        format.PositiveSign.CopyTo(signs[format.NegativeSign.Length..]);
        "-+−".CopyTo(signs[^3..]);

        // The integer part is a slice of the text, not a new string.
        var cut = text.IndexOf(format.NumberDecimalSeparator, StringComparison.Ordinal);
        var integer = (cut < 0 ? text : text[..cut]).Trim().Trim((ReadOnlySpan<char>)signs).Trim();

        // First walk counts the groups; one group means there is no separator, and nothing to check.
        var separator = format.NumberGroupSeparator;
        var count = 1;
        for (var rest = integer; NextSeparator(rest, separator) is { At: >= 0 } found; rest = rest[(found.At + found.Length)..])
            count++;
        if (count is 1)
            return true;

        // Second walk checks each group's length against the culture's sizes, counted from the right.
        // NumberGroupSizes is read only now, after a separator is known to exist, so a plain number allocates nothing.
        var sizes = format.NumberGroupSizes;
        var remaining = integer;
        for (var index = 0; index < count; index++)
        {
            var found = NextSeparator(remaining, separator);
            var length = found.At < 0 ? remaining.Length : found.At;
            var size = sizes.Length == 0 ? 0 : sizes[Math.Min(count - 1 - index, sizes.Length - 1)];
            var fits = index is 0 ? length >= 1 && (size == 0 || length <= size) : size > 0 && length == size;
            if (fits is false)
                return false;
            remaining = found.At < 0 ? default : remaining[(found.At + found.Length)..];
        }

        return true;
    }

    // The next group separator. A plain space also counts when the culture's separator is a no-break space (U+00A0).
    private static (int At, int Length) NextSeparator(ReadOnlySpan<char> text, string separator)
    {
        var at = separator.Length is 0 ? -1 : text.IndexOf(separator, StringComparison.Ordinal);
        var space = separator is " " ? text.IndexOf(' ') : -1;
        return space >= 0 && (at < 0 || space < at) ? (space, 1) : (at, separator.Length);
    }
}
