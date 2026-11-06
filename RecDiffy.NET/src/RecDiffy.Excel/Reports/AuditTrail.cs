using System.Collections.Immutable;
using RecDiffy.Excel.Facts;
using RecDiffy.Excel.Model;
using RecDiffy.Results;

namespace RecDiffy.Excel.Reports;

// MO7. Layout from build_xlsx.py (MO7 audit trail): for each selected entry, its consolidation steps (lifecycle, package), the engine's
// explanation trail (pass, verdict, key or detail, candidates), any hints, and the final result. Committed green, contest amber,
// no key or no candidate grey, hints in italics. The text is the engine's Explanation; nothing here is inferred.
internal static class AuditTrail
{
    public static ReportDefinition Definition { get; } = new(
        ReportKind.AuditTrail,
        [],
        context => Selected(context).IsEmpty ? "Nothing to explain: no entry is selected and every entry is an exact match." : null,
        Build);

    private sealed record Step(string Name, string Verdict, string Detail, string Candidates, bool Hint = false);

    private static ImmutableArray<EntryFact> Selected(ReportContext context) =>
        context.Options.SelectedEntries is { } selected
            ? [.. context.Facts.Entries.Where(entry => selected.Any(id => entry.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase)))]
            : [.. context.Facts.Entries
                .Where(entry => entry.Entry is not { Alignment: Alignment.Matched, Outcome: FieldOutcome.Exact } || entry.Entry.Composition.HasFlag(Composition.LifecycleCollapsed) || entry.Entry.Composition.HasFlag(Composition.Package))
                .OrderBy(entry => entry.Entry switch
                {
                    { Outcome: FieldOutcome.Break } => 0,
                    { Outcome: FieldOutcome.WithinTolerance } => 1,
                    { Alignment: Alignment.Ambiguous or Alignment.SearchIncomplete } => 2,
                    { Alignment: Alignment.LeftOnly or Alignment.RightOnly } => 3,
                    _ => 4,
                })
                .ThenBy(entry => entry.Id, StringComparer.Ordinal)
                .Take(context.Options.MaxCards)];

    private static ReportBody Build(ReportContext context)
    {
        var info = ReportCatalogue.Of(ReportKind.AuditTrail);
        var note = ReportSheet.Body;
        var sheet = Selected(context).Aggregate(
            (Sheet: ReportSheet.Head(context, info)
                .Put(note, 1, "The engine's explanation records the pass that committed each match, or the pass that left an entry open, with its key, candidates and detail. Passes an entry fell through before that are not recorded by the engine.", Styles.Note),
             Row: note + 2),
            (state, entry) => Card(state.Sheet, state.Row, context, entry));
        return new ReportBody(sheet.Sheet.Widths([16, 18, 70, 34]), []);
    }

    private static IEnumerable<Step> Steps(ReportContext context, EntryFact entry)
    {
        var suppressed = context.Facts.RowsByEntry[entry.Id].Where(row => row.Row.Suppression is not null).ToArray();
        var explanation = entry.Entry.Explanation;
        return
        [
            .. entry.Entry.Composition.HasFlag(Composition.LifecycleCollapsed)
                ? [new Step("Consolidation", "Lifecycle", $"{suppressed.Length} row(s) superseded or cancelled: {string.Join(", ", suppressed.Select(row => $"{row.Row.Row.RowKey} ({row.Row.Suppression})"))}", "")]
                : Array.Empty<Step>(),
            .. entry.Entry.Composition.HasFlag(Composition.Package)
                ? [new Step("Consolidation", "Package", $"legs consolidated into one item: ours {entry.Keys(Side.Left)} / theirs {entry.Keys(Side.Right)}", "")]
                : Array.Empty<Step>(),
            .. explanation.Trail.Select(attempt => new Step(
                attempt.PassId,
                attempt.Verdict.ToString(),
                string.Join("  ·  ", new[] { attempt.Key is { } key ? $"key {Readable(key)}" : null, attempt.Detail }.OfType<string>()),
                string.Join(", ", attempt.Candidates.Select(candidate => $"{candidate.Source}:{candidate.RowKey}")))),
            .. explanation.Hints.Select(hint => new Step("Hint", hint.Code, hint.Detail, "", Hint: true)),
            new Step("Result", entry.Entry.Alignment.ToString(), Result(entry), ""),
        ];
    }

    private static string Result(EntryFact entry) => entry.Entry switch
    {
        { Alignment: Alignment.Matched, Outcome: FieldOutcome.Exact } => $"{entry.Entry.Composition}, exact",
        { Alignment: Alignment.Matched } => $"{entry.Entry.Composition}, {entry.Entry.Outcome}: {string.Join(", ", entry.Entry.Diffs.Select(diff => $"{diff.Field.Name} {diff.Outcome}"))}",
        _ => $"{entry.Entry.Alignment}: no match committed",
    };

    private static (Sheet Sheet, int Row) Card(Sheet sheet, int row, ReportContext context, EntryFact entry)
    {
        var steps = Steps(context, entry).ToImmutableArray();
        var drawn = sheet
            .Put(row, 1, $"{entry.Short}  ·  ours {Or(entry.Keys(Side.Left))} / theirs {Or(entry.Keys(Side.Right))}  ·  {entry.Entry.Alignment}, {entry.Entry.Outcome}", Styles.Banner)
            .Merge(row, 1, row, 4)
            .Header(row + 1, context.Labels("Step", "Verdict", "Key or detail", "Candidates"), height: 18)
            .Fold(steps, (current, step, index) =>
            {
                var line = row + 2 + index;
                var verdict = step.Verdict switch
                {
                    "Committed" => new CellStyle(Bold: true, Color: Palette.OkText, Fill: Palette.Ok),
                    "Contest" or "SearchIncomplete" or "Ambiguous" => new CellStyle(Bold: true, Color: Palette.ToleranceText, Fill: Palette.Tolerance),
                    "NoKey" or "NoCandidate" or "LeftOnly" or "RightOnly" => new CellStyle(Bold: true, Color: Palette.AttributedText, Fill: Palette.Attributed),
                    "Vetoed" => new CellStyle(Bold: true, Color: Palette.BreakText, Fill: Palette.Break),
                    "Matched" => new CellStyle(Bold: true, Color: Palette.OkText, Fill: Palette.Ok),
                    _ => new CellStyle(Bold: true, Fill: step.Hint ? Palette.Card : Palette.Sub, Italic: step.Hint),
                };
                return current
                    .Put(line, 1, step.Name, Styles.Bold)
                    .Put(line, 2, step.Verdict, verdict)
                    .Put(line, 3, step.Detail, new CellStyle(Wrap: true, Italic: step.Hint, VAlign: VerticalAlign.Top))
                    .Put(line, 4, step.Candidates, new CellStyle(Wrap: true, Color: Palette.Muted, VAlign: VerticalAlign.Top));
            });
        return (drawn, row + 3 + steps.Length);
    }

    private static string Or(string keys) => keys.Length == 0 ? "—" : keys;

    // Composite keys are joined with control characters; show them as a readable separator.
    private static string Readable(string key) =>
        string.Concat(key.Select(character => char.IsControl(character) ? " | " : character.ToString()));
}
