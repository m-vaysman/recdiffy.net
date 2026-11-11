using System.Collections.Immutable;

namespace RecDiffy;

public sealed record ReferenceDataSet(
    string Id,
    DateOnly AsOf,
    string ContentHash,
    IReadOnlyDictionary<string, string> LeiToCode,
    IReadOnlyDictionary<string, string> AliasToCode,
    IReadOnlyDictionary<string, string> DateFormatByCode)
{
    public static ReferenceDataSet Empty { get; } = new(
        "empty",
        default,
        "",
        ImmutableDictionary.Create<string, string>(StringComparer.Ordinal),
        ImmutableDictionary.Create<string, string>(StringComparer.Ordinal),
        ImmutableDictionary.Create<string, string>(StringComparer.Ordinal));

    public static ReferenceDataSet Create(
        string id,
        DateOnly asOf,
        string contentHash,
        IReadOnlyDictionary<string, string> leiToCode,
        IReadOnlyDictionary<string, string> aliasToCode,
        IReadOnlyDictionary<string, string> dateFormatByCode)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(contentHash);
        ArgumentNullException.ThrowIfNull(leiToCode);
        ArgumentNullException.ThrowIfNull(aliasToCode);
        ArgumentNullException.ThrowIfNull(dateFormatByCode);
        return new(id, asOf, contentHash, Freeze(leiToCode), Freeze(aliasToCode), Freeze(dateFormatByCode));
    }

    private static IReadOnlyDictionary<string, string> Freeze(IReadOnlyDictionary<string, string> source) =>
        source.ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}

internal sealed record InstrumentMaster(
    IReadOnlyDictionary<(string Scheme, string Value), string> Map,
    IReadOnlySet<(string Scheme, string Value)> Conflicts)
{
    public static InstrumentMaster Empty { get; } = new(
        new Dictionary<(string Scheme, string Value), string>(),
        new HashSet<(string Scheme, string Value)>());

    public static InstrumentMaster FromRows(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, IReadOnlyList<string> columns)
    {
        var schemes = columns.Select(SchemeOf).ToArray();
        var map = new Dictionary<(string Scheme, string Value), string>();
        var conflicts = new HashSet<(string Scheme, string Value)>();
        foreach (var row in rows)
        {
            if (First(row, columns) is not { } canonical)
                continue;
            for (var index = 0; index < columns.Count; index++)
            {
                if (Text(row, columns[index]) is { } raw)
                    Record(map, conflicts, (schemes[index], raw), canonical);
            }
        }

        return map.Count is 0 && conflicts.Count is 0 ? Empty : new(map, conflicts);
    }

    public InstrumentMaster Add(string scheme, string value, string canonical) =>
        (Conflicts.Contains((scheme, value)), Map.TryGetValue((scheme, value), out var existing) ? existing : null, canonical) switch
        {
            (true, _, _) => this,
            (false, null, _) => this with { Map = Map.Append((scheme, value), canonical) },
            (false, var current, _) when current == canonical => this,
            _ => this with
            {
                Map = Map.Where(pair => pair.Key != (scheme, value)).ToDictionary(pair => pair.Key, pair => pair.Value),
                Conflicts = Conflicts.Append((scheme, value)),
            },
        };

    private static void Record(
        Dictionary<(string Scheme, string Value), string> map,
        HashSet<(string Scheme, string Value)> conflicts,
        (string Scheme, string Value) key,
        string canonical)
    {
        if (conflicts.Contains(key))
            return;
        if (map.TryGetValue(key, out var existing) is false)
        {
            map[key] = canonical;
            return;
        }

        if (existing != canonical)
        {
            map.Remove(key);
            conflicts.Add(key);
        }
    }

    private static string SchemeOf(string column) => column.ToUpperInvariant() switch
    {
        "TICKER" => "TICKER",
        _ => column.ToUpperInvariant(),
    };

    private static string? Text(IReadOnlyDictionary<string, object?> row, string column) =>
        row.TryGetValue(column, out var value) ? value?.ToString()?.Trim() : null;

    private static string? First(IReadOnlyDictionary<string, object?> row, IReadOnlyList<string> columns)
    {
        for (var index = 0; index < columns.Count; index++)
        {
            if (Text(row, columns[index]) is { } text)
                return text;
        }

        return null;
    }
}

internal static class DictionaryAdds
{
    public static IReadOnlyDictionary<(string Scheme, string Value), string> Append(
        this IReadOnlyDictionary<(string Scheme, string Value), string> source,
        (string Scheme, string Value) key,
        string value) =>
        new Dictionary<(string Scheme, string Value), string>(source) { [key] = value };

    public static IReadOnlySet<(string Scheme, string Value)> Append(
        this IReadOnlySet<(string Scheme, string Value)> source,
        (string Scheme, string Value) key) =>
        new HashSet<(string Scheme, string Value)>(source) { key };
}
