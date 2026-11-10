using System.Collections.Immutable;
using System.Globalization;
using RecDiffy.Results;

namespace RecDiffy.ExcelAddIn.ViewModels;

public abstract record SheetValue
{
    private SheetValue()
    {
    }

    public static SheetValue Empty { get; } = new Blank();

    public sealed record Blank : SheetValue;

    public sealed record Text(string Value) : SheetValue;

    public sealed record Number(decimal Value) : SheetValue;

    public sealed record Date(DateTime Value, bool HasTime) : SheetValue;
}

public enum SheetRowKind
{
    Plain,
    Heading,
    Result,
    Problem,
}

public sealed record SheetRow(SheetRowKind Kind, IReadOnlyList<SheetValue> Cells)
{
    public static SheetRow Blank { get; } = new(SheetRowKind.Plain, []);
}

public sealed record SheetPage(IReadOnlyList<SheetRow> Rows, int FrozenRows);

public sealed record SheetCell(int Row, int Column);

public sealed record ResultSheetOptions(bool IncludeExactMatches, bool IsStale, int RowLimit = ResultSheets.ExcelRowLimit);

public sealed record ResultSheet(
    string Name,
    IReadOnlyList<SheetPage> Pages,
    int Results,
    SheetCell StatusCell,
    string FinalStatus,
    bool IsStale);

public static class ResultSheets
{
    public const int ExcelRowLimit = 1_048_576;

    public const int MinimumRowLimit = 20;

    public const string WritingStatus = "Incomplete: the write didn't finish.";

    public const string CompleteStatus = "Complete";

    public const string StaleStatus = "The rules or sources changed after this run. Run again to refresh the results.";

    public static IReadOnlyList<string> Columns { get; } = ["Outcome", "Left key", "Left rows", "Right key", "Right rows", "Pass", "Details"];

    private static readonly SheetCell Status = new(7, 1);

