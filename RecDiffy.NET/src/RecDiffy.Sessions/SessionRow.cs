using System.Collections.Immutable;

namespace RecDiffy.Sessions;

/// <summary>One pushed row: its source cells by column, as text.</summary>
public sealed record SessionRow(ImmutableSortedDictionary<string, string?> Cells)
{
    public static SessionRow Of(IReadOnlyDictionary<string, string?> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        return new SessionRow(cells.ToImmutableSortedDictionary(StringComparer.Ordinal));
    }

    internal static RecordSource Source(string side, IReadOnlyList<SessionRow> rows) =>
        RecordSources.FromObjects(side, rows, Map(rows));

    private static SourceMap<SessionRow> Map(IReadOnlyList<SessionRow> rows) =>
        rows
            .SelectMany(row => row.Cells.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Aggregate(new SourceMap<SessionRow>(), (map, column) => map.Text(column, row => row.Cells.GetValueOrDefault(column)));
}
