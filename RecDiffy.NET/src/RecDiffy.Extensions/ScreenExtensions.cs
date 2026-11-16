using System.Data;
using RecDiffy.Results;

namespace RecDiffy.Extensions;

/// <summary>
/// Ready-made screens of a <see cref="RecResult{TLeft, TRight}"/>: the matches, the breaks by key, and control totals.
/// Each is a <see cref="DataTable"/> (<c>ToXxxTable</c>) or a <see cref="DataGrid"/> HTML table (<c>ToXxxHtml</c>).
/// The two side-by-side screens leave ambiguous rows out, as <see cref="IReconciliationResult.BuildSideBySide"/> does.
/// </summary>
public static class ScreenExtensions
{
    /// <summary>
    /// Every row with its status, as <see cref="RecResultExtensions.ToReconciliationTable{TLeft, TRight}"/> returns it,
    /// without the <c>Group</c> column and with <c>Diffs</c> named <c>Diff</c>.
    /// </summary>
    public static DataTable ToMatchTable<TLeft, TRight>(this RecResult<TLeft, TRight> result, string tableName = "Match")
    {
        ArgumentNullException.ThrowIfNull(result);

        var table = result.ToReconciliationTable(tableName);
        table.Columns.Remove("Group");
        if (table.Columns["Diffs"] is { } diffs)
            diffs.ColumnName = "Diff";
        return table;
    }

    /// <summary>
    /// One row per key: the <paramref name="by"/> fields, then <c>{quantity} left</c>, <c>{quantity} right</c>,
    /// <c>{quantity} diff</c> and <c>{value} diff</c>. A measure missing from a line is <see cref="DBNull"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">There are several <paramref name="by"/> fields and a key value contains <c>|</c>,
    /// so the side-by-side row label cannot be split back into its keys.</exception>
    public static DataTable ToBreakTable<TLeft, TRight>(
        this RecResult<TLeft, TRight> result,
        IReadOnlyList<string> by,
        string quantity,
        string value,
        string tableName = "Breaks")
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(by);
        ArgumentException.ThrowIfNullOrWhiteSpace(quantity);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (string.Equals(quantity, value, StringComparison.Ordinal))
            throw new ArgumentException("The quantity and the value must be different fields.", nameof(value));

        var view = result.Result.BuildSideBySide(new SideBySideSpec(by, [], [new MeasureSpec(quantity), new MeasureSpec(value)], null));

        var columns = KeyColumns(by).Concat(
        [
            ($"{quantity} left", typeof(decimal)),
            ($"{quantity} right", typeof(decimal)),
            ($"{quantity} diff", typeof(decimal)),
            ($"{value} diff", typeof(decimal)),
        ]);

        var rows = view.Lines.Select(line => (object?[])
        [
            .. KeyValues(line.Row, by.Count),
            Measure(line, quantity, cell => cell.Left),
            Measure(line, quantity, cell => cell.Right),
            Measure(line, quantity, cell => cell.Delta),
            Measure(line, value, cell => cell.Delta),
        ]);