    public static ResultSheet Build(RunOutcome.Completed run, IReadOnlyList<string> keyFields, ResultSheetOptions options)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(keyFields);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.RowLimit, MinimumRowLimit, nameof(options));
        var context = new RunContext(run.Result, run.Left, run.Right, keyFields);
        IReadOnlyList<SheetRow> results =
        [
            .. run.Result.Entries
                .Where(entry => options.IncludeExactMatches || !IsExactMatch(entry))
                .Select(entry => ResultRowOf(entry, context)),
        ];
        var problems = run.Result.Rows.Where(row => row.Disposition is Disposition.Quarantined).ToArray();
        var intro = Intro(run, options);
        var resultsHeading = Heading(Columns);
        var problemsHeading = Heading(["Side", "Row", "Diagnostics"]);
        IReadOnlyList<(SheetRow Row, SheetRow? Repeats)> problemSection = problems.Length == 0
            ? []
            :
            [
                (SheetRow.Blank, null),
                (Heading(["Rows RecDiffy couldn't read"]), null),
                (problemsHeading, null),
                .. problems.Select(problem => (ProblemRowOf(problem, context), (SheetRow?)problemsHeading)),
            ];
        IReadOnlyList<(SheetRow Row, SheetRow? Repeats)> rows =
        [
            .. intro.Select(row => (row, (SheetRow?)null)),
            (resultsHeading, null),
            .. results.Select(row => (row, (SheetRow?)resultsHeading)),
            .. problemSection,
        ];
        return new ResultSheet(
            SheetNames.Base(run.ReadAt),
            Paginate(rows, options.RowLimit, intro.Count + 1),
            results.Count,
            Status,
            options.IsStale ? StaleStatus : CompleteStatus,
            options.IsStale);
    }

    public static bool IsExactMatch(ResultEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry is { Alignment: Alignment.Matched, Outcome: FieldOutcome.Exact };
    }

    public static string Canceled(int written, int total, bool isStale) =>
        Incomplete(string.Create(CultureInfo.CurrentCulture, $"canceled after {written:N0} of {total:N0} results."), isStale);

    public static string Failed(string reason, bool isStale)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return Incomplete(reason, isStale);
    }

    private static string Incomplete(string what, bool isStale) =>
        isStale ? $"Incomplete: {what} {StaleStatus}" : $"Incomplete: {what}";

    private static IReadOnlyList<SheetRow> Intro(RunOutcome.Completed run, ResultSheetOptions options)
    {
        var info = run.Result.Run;
        var counts = ResultText.Counts(run.Result.Summary);
        return
        [
            Heading(["RecDiffy results"]),
            Plain(Text("Data read at"), new SheetValue.Date(run.ReadAt.DateTime, HasTime: true)),
            Heading(["Side", "Rows", "Source"]),
            Plain(Text("Left"), Count(SideRowCount(run.Result, "left")), Text(run.Left.Label)),
            Plain(Text("Right"), Count(SideRowCount(run.Result, "right")), Text(run.Right.Label)),
            Plain(Text("Profile"), Text(info.ProfileHash)),
            Plain(Text("Engine"), Text($"RecDiffy engine {info.EngineVersion}")),
            Plain(Text("Status"), Text(WritingStatus)),
            Plain(Text("Exact matches"), Text(ExactMatchesNote(run.Result.Summary, options.IncludeExactMatches))),
            SheetRow.Blank,
            Heading([.. counts.Select(count => count.Label)]),
            Plain([.. counts.Select(count => Count(count.Count))]),
            SheetRow.Blank,
        ];
    }

    private static string ExactMatchesNote(RunSummary summary, bool included) => (included, summary.Exact) switch
    {
        (true, _) => "Listed below.",
        (false, 0) => "Not listed below. There aren't any.",
        (false, 1) => "Not listed below. The counts include it.",
        _ => string.Create(CultureInfo.CurrentCulture, $"Not listed below. The counts include all {summary.Exact:N0} of them."),
    };

    private static int SideRowCount(ReconciliationResult result, string side) =>
        result.Rows.Count(row => string.Equals(row.Row.Source, side, StringComparison.Ordinal));

    private static SheetRow ResultRowOf(ResultEntry entry, RunContext context) =>
        new(
            SheetRowKind.Result,
            [
                Text(ResultText.Outcome(entry)),
                Key(entry.Left, context),
                Locations(entry.Left, context.Left),
                Key(entry.Right, context),
                Locations(entry.Right, context.Right),
                entry.Explanation.CommittedBy is { } pass ? Text(ResultText.Pass(pass)) : SheetValue.Empty,
                Text(ResultText.Details(entry, context.Result)),
            ]);

    private static SheetRow ProblemRowOf(RowResult row, RunContext context)
    {
        var side = string.Equals(row.Row.Source, "right", StringComparison.Ordinal) ? context.Right : context.Left;
        var problems = row.Diagnostics.Count == 0 ? "the row couldn't be read" : string.Join("; ", row.Diagnostics.Select(Diagnostic));
        return new SheetRow(
            SheetRowKind.Problem,
            [Text(SideName(row.Row.Source)), Text(side.Describe(row.Row.Ordinal)), Text(problems)]);
    }

    private static string SideName(string source) => source switch
    {
        "left" => "Left",
        "right" => "Right",
        _ => source,
    };

    private static string Diagnostic(Diagnostic diagnostic) =>
        $"{diagnostic.Code}{(diagnostic.Field is { } field ? $" in {field}" : string.Empty)}{(diagnostic.RawText is { } raw ? $" ('{raw}')" : string.Empty)}";

    private static SheetValue Locations(IReadOnlyList<SourceRef> rows, SideRows side) =>
        rows.Count == 0 ? SheetValue.Empty : Text(string.Join(", ", rows.Select(row => side.Describe(row.Ordinal))));

    private static SheetValue Key(IReadOnlyList<SourceRef> rows, RunContext context)
    {
        IReadOnlyList<SheetValue> keys = [.. rows.Select(row => KeyOf(row, context)).Distinct()];
        return keys switch
        {
            [] => SheetValue.Empty,
            [var only] => only,
            _ => Text(string.Join(", ", keys.Select(Shown))),
        };
    }

    private static SheetValue KeyOf(SourceRef row, RunContext context)
    {
        var canonical = context.Result.Canonical(row);
        return context.KeyFields switch
        {
            [var field] => Typed(canonical[field]),
            var fields => Text(string.Join(" · ", fields.Select(canonical.Text).Where(text => text.Length > 0))),
        };
    }

    private static SheetValue Typed(CanonicalValue value) => value switch
    {
        { IsNull: true } => SheetValue.Empty,
        { Type: FieldType.Decimal } => new SheetValue.Number(value.Number),
        { Type: FieldType.Date } => new SheetValue.Date(value.Date.ToDateTime(TimeOnly.MinValue), HasTime: false),
        _ => Text(value.Text ?? string.Empty),
    };

    private static string Shown(SheetValue value) => value switch
    {
        SheetValue.Text text => text.Value,
        SheetValue.Number number => number.Value.ToString(CultureInfo.InvariantCulture),
        SheetValue.Date date => date.Value.ToString(date.HasTime ? "yyyy-MM-dd HH:mm" : "yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => "(none)",
    };

    private static IReadOnlyList<SheetPage> Paginate(IReadOnlyList<(SheetRow Row, SheetRow? Repeats)> rows, int limit, int firstFrozen)
    {
        var pages = new List<List<SheetRow>> { new() };
        foreach (var (row, repeats) in rows)
        {
            if (pages[^1].Count == limit)
                pages.Add(repeats is null ? [] : [repeats]);

            pages[^1].Add(row);
        }

        return
        [
            .. pages.Select((page, index) => new SheetPage(
                page,
                index switch
                {
                    0 => Math.Min(firstFrozen, page.Count),
                    _ => page is [{ Kind: SheetRowKind.Heading }, ..] ? 1 : 0,
                })),
        ];
    }

    private static SheetValue Text(string value) => value.Length == 0 ? SheetValue.Empty : new SheetValue.Text(value);

    private static SheetValue Count(int value) => new SheetValue.Number(value);

    private static SheetRow Plain(params IReadOnlyList<SheetValue> cells) => new(SheetRowKind.Plain, cells);

    private static SheetRow Heading(IReadOnlyList<string> labels) => new(SheetRowKind.Heading, [.. labels.Select(Text)]);
}

