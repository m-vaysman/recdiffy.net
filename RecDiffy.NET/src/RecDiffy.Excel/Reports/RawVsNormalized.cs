using System.Collections.Immutable;
using RecDiffy.Excel.Facts;
using RecDiffy.Excel.Model;
using RecDiffy.Results;

namespace RecDiffy.Excel.Reports;

// BO3. Layout from build_xlsx.py (BO3 raw vs normalized): one card per one-to-one matched pair with each side's original columns
// as received (IReconciliationResult.Raw), the canonical values on both sides, the normalization steps that changed a value
// (IReconciliationResult.Trace) and the engine's diffs. The only report that shows original columns.
internal static class RawVsNormalized
{
    private static readonly ImmutableArray<ReportRole> Canonical =
    [
        ReportRole.Counterparty, ReportRole.Instrument, ReportRole.Asset, ReportRole.Side, ReportRole.TradeDate, ReportRole.SettleDate,
        ReportRole.Currency, ReportRole.Quantity, ReportRole.Price, ReportRole.Accrued, ReportRole.Fees, ReportRole.NetCash,
    ];

    private static readonly ImmutableHashSet<ReportRole> Linked =
    [
        ReportRole.TradeDate, ReportRole.SettleDate, ReportRole.Quantity, ReportRole.Price, ReportRole.Accrued, ReportRole.Fees, ReportRole.NetCash,
    ];

    private static readonly ImmutableArray<string> ArgumentNames = ["from", "code", "perspective", "quantity"];

    public static ReportDefinition Definition { get; } = new(ReportKind.RawVsNormalized, [], Unavailable, Build);

    private static string? Unavailable(ReportContext context) =>
        (context.Options.RawColumns, context.Options.Profile, Cards(context).IsEmpty) switch
        {
            (null, null, _) => "Needs ExcelReportOptions.Profile or RawColumns: ReconciliationResult can read a raw cell by column name but does not list a row's columns.",
            (_, _, true) => "No one-to-one matched entries to show.",
            _ => null,
        };

