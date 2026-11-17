using System.Collections.Immutable;
using RecDiffy.Results;

namespace RecDiffy.PublicDataDemo;

/// <summary>One run of the Treasury reconciliation at one tolerance.</summary>
internal sealed record ToleranceRun(decimal Tolerance, RunSummary Summary, int TotalBreaks, int SplitBreaks);

/// <summary>
/// The same Treasury data at several tolerances: too tight and rounding shows up as breaks, too loose and real breaks
/// disappear. The tab also finds the smallest tolerance at which every day's total ties.
/// </summary>
internal static class Tolerances
{
    /// <summary>The tolerances to try, in millions of dollars.</summary>
    public static readonly ImmutableArray<decimal> Steps = [0m, 1m, 2m, Treasury.Tolerance, 100m, 2_000m];

    public const string Code = """
        decimal[] tolerances = [0m, 1m, 2m, 12m, 100m, 2_000m];

        var runs = tolerances.Select(tolerance => RecBuilder.Between(statementDays, debtChanges)
            .MatchOn(d => d.Date, c => c.Date)
            .Compare(d => d.Net, c => c.Net, tolerance)
            .Compare(d => d.Intragovernmental, c => c.Intragovernmental, tolerance)
            .Compare(d => d.Public, c => c.Public, tolerance)
            .Run());
        """;

    public static ImmutableArray<ToleranceRun> Sweep(IReadOnlyList<DtsDay> days, IReadOnlyList<DebtChange> changes) =>
    [
        .. Steps.Select(tolerance => Treasury.Reconcile(days, changes, tolerance)).Zip(Steps, (result, tolerance) => new ToleranceRun(
            tolerance,
            result.Result.Summary,
            result.Matched.Count(match => Breaks(match.Diffs, "Net")),
            result.Matched.Count(match => Breaks(match.Diffs, "Intragovernmental") || Breaks(match.Diffs, "Public")))),
    ];

    /// <summary>The largest difference in each field across the matched days: the smallest tolerance that would clear it.</summary>
    public static ImmutableArray<(string Field, decimal Largest)> Largest(IReadOnlyList<DtsDay> days, IReadOnlyList<DebtChange> changes)
    {
        var pairs = days.Join(changes, day => day.Date, change => change.Date, (day, change) => (Day: day, Change: change)).ToArray();
        return
        [
            ("Net", pairs.Select(pair => Math.Abs(pair.Day.Net - pair.Change.Net)).DefaultIfEmpty().Max()),
            ("Intragovernmental", pairs.Select(pair => Math.Abs(pair.Day.Intragovernmental - pair.Change.Intragovernmental)).DefaultIfEmpty().Max()),
            ("Public", pairs.Select(pair => Math.Abs(pair.Day.Public - pair.Change.Public)).DefaultIfEmpty().Max()),
        ];
    }

    public static string Tab(IReadOnlyList<ToleranceRun> runs, IReadOnlyList<(string Field, decimal Largest)> largest)
    {
        using var sweep = Grids.Build(
            "Sweep",
            [("Tolerance ($M)", typeof(decimal)), ("Exact", typeof(int)), ("Within tolerance", typeof(int)), ("Break", typeof(int)), ("Left only", typeof(int)), ("Right only", typeof(int)), ("Total breaks", typeof(int)), ("Split breaks", typeof(int))],
            runs.Select(run => (object?[])[run.Tolerance, run.Summary.Exact, run.Summary.WithinTolerance, run.Summary.Break, run.Summary.LeftOnly, run.Summary.RightOnly, run.TotalBreaks, run.SplitBreaks]));
        using var needed = Grids.Build(
            "Needed",
            [("Field", typeof(string)), ("Smallest tolerance that clears every day ($M)", typeof(decimal))],
            largest.Select(field => (object?[])[field.Field, field.Largest]));
        var total = largest.First(field => field.Field is "Net").Largest;
        var split = largest.Where(field => field.Field is not "Net").Max(field => field.Largest);
        var reading = (TotalIsRounding: total <= Treasury.Tolerance, SplitIsRounding: split <= Treasury.Tolerance) switch
        {
            (true, false) => $"""
                The total clears every compared day at ${total.ToInvariant("N2")}M, inside the $12M that rounding explains. The split by
                holder needs ${split.ToInvariant("N2")}M, far beyond rounding: a classification difference. A tolerance wide enough to
                clear it would also hide the breaks a reconciliation is for.
                """,
            (true, true) => $"""
                Both the total (${total.ToInvariant("N2")}M) and the split by holder (${split.ToInvariant("N2")}M) clear every compared
                day inside the $12M that rounding explains.
                """,
            _ => $"""
                The total needs ${total.ToInvariant("N2")}M to clear every compared day, more than the $12M that rounding explains: at
                least one day is a real break, not rounding.
                """,
        };

        return $"""
            <p class="lead">A tolerance is a decision, not a fudge. The Treasury data from the previous tab, run again at six tolerances:
            too tight and rounding shows up as breaks; too loose and real breaks disappear.</p>
            <h3>The same data at six tolerances</h3>
            {Page.Grid(sweep)}
            <h3>How much tolerance each field needs</h3>
            {Page.Grid(needed)}
            <p class="note">{reading}</p>
            <h3>The code</h3>
            {Page.Code(Code)}
            """;
    }

    private static bool Breaks(IReadOnlyList<FieldDiff> diffs, string field) =>
        diffs.Any(diff => diff is { Outcome: FieldOutcome.Break } && string.Equals(diff.Field.Name, field, StringComparison.Ordinal));
}
