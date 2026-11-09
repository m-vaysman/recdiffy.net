using System.Globalization;
using RecDiffy.Results;

namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed record SummaryCount(string Label, int Count, bool NeedsAttention);

public sealed record ExplanationSection(string Title, IReadOnlyList<string> Lines);

public sealed record RunContext(ReconciliationResult Result, SideRows Left, SideRows Right, IReadOnlyList<string> KeyFields);

public sealed class ResultRow(ResultEntry entry, RunContext context)
{
    public ResultEntry Entry { get; } = entry ?? throw new ArgumentNullException(nameof(entry));

    public string Outcome => ResultText.Outcome(Entry);

    public bool IsBreak => Entry.Outcome is FieldOutcome.Break;

    public string Left => ResultText.Rows(Entry.Left, context.Left, context);

    public string Right => ResultText.Rows(Entry.Right, context.Right, context);

    public string Details => ResultText.Details(Entry, context.Result);

    public IReadOnlyList<ExplanationSection> Explain() => ResultText.Explain(Entry, context);
}

public sealed record RunView(string Label, string Identity, IReadOnlyList<SummaryCount> Counts, IReadOnlyList<ResultRow> Rows, IReadOnlyList<string> RowProblems)
{
    public const int ShownRowProblems = 200;

    public bool HasRowProblems => RowProblems.Count > 0;

    public static RunView Of(RunOutcome.Completed run, IReadOnlyList<string> keyFields)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(keyFields);
        var context = new RunContext(run.Result, run.Left, run.Right, keyFields);
        var problems = run.Result.Rows.Where(row => row.Disposition is Disposition.Quarantined).ToArray();
        return new RunView(
            ResultText.Label(run),
            $"Profile {ShortHash(run.Result.Run.ProfileHash)} · RecDiffy engine {run.Result.Run.EngineVersion}",
            ResultText.Counts(run.Result.Summary),
            [.. run.Result.Entries.Select(entry => new ResultRow(entry, context))],
            [
                .. problems.Take(ShownRowProblems).Select(row => ResultText.RowProblem(row, context)),
                .. problems.Length > ShownRowProblems
                    ? [string.Create(CultureInfo.CurrentCulture, $"… and {problems.Length - ShownRowProblems:N0} more.")]
                    : Array.Empty<string>(),
            ]);
    }

    private static string ShortHash(string hash) => hash.Length > 12 ? hash[..12] : hash;
}

public static class ResultText
{
    public static string Outcome(ResultEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return (entry.Alignment, entry.Outcome) switch
        {
            (Alignment.Matched, FieldOutcome.Exact) => "Matched · exact",
            (Alignment.Matched, FieldOutcome.WithinTolerance) => "Matched · within tolerance",
            (Alignment.Matched, FieldOutcome.Break) => "Matched · break",
            (Alignment.Matched, _) => "Matched",
            (Alignment.LeftOnly, _) => "Left only",
            (Alignment.RightOnly, _) => "Right only",
            (Alignment.Ambiguous, _) => "Ambiguous",
            (Alignment.SearchIncomplete, _) => "Search incomplete",
            _ => entry.Alignment.ToString(),
        };
    }

    public static IReadOnlyList<SummaryCount> Counts(RunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return
        [
            new("Matched", summary.Matched, NeedsAttention: false),
            new("Exact", summary.Exact, NeedsAttention: false),
            new("Within tolerance", summary.WithinTolerance, NeedsAttention: false),
            new("Breaks", summary.Break, summary.Break > 0),
            new("Left only", summary.LeftOnly, summary.LeftOnly > 0),
            new("Right only", summary.RightOnly, summary.RightOnly > 0),
            new("Ambiguous", summary.Ambiguous, summary.Ambiguous > 0),
            .. Optional("Search incomplete", summary.SearchIncomplete),
            .. Optional("Quarantined rows", summary.Quarantined),
            .. Optional("Suppressed rows", summary.Suppressed),
        ];
    }

