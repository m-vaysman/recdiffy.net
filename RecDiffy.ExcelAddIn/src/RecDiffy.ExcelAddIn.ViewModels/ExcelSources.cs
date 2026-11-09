namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed record ExcelRangePick(string Workbook, string Sheet, string Address, int FirstRow, int FirstColumn, int Rows, int Columns, string? Path = null);

public sealed record SourceTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<object?>> Rows)
{
    public IReadOnlyList<int> SheetRows { get; init; } = [];

    public bool Uses1904 { get; init; }
}

public abstract record RangeChoice
{
    private RangeChoice()
    {
    }

    public sealed record Picked(ExcelRangePick Range) : RangeChoice;

    public sealed record Canceled : RangeChoice;

    public sealed record Rejected(string Reason) : RangeChoice;
}

public abstract record SourceRead
{
    private SourceRead()
    {
    }

    public sealed record Read(ExcelRangePick Range, SourceTable Table, SourcePreview Preview) : SourceRead;

    public sealed record Rejected(string Reason) : SourceRead;
}

public abstract record CsvPeek
{
    private CsvPeek()
    {
    }

    public sealed record Read(SourcePreview Preview) : CsvPeek;

    public sealed record Rejected(string Reason) : CsvPeek;
}

public interface IExcelSources
{
    Task<RangeChoice> PickRangeAsync(string workbookPath, string prompt, CancellationToken cancellationToken);

    Task<RangeChoice> PickOpenRangeAsync(string prompt, CancellationToken cancellationToken);

    Task<SourceRead> ReadAsync(ExcelRangePick range, IProgress<double> progress, CancellationToken cancellationToken);
}

public interface ICsvSources
{
    Task<CsvPeek> PeekAsync(CsvSource source, CancellationToken cancellationToken);
}

public interface IFileDialogs
{
    string? ChooseWorkbook(string title);

    string? ChooseCsvFile(string title);
}

public sealed record SourceServices(IExcelSources Excel, ICsvSources Csv, IFileDialogs Dialogs);
