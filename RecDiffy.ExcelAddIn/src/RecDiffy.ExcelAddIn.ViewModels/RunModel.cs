using System.Globalization;
using RecDiffy.Results;

namespace RecDiffy.ExcelAddIn.ViewModels;

public abstract record RunSide
{
    private RunSide()
    {
    }

    public sealed record Workbook(ExcelRangePick Range) : RunSide;

    public sealed record Csv(CsvSource Source) : RunSide;
}

public sealed record RunRequest(ProfileDocument Profile, RunSide Left, RunSide Right)
{
    private const string DateRule = "date.parse";

    public static string NameOf(Side side) => side switch
    {
        Side.Left => "left",
        Side.Right => "right",
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, "A side is left or right."),
    };

    public IReadOnlySet<string> DateColumns(Side side) =>
        Profile.Normalization.GetValueOrDefault(NameOf(side)) is { } rules
            ? rules.Parse
                .Where(call => string.Equals(call.Rule, DateRule, StringComparison.Ordinal))
                .Select(call => call.Strings.GetValueOrDefault("from"))
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
}

public sealed record RunProgress(string Stage, double? Fraction);

public sealed record SideRows(string Label, IReadOnlyList<int> SheetRows)
{
    public static SideRows Records(string label) => new(label, []);

    public string Describe(int ordinal) =>
        ordinal >= 0 && ordinal < SheetRows.Count
            ? string.Create(CultureInfo.CurrentCulture, $"row {SheetRows[ordinal]:N0}")
            : string.Create(CultureInfo.CurrentCulture, $"record {ordinal + 1:N0}");
}

public abstract record RunOutcome
{
    private RunOutcome()
    {
    }

    public sealed record Completed(ReconciliationResult Result, SideRows Left, SideRows Right, DateTimeOffset ReadAt) : RunOutcome;

    public sealed record Canceled : RunOutcome;

    public sealed record Failed(string Reason) : RunOutcome;
}

public interface IReconciliationRuns
{
    Task<RunOutcome> RunAsync(RunRequest request, IProgress<RunProgress> progress, CancellationToken cancellationToken);
}
