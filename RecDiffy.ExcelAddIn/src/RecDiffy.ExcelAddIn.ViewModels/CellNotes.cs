using System.Buffers;
using System.Collections.Frozen;
using System.Globalization;

namespace RecDiffy.ExcelAddIn.ViewModels;

public static class CellNotes
{
    private static readonly FrozenDictionary<char, string> WhiteSpaceNames = new Dictionary<char, string>
    {
        ['\t'] = "tab",
        ['\n'] = "line break",
        ['\v'] = "vertical tab",
        ['\f'] = "form feed",
        ['\r'] = "carriage return",
        [' '] = "space",
        ['\u0085'] = "next-line character",
        ['\u00A0'] = "non-breaking space",
        ['\u1680'] = "ogham space mark",
        ['\u2000'] = "en quad",
        ['\u2001'] = "em quad",
        ['\u2002'] = "en space",
        ['\u2003'] = "em space",
        ['\u2004'] = "three-per-em space",
        ['\u2005'] = "four-per-em space",
        ['\u2006'] = "six-per-em space",
        ['\u2007'] = "figure space",
        ['\u2008'] = "punctuation space",
        ['\u2009'] = "thin space",
        ['\u200A'] = "hair space",
        ['\u2028'] = "line separator",
        ['\u2029'] = "paragraph separator",
        ['\u202F'] = "narrow non-breaking space",
        ['\u205F'] = "medium mathematical space",
        ['\u3000'] = "ideographic space",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<char, string> InvisibleNames = new Dictionary<char, string>
    {
        ['\u00AD'] = "soft hyphen",
        ['\u200B'] = "zero-width space",
        ['\u200C'] = "zero-width non-joiner",
        ['\u200D'] = "zero-width joiner",
        ['\u2060'] = "word joiner",
        ['\uFEFF'] = "byte order mark",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<char, string> Names = WhiteSpaceNames.Concat(InvisibleNames).ToFrozenDictionary();

    private static readonly SearchValues<char> Hidden = SearchValues.Create([.. Names.Keys]);

    public static string? For(object? value) => value is string text ? ForText(text) : null;

    public static string? At(SourcePreview? preview, int row, int column) =>
        For(preview?.Rows.ElementAtOrDefault(row)?.ElementAtOrDefault(column));

    private static bool IsInvisible(char character) => InvisibleNames.ContainsKey(character);

    private static string? ForText(string text)
    {
        var first = text.AsSpan().IndexOfAnyExcept(Hidden);
        var last = text.AsSpan().LastIndexOfAnyExcept(Hidden);
        return (text.Length, first, last) switch
        {
            (0, _, _) => null,
            (_, -1, _) => Unseen(text),
            (var length, 0, var end) when end == length - 1 => null,
            _ => Edged(text[..first], text[first..(last + 1)], text[(last + 1)..]),
        };
    }

    private static string Unseen(string text) => text.Any(IsInvisible)
        ? $"Nothing visible: {Describe(text)}. The run keeps invisible characters, so this cell isn't empty."
        : $"Only white space: {Describe(text)}. The run reads this cell as empty.";

    private static string Edged(string lead, string core, string trail)
    {
        var where = (lead.Length, trail.Length) switch
        {
            ( > 0, > 0) => $"{Describe(lead)} before the text and {Describe(trail)} after it.",
            ( > 0, _) => $"{Describe(lead)} before the text.",
            _ => $"{Describe(trail)} after the text.",
        };
        var edges = lead + trail;
        var invisible = edges.Count(IsInvisible);
        var effect = (invisible, WhiteSpace: edges.Length - invisible) switch
        {
            (0, _) => $"The run trims white space, so it compares '{core}'.",
            (1, 0) => $"The run keeps it, so this value won't match '{core}'.",
            (_, 0) => $"The run keeps them, so this value won't match '{core}'.",
            _ => $"The run removes white space but not invisible characters, so this value won't match '{core}'.",
        };
        return $"{Capitalized(where)} {effect}";
    }

    private static string Describe(string run)
    {
        var parts = run.Replace("\r\n", "\n", StringComparison.Ordinal)
            .GroupBy(character => character)
            .Select(group => Counted(group.Key, group.Count()))
            .ToArray();
        return parts.Length == 1 ? parts[0] : $"{string.Join(", ", parts[..^1])} and {parts[^1]}";
    }

    private static string Counted(char character, int count)
    {
        var name = Names[character];
        var code = character < 0x80 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (U+{(int)character:X4})");
        return count == 1
            ? $"{Article(name)} {name}{code}"
            : string.Create(CultureInfo.InvariantCulture, $"{count} {name}s{code}");
    }

    private static string Article(string name) => name[0] is 'a' or 'e' or 'i' or 'o' or 'u' ? "an" : "a";

    private static string Capitalized(string sentence) => string.Concat(sentence[..1].ToUpperInvariant(), sentence[1..]);
}
