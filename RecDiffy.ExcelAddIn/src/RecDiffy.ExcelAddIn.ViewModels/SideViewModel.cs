using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed partial class SideViewModel(string side, SourceServices services, CultureChoice defaultCulture) : ObservableObject
{
    private readonly SourceServices _services = services ?? throw new ArgumentNullException(nameof(services));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Heading))]
    private string alias = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCsv), nameof(CsvHasHeader), nameof(CsvDelimiter), nameof(CsvEncoding), nameof(CsvQuote), nameof(CsvEscape))]
    private SourceSetup source = new NoSource();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewTable))]
    private CultureChoice culture = defaultCulture ?? throw new ArgumentNullException(nameof(defaultCulture));

    [ObservableProperty]
    private SourceTable? table;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasData), nameof(PreviewCaption), nameof(PreviewTable))]
    private SourcePreview? preview;

    [ObservableProperty]
    private string summary = string.Empty;

    [ObservableProperty]
    private string? problem;

    [ObservableProperty]
    private bool isReading;

    [ObservableProperty]
    private double progress;

    public string Side { get; } = side ?? throw new ArgumentNullException(nameof(side));

    public string Heading => SideHeading(Side, Alias);

    public bool HasData => Preview is not null;

    public string PreviewCaption => Preview is { } shown
        ? string.Create(CultureInfo.CurrentCulture, $"{shown.Rows.Count:N0} sample {(shown.Rows.Count == 1 ? "row" : "rows")} of {Count(shown.RowCount, "row")}")
        : "No data chosen yet.";

    public PreviewTable? PreviewTable => Preview?.Render(Culture.ToCulture());

    public bool IsCsv => Source is CsvSource;

    public bool CsvHasHeader
    {
        get => Csv.HasHeader;
        set => ChangeCsv(csv => csv with { HasHeader = value });
    }

    public DelimiterChoice CsvDelimiter
    {
        get => DelimiterChoice.For(Csv.Delimiter);
        set => ChangeCsv(csv => value is { } choice ? csv with { Delimiter = choice.Character } : csv);
    }

    public EncodingChoice CsvEncoding
    {
        get => EncodingChoice.For(Csv.Encoding);
        set => ChangeCsv(csv => value is { } choice ? csv with { Encoding = choice.Name } : csv);
    }

    public QuoteChoice CsvQuote
    {
        get => QuoteChoice.For(Csv.Quote);
        set => ChangeCsv(csv => value is { } choice ? WithQuote(csv, choice.Character) : csv);
    }

    public EscapeChoice CsvEscape
    {
        get => EscapeChoice.For(Csv.Escape, Csv.Quote);
        set => ChangeCsv(csv => value is { } choice ? csv with { Escape = choice.EscapeFor(csv.Quote) } : csv);
    }

    private CsvSource Csv => Source is CsvSource csv ? csv : CsvSource.At(string.Empty);

    private string SideName => Side.ToLowerInvariant();

    private string Prompt => $"Select the {SideName} data, including its header row.";

    public static string SideHeading(string side, string alias)
    {
        ArgumentNullException.ThrowIfNull(side);
        ArgumentNullException.ThrowIfNull(alias);
        return alias.Trim() is { Length: > 0 } trimmed ? $"{side} · {trimmed}" : side;
    }

    public static string SummaryOf(ExcelRangePick range, SourceTable table)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(table);
        return $"{range.Workbook} · {range.Sheet} · {range.Address} · {Count(table.Rows.Count, "row")} · {Count(table.Headers.Count, "column")}";
    }

    public static string SummaryOf(CsvSource csv, SourcePreview preview)
    {
        ArgumentNullException.ThrowIfNull(csv);
        ArgumentNullException.ThrowIfNull(preview);
        return $"{Path.GetFileName(csv.Path)} · {Count(preview.RowCount, "row")} · {Count(preview.Headers.Count, "column")}";
    }

    public SideSetup ToSetup() => new(Alias, Source);

    public void Apply(SideSetup setup, CultureChoice chosen)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(chosen);
        (Alias, Source, Culture, Table, Preview, Problem, Summary) = (setup.Alias, setup.Source, chosen, null, null, null, Unread(setup.Source));
        if (setup.Source is CsvSource csv)
            ReadCsvCommand.Execute(csv);
    }

    [RelayCommand]
    private Task ChooseExcelDataAsync(CancellationToken cancellationToken) =>
        _services.Dialogs.ChooseWorkbook($"Choose the {SideName} workbook") is { } path
            ? ReadExcelAsync(_services.Excel.PickRangeAsync(path, Prompt, cancellationToken), cancellationToken)
            : Task.CompletedTask;

    [RelayCommand]
    private Task ChooseOpenWorkbookDataAsync(CancellationToken cancellationToken) =>
        ReadExcelAsync(_services.Excel.PickOpenRangeAsync(Prompt, cancellationToken), cancellationToken);

    [RelayCommand]
    private Task ChooseCsvFileAsync(CancellationToken cancellationToken)
    {
        if (_services.Dialogs.ChooseCsvFile($"Choose the {SideName} CSV file") is not { } path)
            return Task.CompletedTask;

        var csv = Source is CsvSource current ? current with { Path = path } : CsvSource.At(path);
        Source = csv;
        return ReadCsvAsync(csv, cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ReadCsvAsync(CsvSource csv, CancellationToken cancellationToken)
    {
        (Table, Preview, Problem, Summary) = (null, null, null, $"Reading {Path.GetFileName(csv.Path)}…");
        var peek = await _services.Csv.PeekAsync(csv, cancellationToken).ConfigureAwait(true);
        if (Source != csv)
            return;

        (Preview, Summary, Problem) = peek switch
        {
            CsvPeek.Read read => (read.Preview, SummaryOf(csv, read.Preview), (string?)null),
            CsvPeek.Rejected rejected => ((SourcePreview?)null, string.Empty, rejected.Reason),
            _ => throw new InvalidOperationException("A CSV peek is either read or rejected."),
        };
    }

    private async Task ReadExcelAsync(Task<RangeChoice> picking, CancellationToken cancellationToken)
    {
        var choice = await picking.ConfigureAwait(true);
        if (choice is RangeChoice.Rejected rejected)
            Problem = rejected.Reason;

        if (choice is not RangeChoice.Picked picked)
            return;

        (Problem, Progress, IsReading) = (null, 0, true);
        try
        {
            var read = await _services.Excel
                .ReadAsync(picked.Range, new Progress<double>(value => Progress = value), cancellationToken)
                .ConfigureAwait(true);
            (Source, Table, Preview, Summary, Problem) = read switch
            {
                SourceRead.Read done => (WorkbookOf(done.Range), done.Table, done.Preview, SummaryOf(done.Range, done.Table), (string?)null),
                SourceRead.Rejected failed => (new NoSource(), (SourceTable?)null, (SourcePreview?)null, string.Empty, failed.Reason),
                _ => throw new InvalidOperationException("A source read is either read or rejected."),
            };
        }
        finally
        {
            (Progress, IsReading) = (1, false);
        }
    }

    private void ChangeCsv(Func<CsvSource, CsvSource> change)
    {
        if (Source is not CsvSource csv)
            return;

        var changed = change(csv);
        if (changed == csv)
            return;

        Source = changed;
        ReadCsvCommand.Execute(changed);
    }

    private static CsvSource WithQuote(CsvSource csv, char quote) =>
        csv with { Quote = quote, Escape = csv.Escape == csv.Quote ? quote : csv.Escape };

    private static SourceSetup WorkbookOf(ExcelRangePick range) => new WorkbookSource(range.Workbook, range.Path, range.Sheet, range.Address);

    private static string Unread(SourceSetup source) => source switch
    {
        WorkbookSource workbook => $"{workbook.Workbook} · {workbook.Sheet} · {workbook.Address} · choose the data again to read it",
        CsvSource csv => $"Reading {Path.GetFileName(csv.Path)}…",
        _ => string.Empty,
    };

    private static string Count(long count, string noun) =>
        string.Create(CultureInfo.CurrentCulture, $"{count:N0} {(count == 1 ? noun : noun + "s")}");
}
