using System.Collections.Immutable;

namespace RecDiffy;

/// <summary>One pushed row: its source cells by column, as text.</summary>
public sealed record RecRow(ImmutableSortedDictionary<string, string?> Cells)
{
    public static RecRow Of(IReadOnlyDictionary<string, string?> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        return new RecRow(cells.ToImmutableSortedDictionary(StringComparer.Ordinal));
    }

    internal static RecordSource Source(string side, IReadOnlyList<RecRow> rows) =>
        RecordSources.FromObjects(side, rows, Map(rows));

    private static SourceMap<RecRow> Map(IReadOnlyList<RecRow> rows) =>
        rows
            .SelectMany(row => row.Cells.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Aggregate(new SourceMap<RecRow>(), (map, column) => map.Text(column, row => row.Cells.GetValueOrDefault(column)));
}
