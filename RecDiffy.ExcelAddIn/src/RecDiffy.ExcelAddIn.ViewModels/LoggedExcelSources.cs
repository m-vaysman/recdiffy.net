namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed class LoggedExcelSources : IExcelSources
{
    private readonly IExcelSources _inner;
    private readonly LogTiming _timing;

    public LoggedExcelSources(IExcelSources inner, IAddInLog log, TimeProvider clock)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _timing = new LogTiming(log, clock, LogAreas.Excel);
    }

    public Task<RangeChoice> PickRangeAsync(string workbookPath, string prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workbookPath);
        var how = $"Excel file {LogText.FileName(workbookPath)}";
        return _timing.RunAsync(
            $"{how}: picking a range",
            () => _inner.PickRangeAsync(workbookPath, prompt, cancellationToken),
            (choice, elapsed) => Picked(how, choice, elapsed),
            [workbookPath]);
    }

    public Task<RangeChoice> PickOpenRangeAsync(string prompt, CancellationToken cancellationToken) =>
        _timing.RunAsync(
            "Open workbook: picking a range",
            () => _inner.PickOpenRangeAsync(prompt, cancellationToken),
            (choice, elapsed) => Picked("Open workbook", choice, elapsed),
            []);

    public Task<SourceRead> ReadAsync(ExcelRangePick range, IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(range);
        var where = $"{range.Address} on [{range.Workbook}]{range.Sheet}";
        return _timing.RunAsync(
            $"Reading {where}",
            () => _inner.ReadAsync(range, progress, cancellationToken),
            (read, elapsed) => Read(where, range, read, elapsed),
            [range.Path]);
    }

    private static LogLine Picked(string how, RangeChoice choice, TimeSpan elapsed) => choice switch
    {
        RangeChoice.Picked { Range: var range } => new(
            LogSeverity.Information,
            $"{how}: picked {range.Address} on [{range.Workbook}]{range.Sheet}, {LogText.Count(range.Rows, "row", "rows")} by {LogText.Count(range.Columns, "column", "columns")}, after {LogText.Seconds(elapsed)}."),
        RangeChoice.Canceled => new(LogSeverity.Information, $"{how}: the pick was canceled after {LogText.Seconds(elapsed)}."),
        RangeChoice.Rejected rejected => new(LogSeverity.Warning, $"{how}: the pick was rejected after {LogText.Seconds(elapsed)}: {rejected.Reason}"),
        _ => throw new InvalidOperationException("A range choice is picked, canceled or rejected."),
    };

    private static LogLine Read(string where, ExcelRangePick range, SourceRead read, TimeSpan elapsed) => read switch
    {
        SourceRead.Read { Table: var table } => new(
            LogSeverity.Information,
            $"Read {LogText.Count(table.Rows.Count, "row", "rows")} from {where} in {LogText.Seconds(elapsed)}{LeftOut(range.Rows - 1 - table.Rows.Count)}."),
        SourceRead.Rejected rejected => new(LogSeverity.Warning, $"Reading {where} was rejected after {LogText.Seconds(elapsed)}: {rejected.Reason}"),
        _ => throw new InvalidOperationException("A source read is either read or rejected."),
    };

    private static string LeftOut(int emptyRows) =>
        emptyRows > 0 ? $"; {LogText.Count(emptyRows, "empty row was", "empty rows were")} left out" : string.Empty;
}