    public static string Label(RunOutcome.Completed run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var read = run.ReadAt.ToString("g", CultureInfo.CurrentCulture);
        return $"Data read at {read} · Left: {Side(run.Left, run.Result, "left")} · Right: {Side(run.Right, run.Result, "right")} · Counts are result entries: one matched entry can hold several rows.";
    }

    public static string Rows(IReadOnlyList<SourceRef> rows, SideRows side, RunContext context)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(side);
        ArgumentNullException.ThrowIfNull(context);
        return rows switch
        {
            [] => "—",
            [var only] => Row(only, side, context),
            _ => string.Create(CultureInfo.CurrentCulture, $"{rows.Count:N0} rows: {string.Join(", ", rows.Take(3).Select(row => side.Describe(row.Ordinal)))}{(rows.Count > 3 ? ", …" : string.Empty)}"),
        };
    }

    public static string Details(ResultEntry entry, ReconciliationResult result)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(result);
        return entry.Alignment switch
        {
            Alignment.Matched => string.Join(" · ", entry.Diffs.Where(diff => diff.Outcome is FieldOutcome.Break or FieldOutcome.WithinTolerance).Select(diff => Difference(diff, result))),
            _ => entry.Explanation.Trail.LastOrDefault() is { } last ? Reason(last) : string.Empty,
        };
    }

    public static string RowProblem(RowResult row, RunContext context)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(context);
        var side = SideOf(row.Row, context);
        var problems = row.Diagnostics.Count == 0 ? "the row couldn't be read" : string.Join("; ", row.Diagnostics.Select(Diagnostic));
        return $"{Capitalized(row.Row.Source)} {side.Describe(row.Row.Ordinal)}: {problems}";
    }

    public static IReadOnlyList<ExplanationSection> Explain(ResultEntry entry, RunContext context)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(context);
        var explanation = entry.Explanation;
        return
        [
            new("Outcome", [Outcome(entry), .. Composition(entry)]),
            new("Matching",
            [
                explanation.CommittedBy is { } pass ? $"Matched by {Pass(pass)}." : "No pass made a match.",
                .. explanation.Trail.Count == 0 ? ["No pass attempts were recorded for this result."] : explanation.Trail.Select(Attempt),
            ]),
            .. explanation.Hints.Count == 0 ? Array.Empty<ExplanationSection>() : [new("Hints", [.. explanation.Hints.Select(hint => $"{hint.Code}: {hint.Detail}")])],
            .. entry.Left.Concat(entry.Right).Take(ExplainedRows).Select(row => RowSection(row, context)),
            .. entry.Left.Count + entry.Right.Count > ExplainedRows
                ? [new ExplanationSection("More rows", [string.Create(CultureInfo.CurrentCulture, $"{entry.Left.Count + entry.Right.Count - ExplainedRows:N0} more rows belong to this result.")])]
                : Array.Empty<ExplanationSection>(),
        ];
    }

    private const int ExplainedRows = 20;

    public static string Pass(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return id is ['P', .. var number] && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var position)
            ? $"Pass {position}"
            : $"pass {id}";
    }

    public static string Attempt(PassAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        return $"{Pass(attempt.PassId)}: {Reason(attempt)}";
    }

    public static string KeyText(string? key) =>
        key is null
            ? "(none)"
            : string.Join(" · ", key.Split(KeyPartSeparator).Select(part => part.Split(SchemeSeparator) is [_, var value] ? value : part));

    private const char KeyPartSeparator = '\u001f';

    private const char SchemeSeparator = '\u001e';

    private static string Reason(PassAttempt attempt) => attempt.Verdict switch
    {
        PassVerdict.Committed => $"matched on key {KeyText(attempt.Key)}.",
        PassVerdict.NoKey => "the record has no value for the key.",
        PassVerdict.NoCandidate => $"no record on the other side has key {KeyText(attempt.Key)}.",
        PassVerdict.Vetoed => $"a candidate with key {KeyText(attempt.Key)} was refused{Detail(attempt.Detail)}.",
        PassVerdict.Contest => string.Create(CultureInfo.CurrentCulture, $"{attempt.Candidates.Count:N0} records share key {KeyText(attempt.Key)}, so none was picked."),
        PassVerdict.SearchIncomplete => "the search limit was reached before a match was settled.",
        _ => attempt.Verdict.ToString(),
    };

    private static string Detail(string? detail) => detail is { Length: > 0 } text ? $": {text}" : string.Empty;

    private static string Difference(FieldDiff diff, ReconciliationResult result)
    {
        var sign = diff.Outcome is FieldOutcome.Break ? "≠" : "≈";
        var delta = diff.Delta.Kind switch
        {
            DeltaKind.Number => $" (difference {diff.Delta.Number.ToString(CultureInfo.InvariantCulture)})",
            DeltaKind.Days => string.Create(CultureInfo.InvariantCulture, $" ({diff.Delta.Days} {(Math.Abs(diff.Delta.Days) == 1 ? "day" : "days")})"),
            _ => string.Empty,
        };
        return $"{diff.Field.Name} {Shown(result.Format(diff.Left))} {sign} {Shown(result.Format(diff.Right))}{delta}";
    }

    private static string Shown(string value) => value.Length == 0 ? "(empty)" : value;

    private static string Row(SourceRef row, SideRows side, RunContext context) =>
        Keys(row, context) is { Length: > 0 } keys ? $"{keys} · {side.Describe(row.Ordinal)}" : side.Describe(row.Ordinal);

    private static string Keys(SourceRef row, RunContext context)
    {
        var canonical = context.Result.Canonical(row);
        return string.Join(" · ", context.KeyFields.Select(canonical.Text).Where(text => text.Length > 0));
    }

    private static SideRows SideOf(SourceRef row, RunContext context) =>
        string.Equals(row.Source, "right", StringComparison.Ordinal) ? context.Right : context.Left;

    private static ExplanationSection RowSection(SourceRef row, RunContext context)
    {
        var side = SideOf(row, context);
        var outcome = context.Result.Rows.FirstOrDefault(result => result.Row == row);
        var trace = context.Result.Trace(row);
        return new ExplanationSection(
            $"{Capitalized(row.Source)} {Row(row, side, context)}",
            [
                outcome is null ? "No row outcome was recorded." : $"{outcome.Disposition} · input {Validity(outcome.Validity)}",
                .. outcome?.Diagnostics.Select(diagnostic => $"Diagnostic: {Diagnostic(diagnostic)}") ?? [],
                .. trace.Count == 0 ? ["No normalization steps were recorded."] : trace.Select(step => $"{step.RuleId} → {step.Field}: {Quoted(step.Raw)} → {Quoted(step.Canonical)}"),
            ]);
    }

    private static string Validity(InputValidity validity) => validity switch
    {
        InputValidity.Valid => "valid",
        InputValidity.ValidWithWarnings => "valid with warnings",
        InputValidity.Invalid => "invalid",
        _ => validity.ToString(),
    };

    private static string Quoted(string? value) => value is null ? "(none)" : $"'{value}'";

    private static string Diagnostic(Diagnostic diagnostic) =>
        $"{diagnostic.Code}{(diagnostic.Field is { } field ? $" in {field}" : string.Empty)}{(diagnostic.RawText is { } raw ? $" ('{raw}')" : string.Empty)}";

    private static IEnumerable<string> Composition(ResultEntry entry) =>
        entry.Left.Count > 1 || entry.Right.Count > 1
            ? [string.Create(CultureInfo.CurrentCulture, $"{entry.Left.Count:N0} left {(entry.Left.Count == 1 ? "row" : "rows")} and {entry.Right.Count:N0} right {(entry.Right.Count == 1 ? "row" : "rows")}.")]
            : [];

    private static string Side(SideRows side, ReconciliationResult result, string name) =>
        string.Create(CultureInfo.CurrentCulture, $"{side.Label}, {result.Rows.Count(row => string.Equals(row.Row.Source, name, StringComparison.Ordinal)):N0} rows");

    private static string Capitalized(string side) => side switch
    {
        "left" => "Left",
        "right" => "Right",
        _ => side,
    };

    private static IEnumerable<SummaryCount> Optional(string label, int count) =>
        count > 0 ? [new SummaryCount(label, count, NeedsAttention: true)] : [];
}
