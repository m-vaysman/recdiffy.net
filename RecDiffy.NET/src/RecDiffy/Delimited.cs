using System.Buffers;

namespace RecDiffy;

// Escape backslash first, then each delimiter, so a join on those characters stays one-to-one.
internal static class Delimited
{
    // The characters every join escapes, whatever its separator.
    private static readonly char[] Reserved = ['\r', '\n', '\u001f', '|', ',', '='];

    // Each reserved character as text, beside its escaped form. Built once: Encode used to build these 12 strings
    // again for every value it was given (#403).
    private static readonly (string Mark, string Escaped)[] ReservedPairs = Pairs(Reserved);

    // Answers one question quickly: does this value hold anything that needs escaping? It includes the backslash,
    // because a backslash is always doubled.
    private static readonly SearchValues<char> ReservedSearch = Search(Reserved);

    public static string Escape(string part) => Encode(part, ReservedSearch, ReservedPairs);

    // A separator already in Reserved must stay once. A second pass rewrites the escape the first pass wrote.
    // A separator outside Reserved needs tables of its own, or a value that holds it would take the fast path in
    // Encode and stay unescaped. Only RecBuilder joins that way (member names on '.'), and not once per row.
    public static string Join(char separator, IEnumerable<string> parts)
    {
        var (search, pairs) = Reserved.Contains(separator)
            ? (ReservedSearch, ReservedPairs)
            : (Search([.. Reserved, separator]), Pairs([.. Reserved, separator]));
        return string.Join(separator, parts.Select(part => Encode(part, search, pairs)));
    }

    // Parts must already be escaped. A raw separator splits the result.
    public static string Concat(char separator, IEnumerable<string> escapedParts) =>
        string.Join(separator, escapedParts);

    private static string Encode(string part, SearchValues<char> search, (string Mark, string Escaped)[] pairs)
    {
        // Fast path. Most values (a trade id, a counterparty code, a date) hold nothing to escape, so the value is
        // returned as it is and nothing is allocated.
        if (part.AsSpan().IndexOfAny(search) < 0)
            return part;

        // Slow path, in the same order as before: the backslash first, then each mark.
        var escaped = part.Replace("\\", "\\\\", StringComparison.Ordinal);
        foreach (var (mark, replacement) in pairs)
            escaped = escaped.Replace(mark, replacement, StringComparison.Ordinal);
        return escaped;
    }

    private static (string Mark, string Escaped)[] Pairs(char[] marks) =>
        [.. marks.Select(mark => (mark.ToString(), "\\" + mark))];

    private static SearchValues<char> Search(char[] marks) =>
        SearchValues.Create(['\\', .. marks]);
}
