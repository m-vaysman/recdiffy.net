using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn;

public sealed record SheetBlock(int Page, int FirstRow, IReadOnlyList<SheetRow> Rows, int ResultsAfter)
{
    public int Width => Math.Max(1, Rows.Max(row => row.Cells.Count));
}

public sealed record DateRun(int Column, int FirstRow, int LastRow, bool HasTime);

public sealed record RowRun(int FirstRow, int LastRow);

public static class ExcelBlocks
{
    public const int BlockRows = 5_000;

    public const string DateFormat = "yyyy-mm-dd";

    public const string DateTimeFormat = "yyyy-mm-dd hh:mm";

    public static IReadOnlyList<double> ColumnWidths { get; } = [26, 18, 24, 18, 24, 9, 80];

    public static IReadOnlyList<SheetBlock> Split(ResultSheet sheet, int blockRows)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentOutOfRangeException.ThrowIfLessThan(blockRows, 1);
        IReadOnlyList<(int Page, int FirstRow, IReadOnlyList<SheetRow> Rows)> chunks =
        [
            .. sheet.Pages.SelectMany((page, index) => page.Rows
                .Chunk(blockRows)
                .Select((rows, chunk) => (index, chunk * blockRows, (IReadOnlyList<SheetRow>)rows))),
        ];
        var resultsAfter = RunningTotals([.. chunks.Select(chunk => chunk.Rows.Count(row => row.Kind is SheetRowKind.Result))]);
        return [.. chunks.Select((chunk, index) => new SheetBlock(chunk.Page, chunk.FirstRow, chunk.Rows, resultsAfter[index]))];
    }

    public static object[,] Values(SheetBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        var values = new object[block.Rows.Count, block.Width];
        for (var row = 0; row < block.Rows.Count; row++)
        {
            var cells = block.Rows[row].Cells;
            for (var column = 0; column < block.Width; column++)
                values[row, column] = ExcelOutputConversions.ToExcelValue(column < cells.Count ? cells[column] : SheetValue.Empty);
        }

        return values;
    }

    public static IReadOnlyList<DateRun> DateRuns(SheetPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var width = page.Rows.Count == 0 ? 0 : page.Rows.Max(row => row.Cells.Count);
        return [.. Enumerable.Range(0, width).SelectMany(column => DateRunsIn(page.Rows, column))];
    }

    public static IReadOnlyList<RowRun> HeadingRuns(SheetPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var runs = new List<RowRun>();
        foreach (var index in Enumerable.Range(0, page.Rows.Count).Where(index => page.Rows[index].Kind is SheetRowKind.Heading))
        {
            if (runs is [.., { } last] && last.LastRow == index - 1)
                runs[^1] = last with { LastRow = index };
            else
                runs.Add(new RowRun(index, index));
        }

        return runs;
    }

    private static int[] RunningTotals(IReadOnlyList<int> counts)
    {
        var totals = new int[counts.Count];
        var sum = 0;
        for (var index = 0; index < counts.Count; index++)
        {
            sum += counts[index];
            totals[index] = sum;
        }

        return totals;
    }

    private static IEnumerable<DateRun> DateRunsIn(IReadOnlyList<SheetRow> rows, int column)
    {
        DateRun? open = null;
        for (var row = 0; row < rows.Count; row++)
        {
            var cell = column < rows[row].Cells.Count ? rows[row].Cells[column] : SheetValue.Empty;
            (open, var closed) = (open, cell) switch
            {
                ({ } run, SheetValue.Date date) when run.HasTime == date.HasTime => (run with { LastRow = row }, (DateRun?)null),
                (var run, SheetValue.Date date) => (new DateRun(column, row, row, date.HasTime), run),
                (var run, SheetValue.Number) => ((DateRun?)null, run),
                (var run, _) => (run, (DateRun?)null),
            };
            if (closed is { } done)
                yield return done;
        }

        if (open is { } last)
            yield return last;
    }
}
