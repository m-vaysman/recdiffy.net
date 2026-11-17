using System.Collections.Immutable;
using System.Data;
using System.Globalization;
using RecDiffy.Extensions;
using RecDiffy.Results;

namespace RecDiffy.BrowserDemo.Demo;

/// <summary>The result as a grid: status, the chosen columns from each side, and what differs. Exceptions come first.</summary>
/// <param name="Html">The grid, rendered by <see cref="DataGrid"/>.</param>
/// <param name="Shown">How many entries the grid shows.</param>
/// <param name="Total">How many entries have the chosen status.</param>
public sealed record ResultGrid(string Html, int Shown, int Total)
{
    private static readonly CultureInfo Reading = CultureInfo.GetCultureInfo("en-US");

    public static ResultGrid Build(IReconciliationResult result, RecSetup setup, string? status, int limit)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(setup);

        var pairs = setup.CompleteKeys.Concat(setup.CompleteCompares).ToList();
        var numbers = setup.CompleteCompares.Where(pair => pair.Number).ToList();
        var columns = pairs.Select(pair => pair.Left).Distinct(StringComparer.Ordinal)
            .Select(column => new GridColumn($"Left.{column}", column, Left: true, numbers.Exists(pair => pair.Left == column)))
            .Concat(pairs.Select(pair => pair.Right).Distinct(StringComparer.Ordinal)
                .Select(column => new GridColumn($"Right.{column}", column, Left: false, numbers.Exists(pair => pair.Right == column))))
            .ToList();
        var labels = setup.CompleteKeys.Select((pair, index) => (Field: ProfileJson.KeyField(index), pair.Left))
            .Concat(setup.CompleteCompares.Select((pair, index) => (Field: ProfileJson.CompareField(index), pair.Left)))
            .ToDictionary(label => label.Field, label => label.Left, StringComparer.Ordinal);

        var entries = result.Entries
            .Select(entry => (Entry: entry, Status: EntryStatus.Of(entry)))
            .Where(entry => status is null || string.Equals(entry.Status, status, StringComparison.Ordinal))
            .OrderBy(entry => EntryStatus.Rank(entry.Status))
            .ToList();

        var cells = entries.Take(limit)
            .Select(entry => columns.ConvertAll(column => Raw(result, column.Left ? entry.Entry.Left : entry.Entry.Right, column.Source)))
            .ToList();
        var numeric = columns.Select((column, index) => column.Number && cells.TrueForAll(row => row[index] is null || Number(row[index]!) is not null)).ToList();

        using var table = new DataTable("Result");
        table.Columns.AddRange(
        [
            new DataColumn("Status", typeof(string)),
            .. columns.Select((column, index) => new DataColumn(column.Name, numeric[index] ? typeof(decimal) : typeof(string))),
            new DataColumn("Diff", typeof(string)),
        ]);

        foreach (var (entry, row) in entries.Zip(cells))
        {
            table.Rows.Add(
            [
                entry.Status,
                .. row.Select((cell, index) => Cell(cell, numeric[index])),
                Diff(result, entry.Entry, labels),
            ]);
        }

        return new ResultGrid(table.ToDataGridHtml(""), cells.Count, entries.Count);
    }

    private static string? Raw(IReconciliationResult result, ImmutableArray<SourceRef> rows, string column) =>
        rows.IsEmpty ? null : string.Join(" + ", rows.Select(row => result.Raw(row, column) ?? ""));

    private static object Cell(string? text, bool numeric) => (text, numeric) switch
    {
        (null, _) => DBNull.Value,
        (_, true) => Number(text)!.Value,
        _ => text,
    };

    private static decimal? Number(string text) =>
        decimal.TryParse(text, NumberStyles.Number, Reading, out var number) ? number : null;

    private static object Diff(IReconciliationResult result, ResultEntry entry, IReadOnlyDictionary<string, string> labels)
    {
        var parts = entry.Diffs
            .Where(diff => diff.Outcome is FieldOutcome.Break or FieldOutcome.WithinTolerance)
            .Select(diff => $"{labels.GetValueOrDefault(diff.Field.Name, diff.Field.Name)} {Value(result, diff.Left)} → {Value(result, diff.Right)}")
            .ToList();

        return parts.Count == 0 ? DBNull.Value : string.Join(" · ", parts);
    }

    private static string Value(IReconciliationResult result, CanonicalValue value) =>
        value.IsNull ? "(blank)" : result.Format(value);

    private sealed record GridColumn(string Name, string Source, bool Left, bool Number);
}
