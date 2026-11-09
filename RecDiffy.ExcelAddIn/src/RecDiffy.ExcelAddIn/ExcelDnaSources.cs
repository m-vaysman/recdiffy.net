using System.IO;
using ExcelDna.Integration;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn;

internal sealed class ExcelDnaSources(Func<nint, Task> showAbove) : IExcelSources
{
    private readonly Func<nint, Task> _showAbove = showAbove ?? throw new ArgumentNullException(nameof(showAbove));

    private const int BlockRows = 5_000;
    private const int ReferenceType = 8;
    private const int DocumentPath = 2;
    private const int DateSystem = 20;
    private const int CellNumberFormat = 7;

    public Task<RangeChoice> PickRangeAsync(string workbookPath, string prompt, CancellationToken cancellationToken) =>
        OnExcel(() => Pick(workbookPath, prompt), cancellationToken);

    public Task<RangeChoice> PickOpenRangeAsync(string prompt, CancellationToken cancellationToken) =>
        OnExcel(() => PickOpen(prompt), cancellationToken);

    public Task<SourceRead> ReadAsync(ExcelRangePick range, IProgress<double> progress, CancellationToken cancellationToken) =>
        OnExcel(() => Read(range, progress, cancellationToken), cancellationToken);

    private static RangeChoice Pick(string workbookPath, string prompt)
    {
        var fileName = Path.GetFileName(workbookPath);
        var opened = OpenWorkbooks().FirstOrDefault(name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase));
        var ready = opened switch
        {
            null => Open(workbookPath),
            _ when SamePath(opened, workbookPath) => Activate(opened),
            _ => $"A different workbook named {fileName} is already open in Excel. Close it, then choose the file again.",
        };
        return ready is not null ? new RangeChoice.Rejected(ready) : Input(prompt);
    }

    private static RangeChoice PickOpen(string prompt) =>
        OpenWorkbooks().Count == 0
            ? new RangeChoice.Rejected("No workbook is open in Excel. Open one first, or use Excel file… to choose a file.")
            : Input(prompt);

    private static RangeChoice Input(string prompt)
    {
        XlCall.TryExcel(XlCall.xlfInput, out var picked, prompt, ReferenceType, "RecDiffy");
        return picked switch
        {
            ExcelReference reference when reference.InnerReferences.Count > 1 => new RangeChoice.Rejected("Pick one rectangular range, not several."),
            ExcelReference reference => new RangeChoice.Picked(ToPick(reference)),
            _ => new RangeChoice.Canceled(),
        };
    }

    private static SourceRead Read(ExcelRangePick range, IProgress<double> progress, CancellationToken cancellationToken)
    {
        try
        {
            var sheet = $"[{range.Workbook}]{range.Sheet}";
            var rows = new List<IReadOnlyList<SourceCell>>(range.Rows);
            for (var start = 0; start < range.Rows; start += BlockRows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(BlockRows, range.Rows - start);
                var block = new ExcelReference(
                    range.FirstRow + start,
                    range.FirstRow + start + count - 1,
                    range.FirstColumn,
                    range.FirstColumn + range.Columns - 1,
                    sheet).GetValue();
                rows.AddRange(Cells(block, count, range.Columns));
                progress.Report((double)(start + count) / range.Rows);
            }

            return SourceTables.Build(range, rows) switch
            {
                SourceRead.Read read => read with { Preview = TypedPreview(range, sheet, rows, read.Table) },
                var rejected => rejected,
            };
        }
        catch (XlCallException)
        {
            return new SourceRead.Rejected($"Excel couldn't read {range.Address} on [{range.Workbook}]{range.Sheet}. Check that the workbook is still open, then pick the range again.");
        }
    }

    private static SourcePreview TypedPreview(ExcelRangePick range, string sheet, IReadOnlyList<IReadOnlyList<SourceCell>> rows, SourceTable table)
    {
        var kept = SourceTables.KeptRows(rows);
        var uses1904 = Uses1904(range.Workbook);
        IReadOnlyList<IReadOnlyList<object?>> sample =
        [
            .. SourcePreview.SampleIndexes(kept.Count, SourcePreview.SampleSize, SourcePreview.Seed)
                .Select(index => (IReadOnlyList<object?>)
                [
                    .. Enumerable.Range(0, range.Columns).Select(column =>
                        Typed(sheet, range.FirstRow + kept[index], range.FirstColumn + column, rows[kept[index]][column].Value, uses1904)),
                ]),
        ];
        return new SourcePreview(table.Headers, sample, table.Rows.Count);
    }

    private static object? Typed(string sheet, int row, int column, object? value, bool uses1904) =>
        value is decimal or double
            ? ExcelDates.Typed(value, Shape(new ExcelReference(row, row, column, column, sheet)), uses1904)
            : value;

    private static CellShape Shape(ExcelReference cell) =>
        ExcelDates.ShapeOf(FormatCode(cell)) is var shape and not CellShape.Value ? shape : ExcelDates.ShapeOfFormat(NumberFormat(cell));

    private static string FormatCode(ExcelReference cell) =>
        XlCall.TryExcel(XlCall.xlfCell, out var code, "format", cell) is XlCall.XlReturn.XlReturnSuccess && code is string text ? text : "G";

    private static string NumberFormat(ExcelReference cell) =>
        XlCall.TryExcel(XlCall.xlfGetCell, out var format, CellNumberFormat, cell) is XlCall.XlReturn.XlReturnSuccess && format is string text ? text : "General";

    private static bool Uses1904(string workbook) =>
        XlCall.TryExcel(XlCall.xlfGetDocument, out var flag, DateSystem, workbook) is XlCall.XlReturn.XlReturnSuccess && flag is true;

    private static IEnumerable<IReadOnlyList<SourceCell>> Cells(object block, int rows, int columns) =>
        block switch
        {
            object[,] grid => Enumerable.Range(0, rows).Select(row => (IReadOnlyList<SourceCell>)[.. Enumerable.Range(0, columns).Select(column => ExcelCellConversions.ToSourceCell(grid[row, column]))]),
            _ => [[ExcelCellConversions.ToSourceCell(block)]],
        };

    private static IReadOnlyList<string> OpenWorkbooks() =>
        XlCall.TryExcel(XlCall.xlfDocuments, out var names) is XlCall.XlReturn.XlReturnSuccess && names is object[,] grid
            ? [.. grid.Cast<object>().OfType<string>()]
            : [];

    private static bool SamePath(string openName, string workbookPath) =>
        PathOf(openName) is { } path
        && string.Equals(path, Path.GetFullPath(workbookPath), StringComparison.OrdinalIgnoreCase);

    private static string? PathOf(string openName) =>
        XlCall.TryExcel(XlCall.xlfGetDocument, out var directory, DocumentPath, openName) is XlCall.XlReturn.XlReturnSuccess
        && directory is string { Length: > 0 } folder
            ? Path.GetFullPath(Path.Combine(folder, openName))
            : null;

    private static string? Open(string workbookPath) =>
        XlCall.TryExcel(XlCall.xlcOpen, out var opened, workbookPath, 0, true) is XlCall.XlReturn.XlReturnSuccess && opened is true
            ? null
            : $"Excel couldn't open {workbookPath}.";

    private static string? Activate(string openName)
    {
        XlCall.TryExcel(XlCall.xlcActivate, out _, openName);
        return null;
    }

    private static ExcelRangePick ToPick(ExcelReference reference)
    {
        var sheetName = XlCall.Excel(XlCall.xlSheetNm, reference) as string ?? string.Empty;
        var reftext = XlCall.Excel(XlCall.xlfReftext, reference, true) as string ?? string.Empty;
        var close = sheetName.IndexOf(']', StringComparison.Ordinal);
        var workbook = close > 0 ? sheetName[1..close] : string.Empty;
        return new ExcelRangePick(
            Workbook: workbook,
            Sheet: close > 0 ? sheetName[(close + 1)..] : sheetName,
            Address: reftext[(reftext.LastIndexOf('!') + 1)..],
            FirstRow: reference.RowFirst,
            FirstColumn: reference.ColumnFirst,
            Rows: reference.RowLast - reference.RowFirst + 1,
            Columns: reference.ColumnLast - reference.ColumnFirst + 1,
            Path: PathOf(workbook));
    }

    private static nint ActiveWindow() =>
        XlCall.TryExcel(XlCall.xlGetHwnd, out var handle) switch
        {
            XlCall.XlReturn.XlReturnSuccess => ExcelWindows.Resolve(handle),
            _ => 0,
        };

    private async Task<T> OnExcel<T>(Func<T> work, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var activeWindow = new TaskCompletionSource<nint>(TaskCreationOptions.RunContinuationsAsynchronously);
        ExcelWindows.BringForward(ExcelWindows.Foremost());
        ExcelAsyncUtil.QueueAsMacro(() =>
        {
            Complete(work, completion, cancellationToken);
            activeWindow.SetResult(ActiveWindow());
        });
        await _showAbove(await activeWindow.Task.ConfigureAwait(false)).ConfigureAwait(false);
        return await completion.Task.ConfigureAwait(false);
    }

    private static void Complete<T>(Func<T> work, TaskCompletionSource<T> completion, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            completion.SetCanceled(cancellationToken);
            return;
        }

        try
        {
            completion.SetResult(work());
        }
        catch (OperationCanceledException canceled)
        {
            completion.SetCanceled(canceled.CancellationToken);
        }
        catch (Exception exception)
        {
            completion.SetException(exception);
        }
    }
}
