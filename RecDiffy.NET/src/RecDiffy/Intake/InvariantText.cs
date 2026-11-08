using System.Globalization;

namespace RecDiffy;

// RecDiffy's own text for a value: a formattable value is written invariant, so the server's culture never changes it.
internal static class InvariantText
{
    public static string? Of(object? value) => value switch
    {
        null => null,
        string text => text,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
