using System.IO;
using ExcelDna.Integration;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn;

internal sealed class ExcelDnaSources : IExcelSources
{
    private const int BlockRows = 5_000;
    private const int ReferenceType = 8;
    private const int DocumentPath = 2;

    public Task<RangeChoice> PickRangeAsync(string workbookPath, string prompt, CancellationToken cancellationToken) =>
        OnExcel(() => Pick(workbookPath, prompt), cancellationToken);

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
        if (ready is not null)
            return new RangeChoice.Rejected(ready);

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

            return SourceTables.Build(range, rows);
        }
        catch (XlCallException)
        {
            return new SourceRead.Rejected($"Excel couldn't read {range.Address} on [{range.Workbook}]{range.Sheet}. Check that the workbook is still open, then pick the range again.");
        }
    }

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
        XlCall.TryExcel(XlCall.xlfGetDocument, out var directory, DocumentPath, openName) is XlCall.XlReturn.XlReturnSuccess
        && directory is string folder
        && string.Equals(
            Path.GetFullPath(Path.Combine(folder, openName)),
            Path.GetFullPath(workbookPath),
            StringComparison.OrdinalIgnoreCase);

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
        return new ExcelRangePick(
            Workbook: close > 0 ? sheetName[1..close] : string.Empty,
            Sheet: close > 0 ? sheetName[(close + 1)..] : sheetName,
            Address: reftext[(reftext.LastIndexOf('!') + 1)..],
            FirstRow: reference.RowFirst,
            FirstColumn: reference.ColumnFirst,
            Rows: reference.RowLast - reference.RowFirst + 1,
            Columns: reference.ColumnLast - reference.ColumnFirst + 1);
    }

    private static Task<T> OnExcel<T>(Func<T> work, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        ExcelAsyncUtil.QueueAsMacro(() =>
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
        });
        return completion.Task;
    }
}
