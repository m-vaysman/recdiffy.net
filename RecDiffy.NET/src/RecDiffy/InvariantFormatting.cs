using System.Globalization;

namespace RecDiffy;

/// <summary>
/// Formats a single value with <see cref="CultureInfo.InvariantCulture"/>, for text that machines read back.
/// </summary>
/// <remarks>
/// An interpolated hole such as <c>$"{row}"</c> formats through the current culture. Excel formulas, cell
/// references and other machine-readable text must not change with the machine's culture: on de-DE a decimal
/// prints <c>0,5</c>, which breaks a formula, and a date prints in an order the reader does not expect. These
/// methods fix the culture of one value, so the rest of the interpolated string stays as written.
/// This type is public because the samples are compiled against the packed package, where an internal
/// helper is not visible.
/// </remarks>
public static class InvariantFormatting
{
    /// <summary>
    /// Formats <paramref name="value"/> with the invariant culture, using the type's default format.
    /// </summary>
    /// <typeparam name="T">The value type, which must know how to format itself.</typeparam>
    /// <param name="value">The value to format.</param>
    /// <returns>The value as invariant text, the same on every machine.</returns>
    public static string ToInvariant<T>(this T value)
        where T : IFormattable =>
        value.ToString(null, CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats <paramref name="value"/> with the invariant culture and the given format.
    /// </summary>
    /// <typeparam name="T">The value type, which must know how to format itself.</typeparam>
    /// <param name="value">The value to format.</param>
    /// <param name="format">A standard or custom format string, or <see langword="null"/> for the default format.</param>
    /// <returns>The value as invariant text, the same on every machine.</returns>
    public static string ToInvariant<T>(this T value, string? format)
        where T : IFormattable =>
        value.ToString(format, CultureInfo.InvariantCulture);
}
