using System.Globalization;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

internal sealed class FakeDialog(string? path) : IFileDialogs
{
    public int Shown { get; private set; }

    public string? ChooseWorkbook(string title)
    {
        Shown++;
        return path;
    }

    public string? ChooseCsvFile(string title)
    {
        Shown++;
        return path;
    }
}

internal sealed class FakeExcel(RangeChoice choice, Func<ExcelRangePick, Task<SourceRead>> read) : IExcelSources
{
    public int Picks { get; private set; }

    public int OpenPicks { get; private set; }

    public int Reads { get; private set; }

    public static ExcelRangePick Range { get; } = new("Book.xlsx", "Trades", "$A$1:$C$3", 0, 0, 3, 3, @"C:\data\Book.xlsx");

    public static SourceTable Table { get; } = new(["Ref", "Quantity", "Price"], [["T-1", 10m, 1.5m], ["T-2", 20m, 2.5m]]);

    public static FakeExcel Reading(SourceRead result) =>
        new(new RangeChoice.Picked(Range), _ => Task.FromResult(result));

    public static FakeExcel ReadingTable() => Reading(new SourceRead.Read(Range, Table));

    public Task<RangeChoice> PickRangeAsync(string workbookPath, string prompt, CancellationToken cancellationToken)
    {
        Picks++;
        return Task.FromResult(choice);
    }

    public Task<RangeChoice> PickOpenRangeAsync(string prompt, CancellationToken cancellationToken)
    {
        OpenPicks++;
        return Task.FromResult(choice);
    }

    public Task<SourceRead> ReadAsync(ExcelRangePick range, IProgress<double> progress, CancellationToken cancellationToken)
    {
        Reads++;
        return read(range);
    }
}

internal sealed class FakeCsv(Func<CsvSource, Task<CsvPeek>> peek) : ICsvSources
{
    public static SourcePreview Preview { get; } = new(["Ref", "Quantity"], [["T-1", "10"], ["T-2", "20"]], 2);

    public List<CsvSource> Peeked { get; } = [];

    public static FakeCsv Reading() => new(_ => Task.FromResult<CsvPeek>(new CsvPeek.Read(Preview)));

    public Task<CsvPeek> PeekAsync(CsvSource source, CancellationToken cancellationToken)
    {
        Peeked.Add(source);
        return peek(source);
    }
}

internal static class Make
{
    public static CultureInfo ExcelCulture { get; } = CultureInfo.GetCultureInfo("en-US");

    public static SideViewModel Side(string side, IExcelSources excel, IFileDialogs dialogs, ICsvSources? csv = null) =>
        new(side, new SourceServices(excel, csv ?? FakeCsv.Reading(), dialogs), CultureChoice.Of(ExcelCulture));

    public static SourcesViewModel Sources(IExcelSources excel, IFileDialogs dialogs, ICsvSources? csv = null) =>
        new(new SourceServices(excel, csv ?? FakeCsv.Reading(), dialogs), ExcelCulture);
}