    private static ImmutableArray<EntryFact> Cards(ReportContext context)
    {
        var oneToOne = context.Facts.Entries.Where(entry => entry is { IsMatched: true, Entry.Left.Length: 1, Entry.Right.Length: 1 });
        return context.Options.SelectedEntries is { } selected
            ? [.. oneToOne.Where(entry => selected.Any(id => entry.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase)))]
            : [.. oneToOne
                .Where(entry => entry.Entry.Outcome is not FieldOutcome.Exact || Changed(context, entry).Any())
                .OrderBy(entry => entry.Entry.Outcome switch { FieldOutcome.Break => 0, FieldOutcome.WithinTolerance => 1, _ => 2 })
                .ThenBy(entry => entry.Id, StringComparer.Ordinal)
                .Take(context.Options.MaxCards)];
    }

    private static IEnumerable<(string Side, TraceStep Step)> Changed(ReportContext context, EntryFact entry) =>
        new[] { ("Ours", entry.Entry.Left[0]), ("Theirs", entry.Entry.Right[0]) }
            .SelectMany(pair => context.Result.Trace(pair.Item2).Select(step => (Side: pair.Item1, Step: step)))
            .Where(pair => pair.Step is { Raw: { } raw, Canonical: { } canonical } && raw != canonical);

    private static ImmutableArray<string> RawColumns(ReportContext context, string side, IEnumerable<SourceRef> rows)
    {
        var declared = context.Options.RawColumns is { } configured && configured.TryGetValue(side, out var columns)
            ? columns
            : FromProfile(context.Options.Profile, side);
        var sample = rows.ToArray();
        return [.. declared.Distinct(StringComparer.Ordinal).Where(column => sample.Any(row => context.Result.Raw(row, column) is not null))];
    }

    private static IEnumerable<string> FromProfile(CompiledProfile? profile, string side) =>
        profile is null
            ? []
            : [
                .. profile.Document.SourceKeys.TryGetValue(side, out var key) ? [key] : Array.Empty<string>(),
                .. profile.Document.Normalization.TryGetValue(side, out var rules)
                    ? rules.Identify.Concat(rules.Parse).Concat(rules.Derive).Concat(rules.Validate)
                        .SelectMany(rule => ArgumentNames.Select(name => rule.Strings.TryGetValue(name, out var value) ? value : null).OfType<string>())
                    : [],
            ];

    private static ReportBody Build(ReportContext context)
    {
        var info = ReportCatalogue.Of(ReportKind.RawVsNormalized);
        var entries = DataTabs.Entries(context);
        var diffs = DataTabs.Diffs(context);
        var cards = Cards(context);
        var left = RawColumns(context, "left", cards.Select(entry => entry.Entry.Left[0]));
        var right = RawColumns(context, "right", cards.Select(entry => entry.Entry.Right[0]));
        var roles = Canonical.Where(context.Fields.IsMapped).ToImmutableArray();
        var span = Math.Max(Math.Max(left.Length, right.Length), roles.Length) + 1;

        var sheet = cards.Aggregate(
            (Sheet: ReportSheet.Head(context, info), Row: ReportSheet.Body),
            (state, entry) => Card(state.Sheet, state.Row, context, entries, diffs, entry, left, right, roles, span));
        return new ReportBody(
            sheet.Sheet.Widths([20, .. Enumerable.Repeat(15.0, span - 1)]),
            [entries, diffs]);
    }

    private static (Sheet Sheet, int Row) Card(
        Sheet sheet,
        int row,
        ReportContext context,
        DataTab entries,
        DataTab diffs,
        EntryFact entry,
        ImmutableArray<string> left,
        ImmutableArray<string> right,
        ImmutableArray<ReportRole> roles,
        int span)
    {
        var ours = entry.Entry.Left[0];
        var theirs = entry.Entry.Right[0];
        var entryDiffs = context.Facts.DiffsByEntry[entry.Id].ToImmutableArray();
        var states = entryDiffs.ToImmutableDictionary(diff => diff.Field, diff => diff.State, StringComparer.Ordinal);
        var steps = Changed(context, entry).ToImmutableArray();
        var why = entry.Entry.Outcome switch
        {
            FieldOutcome.Break => $"break on {string.Join(", ", entry.RootFields)}",
            FieldOutcome.WithinTolerance => $"within tolerance on {string.Join(", ", entryDiffs.Where(diff => diff.State is DiffState.Tolerance).Select(diff => diff.Field))}",
            _ => $"exact after {steps.Length} normalization step{(steps.Length == 1 ? "" : "s")}",
        };
        var raw = row + 1;
        var canonical = raw + 4;
        var stepsTitle = canonical + 4;
        var diffTitle = stepsTitle + 1 + steps.Length + 1;
        var end = diffTitle + 2 + Math.Max(1, entryDiffs.Length);

        var drawn = sheet
            .Put(row, 1, $"{entry.Short}  ·  {why}  ·  matched by {entry.Entry.Explanation.CommittedBy}  ·  outcome {entry.Entry.Outcome}  ·  ours {ours.RowKey} / theirs {theirs.RowKey}", Styles.Banner)
            .Merge(row, 1, row, span)
            .Put(raw, 1, context.Label("Ours, as received"), Styles.Header)
            .PutRow(raw, 2, left, Styles.Header)
            .Put(raw + 1, 1, context.Label("raw"), new CellStyle(Bold: true, Fill: Palette.Sub))
            .PutRow(raw + 1, 2, left.Select(column => context.Result.Raw(ours, column)), new CellStyle(Color: Palette.Muted))
            .Put(raw + 2, 1, context.Label("Theirs, as received"), Styles.Header)
            .PutRow(raw + 2, 2, right, Styles.Header)
            .Put(raw + 3, 1, context.Label("raw"), new CellStyle(Bold: true, Fill: Palette.Sub))
            .PutRow(raw + 3, 2, right.Select(column => context.Result.Raw(theirs, column)), new CellStyle(Color: Palette.Muted))
            .Put(canonical, 1, context.Label("Canonical"), Styles.Header)
            .PutRow(canonical, 2, roles.Select(role => context.Label(context.Field(role)!)), Styles.Header)
            .Put(canonical + 1, 1, context.Label("Ours, normalized"), new CellStyle(Bold: true, Fill: Palette.Card))
            .Put(canonical + 2, 1, context.Label("Theirs, normalized"), new CellStyle(Bold: true, Fill: Palette.Card))
            .Fold(roles, (current, role, index) =>
            {
                var format = DataTabs.FormatFor(role);
                var theirsStyle = states.TryGetValue(context.Field(role)!, out var state) ? SideBySideCompare.DiffStyleFor(state) with { Format = format } : Styles.Link(format);
                return Linked.Contains(role)
                    ? current
                        .PutFormula(canonical + 1, index + 2, ReportSheet.Link(entries.Cell(DataTabs.Ours(role), entry.Index)), Styles.Link(format))
                        .PutFormula(canonical + 2, index + 2, ReportSheet.Link(entries.Cell(DataTabs.Theirs(role), entry.Index)), theirsStyle)
                    : current
                        .Put(canonical + 1, index + 2, entry.Ours[role].Text)
                        .Put(canonical + 2, index + 2, entry.Theirs[role].Text, states.ContainsKey(context.Field(role)!) ? theirsStyle : CellStyle.Plain);
            })
            .Put(stepsTitle, 1, context.Label("Normalization steps that changed a value"), Styles.Section)
            .Fold(steps, (current, step, index) => current
                .Put(stepsTitle + 1 + index, 2, step.Side)
                .Put(stepsTitle + 1 + index, 3, step.Step.RuleId)
                .Put(stepsTitle + 1 + index, 4, step.Step.Field)
                .Put(stepsTitle + 1 + index, 5, $"{step.Step.Raw}  →  {step.Step.Canonical}")
                .Merge(stepsTitle + 1 + index, 5, stepsTitle + 1 + index, Math.Max(5, Math.Min(span, 9))))
            .Put(diffTitle, 1, context.Label("Differences"), Styles.Section);
        var withDiffs = entryDiffs.IsEmpty
            ? drawn.Put(diffTitle + 1, 2, context.Label("None: exact after normalization"), new CellStyle(Color: Palette.OkText, Fill: Palette.Ok))
            : drawn
                .PutRow(diffTitle + 1, 2, context.Labels("Field", "Ours", "Theirs", "Delta", "Outcome", "Attributed to"), new CellStyle(Bold: true, Fill: Palette.Sub))
                .Fold(entryDiffs, (current, diff, index) =>
                {
                    var line = diffTitle + 2 + index;
                    var format = DataTabs.FormatFor(diff.Ours, diff.Role);
                    return current
                        .Put(line, 2, diff.Field)
                        .PutFormula(line, 3, ReportSheet.Link(diffs.Cell("ours", diff.Index)), Styles.Link(format))
                        .PutFormula(line, 4, ReportSheet.Link(diffs.Cell("theirs", diff.Index)), Styles.Link(format))
                        .Put(line, 5, diff.Delta is null ? null : Content.Formula(ReportSheet.Link(diffs.Cell("delta", diff.Index))), Styles.Link(diff.Diff.Delta.Kind is DeltaKind.Days ? Formats.Integer : DataTabs.FormatFor(diff.Role)))
                        .Put(line, 6, diff.Diff.Outcome.ToString(), SideBySideCompare.DiffStyleFor(diff.State))
                        .Put(line, 7, diff.Diff.AttributedTo.IsDefaultOrEmpty ? null : string.Join(", ", diff.Diff.AttributedTo));
                });
        return (withDiffs, end + 1);
    }
}
