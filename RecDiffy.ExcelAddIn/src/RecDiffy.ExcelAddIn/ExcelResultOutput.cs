using System.Globalization;
using ExcelDna.Integration;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn;

internal sealed class ExcelResultOutput : IResultOutput
{
    private const int WorksheetType = 1;
    private const int SheetNamesInfo = 1;
    private const int DateSystemInfo = 20;

    public async Task<WriteOutcome> WriteAsync(ResultSheet sheet, IProgress<WriteProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(progress);
        return await OnExcel(Open, error => new Created.Broken(error)).ConfigureAwait(false) switch
        {
            Created.Opened opened => await PrepareAsync(opened, sheet, progress, cancellationToken).ConfigureAwait(false),
            var failed => NotCreated(failed, sheet),
        };
    }

    private static async Task<WriteOutcome> PrepareAsync(Created.Opened opened, ResultSheet sheet, IProgress<WriteProgress> progress, CancellationToken cancellationToken) =>
        await OnExcel(() => Name(opened, sheet), error => new Created.Broken(error)).ConfigureAwait(false) switch
        {
            Created.Ready ready => await WriteToAsync(ready.Target, sheet, progress, cancellationToken).ConfigureAwait(false),
            var failed => NotCreated(failed, sheet),
        };

    private static WriteOutcome NotCreated(Created created, ResultSheet sheet) => created switch
    {
        Created.Rejected rejected => new WriteOutcome.Failed(rejected.Reason, 0, sheet.Results),
        Created.Broken broken => new WriteOutcome.Failed($"Excel stopped the write with an error: {broken.Error.Message}", 0, sheet.Results),
        _ => throw new InvalidOperationException("Only a rejected or broken workbook stops the write before it starts."),
    };

    private static async Task<WriteOutcome> WriteToAsync(Target target, ResultSheet sheet, IProgress<WriteProgress> progress, CancellationToken cancellationToken)
    {
        var total = sheet.Results;
        var blocks = ExcelBlocks.Split(sheet, ExcelBlocks.BlockRows);
        progress.Report(new WriteProgress(0, total));
        foreach (var (index, block) in blocks.Index())
        {
            var written = index == 0 ? 0 : blocks[index - 1].ResultsAfter;
            if (cancellationToken.IsCancellationRequested)
            {
                await OnExcel(() => Finish(target, sheet, ResultSheets.Canceled(written, total, sheet.IsStale)), Unexpected).ConfigureAwait(false);
                return new WriteOutcome.Canceled(target.Workbook, target.Sheets, written, total);
            }

            var values = ExcelBlocks.Values(block);
            var problem = await OnExcel(() => WriteBlock(target, block, values), Unexpected).ConfigureAwait(false);
            if (problem is not null)
            {
                await OnExcel(() => Finish(target, sheet, ResultSheets.Failed(problem, sheet.IsStale)), Unexpected).ConfigureAwait(false);
                return new WriteOutcome.Failed(problem, written, total);
            }

            progress.Report(new WriteProgress(block.ResultsAfter, total));
        }

        var finished = await OnExcel(() => Finish(target, sheet, sheet.FinalStatus), Unexpected).ConfigureAwait(false);
        return finished is null
            ? new WriteOutcome.Completed(target.Workbook, target.Sheets, total)
            : new WriteOutcome.Failed(finished, total, total);
    }

    private static Created Open()
    {
        var before = OpenWorkbooks();
        if (XlCall.TryExcel(XlCall.xlcNew, out var made, WorksheetType) is not XlCall.XlReturn.XlReturnSuccess || made is false)
            return new Created.Rejected("Excel couldn't create a new workbook.");

        return OpenWorkbooks().Except(before, StringComparer.OrdinalIgnoreCase).ToArray() switch
        {
            [var workbook] when Uses1904(workbook) => new Created.Rejected($"the new workbook {workbook} uses the 1904 date system, so its dates would be wrong."),
            [var workbook] => new Created.Opened(workbook, SheetsOf(workbook)),
            _ => new Created.Rejected("Excel created a workbook, but RecDiffy couldn't tell which one it was."),
        };
    }