public static class SheetNames
{
    public const int MaxLength = 31;

    private const string Fallback = "RecDiffy";

    private const string Reserved = "History";

    public static string Base(DateTimeOffset readAt) =>
        string.Create(CultureInfo.InvariantCulture, $"RecDiffy {readAt:yyyy-MM-dd HH.mm}");

    public static IReadOnlyList<string> ForPages(string wanted, int pages, IEnumerable<string> taken)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        ArgumentNullException.ThrowIfNull(taken);
        ArgumentOutOfRangeException.ThrowIfLessThan(pages, 1);
        IReadOnlyList<string> others = [.. taken];
        return Enumerable.Range(0, pages).Aggregate(
            ImmutableArray<string>.Empty,
            (names, _) => names.Add(Unique(wanted, [.. others, .. names])));
    }

    public static string Unique(string wanted, IEnumerable<string> taken)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        ArgumentNullException.ThrowIfNull(taken);
        var used = taken.Append(Reserved).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var clean = Clean(wanted);
        return Enumerable.Range(1, int.MaxValue)
            .Select(number => number == 1 ? Fit(clean, string.Empty) : Fit(clean, string.Create(CultureInfo.InvariantCulture, $" ({number})")))
            .First(name => !used.Contains(name));
    }

    private static string Clean(string name)
    {
        var replaced = string.Concat(name.Select(character => character is ':' or '\\' or '/' or '?' or '*' or '[' or ']' || char.IsControl(character) ? '-' : character));
        var trimmed = replaced.Trim().Trim('\'').Trim();
        return trimmed.Length == 0 ? Fallback : trimmed;
    }

    private static string Fit(string name, string suffix) =>
        name.Length + suffix.Length <= MaxLength
            ? name + suffix
            : name[..(MaxLength - suffix.Length)].TrimEnd().TrimEnd('\'') + suffix;
}
