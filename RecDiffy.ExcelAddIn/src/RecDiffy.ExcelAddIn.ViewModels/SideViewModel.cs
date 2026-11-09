using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed partial class SideViewModel(string side, IExcelSources excel, IWorkbookDialog dialog) : ObservableObject
{
    private readonly IExcelSources _excel = excel ?? throw new ArgumentNullException(nameof(excel));
    private readonly IWorkbookDialog _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Heading))]
    private string alias = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasData))]
    private SourceTable? table;

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

    public bool HasData => Table is not null;

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
        var rows = table.Rows.Count;
        var columns = table.Headers.Count;
        return string.Create(
            CultureInfo.CurrentCulture,
            $"{range.Workbook} · {range.Sheet} · {range.Address} · {rows:N0} {(rows == 1 ? "row" : "rows")} · {columns:N0} {(columns == 1 ? "column" : "columns")}");
    }

    [RelayCommand]
    private async Task ChooseExcelDataAsync(CancellationToken cancellationToken)
    {
        if (_dialog.ChooseWorkbook($"Choose the {Side.ToLowerInvariant()} workbook") is not { } path)
            return;

        var choice = await _excel
            .PickRangeAsync(path, $"Select the {Side.ToLowerInvariant()} data, including its header row.", cancellationToken)
            .ConfigureAwait(true);
        if (choice is RangeChoice.Rejected rejected)
            Problem = rejected.Reason;

        if (choice is not RangeChoice.Picked picked)
            return;

        (Problem, Progress, IsReading) = (null, 0, true);
        try
        {
            var read = await _excel
                .ReadAsync(picked.Range, new Progress<double>(value => Progress = value), cancellationToken)
                .ConfigureAwait(true);
            (Table, Summary, Problem) = read switch
            {
                SourceRead.Read done => (done.Table, SummaryOf(done.Range, done.Table), (string?)null),
                SourceRead.Rejected failed => ((SourceTable?)null, string.Empty, failed.Reason),
                _ => throw new InvalidOperationException("A source read is either read or rejected."),
            };
        }
        finally
        {
            (Progress, IsReading) = (1, false);
        }
    }
}
