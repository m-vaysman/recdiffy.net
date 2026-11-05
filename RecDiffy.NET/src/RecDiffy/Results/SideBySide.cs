namespace RecDiffy.Results;

public sealed record MeasureSpec(string Field);

public sealed record FilterSpec(string Field, string EqualsValue);

public sealed record SideBySideSpec(
    IReadOnlyList<string> Rows,
    IReadOnlyList<string> Columns,
    IReadOnlyList<MeasureSpec> Measures,
    FilterSpec? Filter);

public readonly record struct SideBySideCell(decimal Left, decimal Right, decimal Delta, decimal LeftOnly, decimal RightOnly, decimal MatchedDelta);

public sealed record SideBySideLine(string Row, string Column, IReadOnlyDictionary<string, SideBySideCell> Cells);

public sealed class SideBySideView(IReadOnlyList<SideBySideLine> lines)
{
    public IReadOnlyList<SideBySideLine> Lines { get; } = lines;
}

internal static class SideBySideBuilder
{
    public static SideBySideView Build(ReconciliationResult result, SideBySideSpec spec)
    {
        var totals = new Dictionary<(string Row, string Column, string Measure), Accumulator>();
        foreach (var row in result.Rows)
        {
            if (row.Disposition is Disposition.Suppressed or Disposition.Quarantined or Disposition.Ambiguous or Disposition.SearchIncomplete)
                continue;
            var canonical = result.Canonical(row.Row);
            if (spec.Filter is { } filter && canonical.Text(filter.Field) != filter.EqualsValue)
                continue;
            var rowKey = Label(canonical, spec.Rows);
            var columnKey = Label(canonical, spec.Columns);
            foreach (var measure in spec.Measures)
            {
                var amount = canonical.IsNull(measure.Field) ? 0m : canonical[measure.Field].Number;
                var key = (rowKey, columnKey, measure.Field);
                totals[key] = totals.TryGetValue(key, out var current) ? current.Add(row.Disposition, row.Row.Source, amount) : Accumulator.Start(row.Disposition, row.Row.Source, amount);
            }
        }

        var lines = totals
            .GroupBy(pair => (pair.Key.Row, pair.Key.Column))
            .OrderBy(group => group.Key.Row, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Column, StringComparer.Ordinal)
            .Select(group => new SideBySideLine(
                group.Key.Row,
                group.Key.Column,
                group.ToDictionary(pair => pair.Key.Measure, pair => pair.Value.Cell(), StringComparer.Ordinal)))
            .ToArray();
        return new SideBySideView(lines);
    }

    private static string Label(CanonicalRow row, IReadOnlyList<string> fields) =>
        fields.Count == 0 ? "All" : string.Join("|", fields.Select(field => row.Text(field)));

    private readonly record struct Accumulator(decimal Left, decimal Right, decimal LeftOnly, decimal RightOnly, decimal MatchedLeft, decimal MatchedRight)
    {
        public static Accumulator Start(Disposition disposition, string source, decimal amount) => new Accumulator(0m, 0m, 0m, 0m, 0m, 0m).Add(disposition, source, amount);

        public Accumulator Add(Disposition disposition, string source, decimal amount) => (disposition, source) switch
        {
            (Disposition.Matched, "left") => this with { Left = Left + amount, MatchedLeft = MatchedLeft + amount },
            (Disposition.Matched, _) => this with { Right = Right + amount, MatchedRight = MatchedRight + amount },
            (Disposition.LeftOnly, _) => this with { Left = Left + amount, LeftOnly = LeftOnly + amount },
            (Disposition.RightOnly, _) => this with { Right = Right + amount, RightOnly = RightOnly + amount },
            _ => this,
        };

        // LeftOnly is negated on the cell. RightOnly stays positive.
        public SideBySideCell Cell() => new(Left, Right, Right - Left, -LeftOnly, RightOnly, MatchedRight - MatchedLeft);
    }
}
