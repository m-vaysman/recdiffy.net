namespace RecDiffy;

// Escape backslash first, then each delimiter, so a join on those characters stays one-to-one.
internal static class Delimited
{
    private static readonly char[] Reserved = ['\r', '\n', '\u001f', '|', ',', '='];

    public static string Escape(string part) => Encode(part, Reserved);

    public static string Join(char separator, IEnumerable<string> parts) =>
        string.Join(separator, parts.Select(part => Encode(part, Marks(separator))));

    public static string Concat(char separator, IEnumerable<string> escapedParts) =>
        string.Join(separator, escapedParts);

    private static char[] Marks(char separator) =>
        Reserved.Contains(separator) ? Reserved : [.. Reserved, separator];

    private static string Encode(string part, IReadOnlyList<char> marks)
    {
        var escaped = part.Replace("\\", "\\\\", StringComparison.Ordinal);
        foreach (var mark in marks)
            escaped = escaped.Replace(mark.ToString(), "\\" + mark, StringComparison.Ordinal);
        return escaped;
    }
}
