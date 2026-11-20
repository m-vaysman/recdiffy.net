using System.Collections.Frozen;
using System.Globalization;

namespace RecDiffy.Fix;

/// <summary>Reads FIX tag=value text strictly. A frame that fails any check is rejected with its reason, never passed on.</summary>
public static class FixReader
{
    // Length field -> the data field whose value has exactly that many characters and may hold the delimiter.
    private static readonly FrozenDictionary<int, int> DataFields = new Dictionary<int, int>
    {
        [90] = 91, [93] = 89, [95] = 96, [212] = 213, [348] = 349, [350] = 351, [352] = 353,
        [354] = 355, [356] = 357, [358] = 359, [445] = 446, [618] = 619, [621] = 622,
    }.ToFrozenDictionary();

    // Count field -> the fields that repeat once per entry. The first member opens each entry.
    private static readonly FrozenDictionary<int, int[]> Groups = new Dictionary<int, int[]>
    {
        [78] = [79, 80, 467, 539, 524, 525, 538],
        [453] = [448, 447, 452],
        [552] = [54, 37, 11, 1, 453, 448, 447, 452, 528, 581],
    }.ToFrozenDictionary();

    /// <summary>Splits text into frames by BodyLength and reads each. Whitespace and line breaks between frames are ignored.</summary>
    public static IReadOnlyList<FixReading> Read(string text, FixOptions options)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);
        return [.. Frames(text, options.Delimiter).Select(frame => frame.Error is { } error ? Reject(new FixRejected(frame.Text, error), options) : ReadMessage(frame.Text, options))];
    }

    /// <summary>Reads one complete message.</summary>
    public static FixReading ReadMessage(string text, FixOptions options)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);
        return Check(text, options.Delimiter) switch
        {
            { Error: { } error } => Reject(new FixRejected(text, error), options),
            var scan => new FixAccepted(new FixMessage(scan.Fields, text)),
        };
    }

    internal static FixRejected Reject(FixRejected rejected, FixOptions options) =>
        options.ThrowOnRejected ? throw new FixRejectedException(rejected) : rejected;

    private static Scan Check(string text, char delimiter)
    {
        var scan = Fields(text, delimiter);
        if (scan.Error is not null)
            return scan;
        var fields = scan.Fields;
        return fields switch
        {
            { Count: < 4 } => Scan.Fail("too few fields"),
            _ when text.Any(c => c > 127) => Scan.Fail("non-ASCII character"),
            _ when fields[0].Tag != 8 || fields[1].Tag != 9 || fields[2].Tag != 35 => Scan.Fail("header must start 8, 9, 35"),
            _ when fields[^1].Tag != 10 => Scan.Fail("CheckSum (10) must be the last field"),
            _ when fields.Take(fields.Count - 1).Any(field => field.Tag == 10) => Scan.Fail("CheckSum (10) appears before the end"),
            _ when BodyLength(text, delimiter) is var (declared, actual) && declared != actual => Scan.Fail($"BodyLength (9) says {declared.ToInvariant()} but the body has {actual.ToInvariant()}"),
            _ when CheckSum(text, delimiter) is var (stated, computed) && !string.Equals(stated, computed, StringComparison.Ordinal) => Scan.Fail($"CheckSum (10) is {stated} but the message sums to {computed}"),
            _ when Duplicate(fields) is { } tag => Scan.Fail($"tag {tag.ToInvariant()} repeats outside a repeating group"),
            _ when GroupMismatch(fields) is { } problem => Scan.Fail(problem),
            _ => scan,
        };
    }

    private static Scan Fields(string text, char delimiter)
    {
        var fields = new List<FixField>();
        var position = 0;
        var pendingLength = (Tag: 0, Length: 0);
        while (position < text.Length)
        {
            var equals = text.IndexOf('=', position);
            if (equals <= position || !int.TryParse(text.AsSpan(position, equals - position), NumberStyles.None, CultureInfo.InvariantCulture, out var tag))
                return Scan.Fail($"malformed field at character {position.ToInvariant()}");
            var start = equals + 1;
            var end = DataFields.TryGetValue(pendingLength.Tag, out var dataTag) && dataTag == tag
                ? start + pendingLength.Length
                : text.IndexOf(delimiter, start);
            if (end < 0 || end >= text.Length || text[end] != delimiter)
                return Scan.Fail($"field {tag.ToInvariant()} is not closed by the delimiter");
            var value = text[start..end];
            fields.Add(new FixField(tag, value));
            pendingLength = DataFields.ContainsKey(tag) && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var length) ? (tag, length) : (0, 0);
            position = end + 1;
        }

        return new Scan(fields, null);
    }

    private static (int Declared, int Actual) BodyLength(string text, char delimiter)
    {
        var afterLength = text.IndexOf(delimiter, text.IndexOf(delimiter) + 1) + 1;
        var checksumField = text.LastIndexOf(delimiter + "10=", StringComparison.Ordinal) + 1;
        var declared = int.TryParse(Value(text, 9, delimiter), NumberStyles.None, CultureInfo.InvariantCulture, out var length) ? length : -1;
        return (declared, checksumField - afterLength);
    }

    private static (string Stated, string Computed) CheckSum(string text, char delimiter)
    {
        var checksumField = text.LastIndexOf(delimiter + "10=", StringComparison.Ordinal) + 1;
        var sum = text.AsSpan(0, checksumField).ToArray().Sum(c => c == delimiter ? 1 : c);
        return (Value(text, 10, delimiter), (sum % 256).ToInvariant("000"));
    }

    private static string Value(string text, int tag, char delimiter)
    {
        var marker = tag == 8 ? "8=" : delimiter + tag.ToInvariant() + "=";
        var start = text.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        return text[start..text.IndexOf(delimiter, start)];
    }

    private static int? Duplicate(IReadOnlyList<FixField> fields)
    {
        var repeating = Groups.Where(group => fields.Any(field => field.Tag == group.Key)).SelectMany(group => group.Value).ToFrozenSet();
        return fields
            .Where(field => !repeating.Contains(field.Tag))
            .GroupBy(field => field.Tag)
            .Where(group => group.Count() > 1)
            .Select(group => (int?)group.Key)
            .FirstOrDefault();
    }

    private static string? GroupMismatch(IReadOnlyList<FixField> fields) =>
        Groups
            .Where(group => fields.Any(field => field.Tag == group.Key))
            .Select(group => (
                Count: group.Key,
                Declared: int.TryParse(fields.First(field => field.Tag == group.Key).Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : -1,
                Entries: fields.Count(field => field.Tag == group.Value[0])))
            .Where(group => group.Declared != group.Entries)
            .Select(group => $"group {group.Count.ToInvariant()} declares {group.Declared.ToInvariant()} entries but has {group.Entries.ToInvariant()}")
            .FirstOrDefault();

    private static IReadOnlyList<(string Text, string? Error)> Frames(string text, char delimiter)
    {
        var frames = new List<(string Text, string? Error)>();
        var position = 0;
        while (position < text.Length)
        {
            if (char.IsWhiteSpace(text[position]))
            {
                position++;
                continue;
            }

            var frame = Frame(text, position, delimiter);
            frames.Add((frame.Text, frame.Error));
            position = frame.Next;
        }

        return frames;
    }

    private static (string Text, string? Error, int Next) Frame(string text, int start, char delimiter)
    {
        var nextFrame = text.IndexOf("8=FIX", start + 1, StringComparison.Ordinal) is var found and >= 0 ? found : text.Length;
        var skip = (string error) => (text[start..nextFrame].TrimEnd(), (string?)error, nextFrame);
        if (!text.AsSpan(start).StartsWith("8=", StringComparison.Ordinal))
            return skip("frame does not start with BeginString (8)");
        var first = text.IndexOf(delimiter, start);
        if (first < 0 || !text.AsSpan(first + 1).StartsWith("9=", StringComparison.Ordinal))
            return skip("BodyLength (9) must follow BeginString (8)");
        var second = text.IndexOf(delimiter, first + 1);
        if (second < 0 || !int.TryParse(text.AsSpan(first + 3, second - first - 3), NumberStyles.None, CultureInfo.InvariantCulture, out var length))
            return skip("BodyLength (9) is not a number");
        var checksum = second + 1 + length;
        var end = checksum + 7;
        return end <= text.Length && text.AsSpan(checksum).StartsWith("10=", StringComparison.Ordinal) && text[end - 1] == delimiter
            ? (text[start..end], null, end)
            : skip("frame is truncated, or BodyLength (9) does not reach CheckSum (10)");
    }

    private sealed record Scan(IReadOnlyList<FixField> Fields, string? Error)
    {
        public static Scan Fail(string error) => new([], error);
    }
}
