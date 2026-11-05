namespace RecDiffy;

// Escape backslash first, then each delimiter, so a join on those characters stays one-to-one.
internal static class Delimited
{
    public static string Escape(string part) =>
        part.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\\r", StringComparison.Ordinal)
            .Replace("\n", "\\\n", StringComparison.Ordinal)
            .Replace("\u001f", "\\\u001f", StringComparison.Ordinal);

    public static string Join(char separator, IEnumerable<string> parts) =>
        string.Join(separator, parts.Select(Escape));
}