        return Tables.Build(tableName, columns, rows);
    }

    /// <summary>
    /// One row per key: the <paramref name="by"/> fields, then <c>Left</c>, <c>Right</c>, <c>Diff</c>, <c>Left only</c>,
    /// <c>Right only</c> and <c>Matched diff</c> of <paramref name="measure"/>, followed by a <c>Total</c> row over every key.
    /// </summary>
    /// <exception cref="InvalidOperationException">There are several <paramref name="by"/> fields and a key value contains <c>|</c>,
    /// so the side-by-side row label cannot be split back into its keys.</exception>
    public static DataTable ToControlTotalsTable<TLeft, TRight>(
        this RecResult<TLeft, TRight> result,
        IReadOnlyList<string> by,
        string measure,
        string tableName = "ControlTotals")
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(by);
        ArgumentException.ThrowIfNullOrWhiteSpace(measure);
        if (by.Count == 0)
            throw new ArgumentException("Give at least one field to total by.", nameof(by));

        MeasureSpec[] measures = [new MeasureSpec(measure)];
        var perKey = result.Result.BuildSideBySide(new SideBySideSpec(by, [], measures, null));
        var overall = result.Result.BuildSideBySide(new SideBySideSpec([], [], measures, null));

        var columns = KeyColumns(by).Concat(
        [
            ("Left", typeof(decimal)),
            ("Right", typeof(decimal)),
            ("Diff", typeof(decimal)),
            ("Left only", typeof(decimal)),
            ("Right only", typeof(decimal)),
            ("Matched diff", typeof(decimal)),
        ]);

        string[] totalKeys = ["Total", .. Enumerable.Repeat("", by.Count - 1)];
        var rows = perKey.Lines
            .Select(line => Totals(KeyValues(line.Row, by.Count), line, measure))
            .Concat(overall.Lines.Select(line => Totals(totalKeys, line, measure)));

        return Tables.Build(tableName, columns, rows);
    }

    /// <summary>The <see cref="ToMatchTable{TLeft, TRight}"/> screen as a <see cref="DataGrid"/>. Add <see cref="DataGrid.Css"/> to the page.</summary>
    public static string ToMatchHtml<TLeft, TRight>(this RecResult<TLeft, TRight> result, string caption = "Match")
    {
        using var table = result.ToMatchTable(caption);
        return table.ToDataGridHtml(caption);
    }

    /// <summary>The <see cref="ToBreakTable{TLeft, TRight}"/> screen as a <see cref="DataGrid"/>. Add <see cref="DataGrid.Css"/> to the page.</summary>
    public static string ToBreakHtml<TLeft, TRight>(
        this RecResult<TLeft, TRight> result,
        IReadOnlyList<string> by,
        string quantity,
        string value,
        string caption = "Breaks")
    {
        using var table = result.ToBreakTable(by, quantity, value, caption);
        return table.ToDataGridHtml(caption);
    }

    /// <summary>The <see cref="ToControlTotalsTable{TLeft, TRight}"/> screen as a <see cref="DataGrid"/>. Add <see cref="DataGrid.Css"/> to the page.</summary>
    public static string ToControlTotalsHtml<TLeft, TRight>(
        this RecResult<TLeft, TRight> result,
        IReadOnlyList<string> by,
        string measure,
        string caption = "ControlTotals")
    {
        using var table = result.ToControlTotalsTable(by, measure, caption);
        return table.ToDataGridHtml(caption);
    }

    private static IEnumerable<(string Name, Type Type)> KeyColumns(IReadOnlyList<string> by) =>
        by.Select(field => (Name: field, Type: typeof(string)));

    // The engine joins the key values of a side-by-side line with '|'. One key needs no split; with several, a value
    // that contains '|' gives more parts than keys, and there is no way to tell which value it belongs to.
    private static string[] KeyValues(string label, int count) => (count, label.Split('|')) switch
    {
        (0, _) => [],
        (1, _) => [label],
        (_, var parts) when parts.Length == count => parts,
        _ => throw new InvalidOperationException(
            $"The side-by-side row '{label}' cannot be split into {count.ToInvariant()} keys, because a key value contains '|'."),
    };

    private static object Measure(SideBySideLine line, string measure, Func<SideBySideCell, decimal> read) =>
        line.Cells.TryGetValue(measure, out var cell) ? read(cell) : DBNull.Value;

    private static object?[] Totals(string[] keys, SideBySideLine line, string measure) =>
    [
        .. keys,
        Measure(line, measure, cell => cell.Left),
        Measure(line, measure, cell => cell.Right),
        Measure(line, measure, cell => cell.Delta),
        Measure(line, measure, cell => cell.LeftOnly),
        Measure(line, measure, cell => cell.RightOnly),
        Measure(line, measure, cell => cell.MatchedDelta),
    ];
}
