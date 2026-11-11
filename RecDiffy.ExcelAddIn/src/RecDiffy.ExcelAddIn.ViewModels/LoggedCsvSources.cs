namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed class LoggedCsvSources : ICsvSources
{
    private readonly ICsvSources _inner;
    private readonly LogTiming _timing;

    public LoggedCsvSources(ICsvSources inner, IAddInLog log, TimeProvider clock)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _timing = new LogTiming(log, clock, LogAreas.Csv);
    }

    public Task<CsvPeek> PeekAsync(CsvSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var file = LogText.FileName(source.Path);
        return _timing.RunAsync(
            $"Reading {file}",
            () => _inner.PeekAsync(source, cancellationToken),
            (peek, elapsed) => Peeked(file, source, peek, elapsed),
            [source.Path]);
    }

    public static string Settings(CsvSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return $"{source.Encoding}, {Delimiter(source.Delimiter)}, {(source.HasHeader ? "with" : "without")} a header row";
    }

    private static LogLine Peeked(string file, CsvSource source, CsvPeek peek, TimeSpan elapsed) => peek switch
    {
        CsvPeek.Read { Preview: var preview } => new(
            LogSeverity.Information,
            $"Read {file}: {LogText.Count(preview.RowCount, "row", "rows")}, {LogText.Count(preview.Headers.Count, "column", "columns")}, {Settings(source)}, in {LogText.Seconds(elapsed)}."),
        CsvPeek.Rejected rejected => new(LogSeverity.Warning, $"Rejected {file} ({Settings(source)}) after {LogText.Seconds(elapsed)}: {rejected.Reason}"),
        _ => throw new InvalidOperationException("A CSV peek is either read or rejected."),
    };

    private static string Delimiter(char? delimiter) => delimiter switch
    {
        null => "delimiter detected",
        ',' => "comma-delimited",
        ';' => "semicolon-delimited",
        '\t' => "tab-delimited",
        '|' => "pipe-delimited",
        { } other => $"delimited by '{other}'",
    };
}