    private static Created Name(Created.Opened opened, ResultSheet sheet)
    {
        if (!IsOpen(opened.Workbook))
            return new Created.Rejected($"the workbook {opened.Workbook} was closed before the write started.");

        Echo(on: false);
        try
        {
            var names = SheetNames.ForPages(sheet.Name, sheet.Pages.Count, opened.Sheets.Skip(sheet.Pages.Count));
            var named = Succeeded(XlCall.xlcActivate, opened.Workbook)
                && names.Select((name, page) => NameSheet(opened.Workbook, opened.Sheets, page, name)).ToArray().All(ok => ok);
            return named
                ? new Created.Ready(new Target(opened.Workbook, names))
                : new Created.Rejected($"Excel couldn't name the results sheets in {opened.Workbook}.");
        }
        finally
        {
            Echo(on: true);
        }
    }

    private static bool NameSheet(string workbook, IReadOnlyList<string> existing, int page, string name)
    {
        if (page < existing.Count)
            return Succeeded(XlCall.xlcWorkbookName, existing[page], name);

        var before = SheetsOf(workbook);
        if (!Succeeded(XlCall.xlcWorkbookInsert, WorksheetType))
            return false;

        return SheetsOf(workbook).Except(before, StringComparer.OrdinalIgnoreCase).ToArray() is [var inserted]
            && Succeeded(XlCall.xlcWorkbookName, inserted, name)
            && Succeeded(XlCall.xlcWorkbookMove, name, workbook, page + 1);
    }

    private static string? WriteBlock(Target target, SheetBlock block, object[,] values)
    {
        if (!IsOpen(target.Workbook))
            return Closed(target);

        Echo(on: false);
        try
        {
            var reference = new ExcelReference(block.FirstRow, block.FirstRow + block.Rows.Count - 1, 0, block.Width - 1, target.Reference(block.Page));
            return reference.SetValue(values)
                ? null
                : string.Create(CultureInfo.CurrentCulture, $"Excel didn't accept rows {block.FirstRow + 1:N0} to {block.FirstRow + block.Rows.Count:N0} on {target.Reference(block.Page)}.");
        }
        catch (XlCallException)
        {
            return IsOpen(target.Workbook)
                ? string.Create(CultureInfo.CurrentCulture, $"Excel couldn't write rows {block.FirstRow + 1:N0} to {block.FirstRow + block.Rows.Count:N0} on {target.Reference(block.Page)}.")
                : Closed(target);
        }
        finally
        {
            Echo(on: true);
        }
    }

    private static string? Finish(Target target, ResultSheet sheet, string status)
    {
        if (!IsOpen(target.Workbook))
            return Closed(target);

        Echo(on: false);
        try
        {
            foreach (var (index, page) in sheet.Pages.Index().Reverse())
                Format(target, index, page);

            var cell = sheet.StatusCell;
            return new ExcelReference(cell.Row, cell.Row, cell.Column, cell.Column, target.Reference(0)).SetValue(status)
                ? null
                : $"Excel didn't accept the status line on {target.Reference(0)}.";
        }
        catch (XlCallException)
        {
            return IsOpen(target.Workbook) ? $"Excel couldn't finish {target.Reference(0)}." : Closed(target);
        }
        finally
        {
            Echo(on: true);
        }
    }

