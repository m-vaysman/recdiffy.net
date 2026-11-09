using System.Globalization;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn;

public static class SourceTables
{
    public static SourceRead Build(ExcelRangePick range, IReadOnlyList<IReadOnlyList<SourceCell>> rows)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(rows);

        var location = $"[{range.Workbook}]{range.Sheet}";
        if (FirstError(rows) is { } error)
            return new SourceRead.Rejected(
                $"Cell {CellName(range.FirstRow + error.Row, range.FirstColumn + error.Column)} of {location} holds {error.Text}. RecDiffy does not read error cells: fix or remove the error in the workbook and pick the range again.");

        if (rows.Count == 0)
            return new SourceRead.Rejected($"The range picked on {location} is empty. Pick the data including its header row.");

        var headers = rows[0].Select(cell => HeaderText(cell.Value)).ToArray();
        if (HeaderProblems(headers) is { Length: > 0 } problems)
            return new SourceRead.Rejected($"The header row of {location} ({range.Address}) can't be used: {string.Join("; ", problems)}.");

        var kept = KeptRows(rows);
        var table = new SourceTable(headers, [.. kept.Select(index => (IReadOnlyList<object?>)[.. rows[index].Select(cell => cell.Value)])])
        {
            SheetRows = [.. kept.Select(index => range.FirstRow + index + 1)],
        };
        return new SourceRead.Read(range, table, SourcePreview.Of(table));
    }

    public static IReadOnlyList<int> KeptRows(IReadOnlyList<IReadOnlyList<SourceCell>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return [.. Enumerable.Range(1, Math.Max(rows.Count - 1, 0)).Where(index => HasValue(rows[index]))];
    }

    private static bool HasValue(IReadOnlyList<SourceCell> row) => row.Any(cell => !IsBlank(cell.Value));

    private static bool IsBlank(object? value) => value switch
    {
        null => true,
        string text => string.IsNullOrWhiteSpace(text),
        _ => false,
    };

    public static string CellName(int row, int column) => ColumnName(column) + (row + 1).ToString(CultureInfo.InvariantCulture);

    private static (int Row, int Column, string Text)? FirstError(IReadOnlyList<IReadOnlyList<SourceCell>> rows) =>
        rows.SelectMany((row, rowIndex) => row.Select((cell, columnIndex) => (Row: rowIndex, Column: columnIndex, cell.Error)))
            .Where(found => found.Error is not null)
            .Select(found => ((int Row, int Column, string Text)?)(found.Row, found.Column, found.Error!))
            .FirstOrDefault();

    private static string HeaderText(object? value) => value switch
    {
        null => string.Empty,
        string text => text.Trim(),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static string[] HeaderProblems(string[] headers)
    {
        var unnamed = headers.Select((name, index) => (name, index)).Where(pair => pair.name.Length == 0).Select(pair => ColumnPosition(pair.index)).ToArray();
        var repeated = headers.Where(name => name.Length > 0).GroupBy(name => name, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => $"'{group.Key}'").ToArray();
        return
        [
            .. unnamed.Length > 0 ? [$"column(s) {string.Join(", ", unnamed)} have no name"] : Array.Empty<string>(),
            .. repeated.Length > 0 ? [$"{string.Join(", ", repeated)} appear more than once"] : Array.Empty<string>(),
        ];
    }

    private static string ColumnPosition(int index) => (index + 1).ToString(CultureInfo.InvariantCulture);

    private static string ColumnName(int index) =>
        index < 26
            ? ((char)('A' + index)).ToString()
            : ColumnName((index / 26) - 1) + (char)('A' + (index % 26));
}
