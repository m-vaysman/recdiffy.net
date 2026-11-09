namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed record ExcelRangePick(string Workbook, string Sheet, string Address, int FirstRow, int FirstColumn, int Rows, int Columns);

public sealed record SourceTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<object?>> Rows);

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

    public sealed record Read(ExcelRangePick Range, SourceTable Table) : SourceRead;

    public sealed record Rejected(string Reason) : SourceRead;
}

public interface IExcelSources
{
    Task<RangeChoice> PickRangeAsync(string workbookPath, string prompt, CancellationToken cancellationToken);

    Task<SourceRead> ReadAsync(ExcelRangePick range, IProgress<double> progress, CancellationToken cancellationToken);
}

public interface IWorkbookDialog
{
    string? ChooseWorkbook(string title);
}