    private static void Format(Target target, int index, SheetPage page)
    {
        var sheet = target.Reference(index);
        var width = Math.Max(ExcelBlocks.ColumnWidths.Count, page.Rows.Max(row => row.Cells.Count));
        _ = Succeeded(XlCall.xlcActivate, target.Workbook);
        _ = Succeeded(XlCall.xlcWorkbookActivate, target.Sheets[index]);
        foreach (var (columnWidth, column) in ExcelBlocks.ColumnWidths.Select((columnWidth, column) => (columnWidth, column)))
            _ = Succeeded(XlCall.xlcColumnWidth, columnWidth, new ExcelReference(0, 0, column, column, sheet));

        foreach (var run in ExcelBlocks.HeadingRuns(page))
        {
            if (Succeeded(XlCall.xlcSelect, new ExcelReference(run.FirstRow, run.LastRow, 0, width - 1, sheet)))
                _ = Succeeded(XlCall.xlcFormatFont, ExcelMissing.Value, ExcelMissing.Value, true);
        }

        foreach (var run in ExcelBlocks.DateRuns(page))
        {
            if (Succeeded(XlCall.xlcSelect, new ExcelReference(run.FirstRow, run.LastRow, run.Column, run.Column, sheet)))
                _ = Succeeded(XlCall.xlcFormatNumber | XlCall.xlIntl, run.HasTime ? ExcelBlocks.DateTimeFormat : ExcelBlocks.DateFormat);
        }

        var topLeft = new ExcelReference(0, 0, 0, 0, sheet);
        _ = Succeeded(XlCall.xlcFormulaGoto, topLeft, true);
        if (page.FrozenRows > 0)
            _ = Succeeded(XlCall.xlcFreezePanes, true, 0, page.FrozenRows);

        _ = Succeeded(XlCall.xlcSelect, topLeft);
    }

    private static bool Succeeded(int command, params object[] arguments) =>
        XlCall.TryExcel(command, out var result, arguments) is XlCall.XlReturn.XlReturnSuccess && result is not false;

    private static void Echo(bool on) => _ = XlCall.TryExcel(XlCall.xlcEcho, out _, on);

    private static bool Uses1904(string workbook) =>
        XlCall.TryExcel(XlCall.xlfGetDocument, out var flag, DateSystemInfo, workbook) is XlCall.XlReturn.XlReturnSuccess && flag is true;

    private static bool IsOpen(string workbook) => OpenWorkbooks().Contains(workbook, StringComparer.OrdinalIgnoreCase);

    private static string Closed(Target target) => $"the workbook {target.Workbook} was closed before the write finished.";

    private static IReadOnlyList<string> OpenWorkbooks() =>
        XlCall.TryExcel(XlCall.xlfDocuments, out var names) is XlCall.XlReturn.XlReturnSuccess && names is object[,] grid
            ? [.. grid.Cast<object>().OfType<string>()]
            : [];

    private static IReadOnlyList<string> SheetsOf(string workbook) =>
        XlCall.TryExcel(XlCall.xlfGetWorkbook, out var names, SheetNamesInfo, workbook) is XlCall.XlReturn.XlReturnSuccess
            ? names switch
            {
                object[,] grid => [.. grid.Cast<object>().OfType<string>().Select(PlainSheetName)],
                string only => [PlainSheetName(only)],
                _ => [],
            }
            : [];

    private static string PlainSheetName(string name) =>
        name.IndexOf(']', StringComparison.Ordinal) is var close and >= 0 ? name[(close + 1)..] : name;

    private static string? Unexpected(Exception exception) => $"Excel stopped the write with an error: {exception.Message}";

    private static Task<T> OnExcel<T>(Func<T> work, Func<Exception, T> failed)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        ExcelAsyncUtil.QueueAsMacro(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception exception)
            {
                completion.SetResult(failed(exception));
            }
        });
        return completion.Task;
    }

    private sealed record Target(string Workbook, IReadOnlyList<string> Sheets)
    {
        public string Reference(int page) => $"[{Workbook}]{Sheets[page]}";
    }

    private abstract record Created
    {
        private Created()
        {
        }

        public sealed record Opened(string Workbook, IReadOnlyList<string> Sheets) : Created;

        public sealed record Ready(Target Target) : Created;

        public sealed record Rejected(string Reason) : Created;

        public sealed record Broken(Exception Error) : Created;
    }
}
