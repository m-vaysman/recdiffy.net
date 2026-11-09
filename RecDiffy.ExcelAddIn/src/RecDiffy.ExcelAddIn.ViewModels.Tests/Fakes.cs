using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

internal sealed class FakeDialog(string? path) : IWorkbookDialog
{
    public int Shown { get; private set; }

    public string? ChooseWorkbook(string title)
    {
        Shown++;
        return path;
    }
}

internal sealed class FakeExcel(RangeChoice choice, Func<ExcelRangePick, Task<SourceRead>> read) : IExcelSources
{
    public int Picks { get; private set; }

    public int Reads { get; private set; }

    public static ExcelRangePick Range { get; } = new("Book.xlsx", "Trades", "$A$1:$C$3", 0, 0, 3, 3);

    public static SourceTable Table { get; } = new(["Ref", "Quantity", "Price"], [["T-1", 10m, 1.5m], ["T-2", 20m, 2.5m]]);

    public static FakeExcel Reading(SourceRead result) =>
        new(new RangeChoice.Picked(Range), _ => Task.FromResult(result));

    public static FakeExcel ReadingTable() => Reading(new SourceRead.Read(Range, Table));

    public Task<RangeChoice> PickRangeAsync(string workbookPath, string prompt, CancellationToken cancellationToken)
    {
        Picks++;
        return Task.FromResult(choice);
    }

    public Task<SourceRead> ReadAsync(ExcelRangePick range, IProgress<double> progress, CancellationToken cancellationToken)
    {
        Reads++;
        return read(range);
    }
}
