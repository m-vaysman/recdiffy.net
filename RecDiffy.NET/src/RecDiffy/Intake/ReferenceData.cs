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
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal));

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
        return new(id, asOf, contentHash, leiToCode, aliasToCode, dateFormatByCode);
    }
}

internal sealed record InstrumentMaster(
    IReadOnlyDictionary<(string Scheme, string Value), string> Map,
    IReadOnlySet<(string Scheme, string Value)> Conflicts)
{
    public static InstrumentMaster Empty { get; } = new(
        new Dictionary<(string Scheme, string Value), string>(),
        new HashSet<(string Scheme, string Value)>());

    public static InstrumentMaster FromRows(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, IReadOnlyList<string> columns) =>
        columns
            .SelectMany(column => rows.Select(row => (Scheme: SchemeOf(column), Raw: Text(row, column), Canonical: First(row, columns))))
            .Where(entry => entry.Raw is not null && entry.Canonical is not null)
            .Aggregate(Empty, (master, entry) => master.Add(entry.Scheme, entry.Raw!, entry.Canonical!));

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

    private static string SchemeOf(string column) => column.ToUpperInvariant() switch
    {
        "TICKER" => "TICKER",
        _ => column.ToUpperInvariant(),
    };

    private static string? Text(IReadOnlyDictionary<string, object?> row, string column) =>
        row.TryGetValue(column, out var value) ? value?.ToString()?.Trim() : null;

    private static string? First(IReadOnlyDictionary<string, object?> row, IReadOnlyList<string> columns) =>
        columns.Select(column => Text(row, column)).FirstOrDefault(text => text is not null);
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
