using System.Collections.Immutable;
using System.Globalization;
using RecDiffy.Excel.Model;
using RecDiffy.Results;

namespace RecDiffy.Excel.Reports;

// MO6. Layout from build_xlsx.py (MO6 tolerance usage): every diff the engine judged WithinTolerance that no other field explains, with the share of
// its tolerance it used. The engine reports a tolerance by kind; the amount is read from the profile's diff declaration (ExcelReportOptions.Profile).
internal static class ToleranceUsage
{
    public static ReportDefinition Definition { get; } = new(ReportKind.ToleranceUsage, [], Unavailable, Build);

    private static string? Unavailable(ReportContext context) => (context.Options.Profile, Tolerated(context).IsEmpty) switch
    {
        (null, _) => "Needs ExcelReportOptions.Profile: ReconciliationResult reports a tolerance by kind only, so the tolerance amount comes from the profile.",
        (_, true) => "No within-tolerance differences in this run.",
        _ => null,
    };

    private static ImmutableArray<Facts.DiffFact> Tolerated(ReportContext context) =>
        [.. context.Facts.Diffs
            .Where(diff => diff.Diff.Outcome is FieldOutcome.WithinTolerance && diff.Diff.AttributedTo.IsDefaultOrEmpty && diff.ToleranceAmount is > 0m)
            .OrderByDescending(diff => Math.Abs(diff.Delta ?? 0m) / diff.ToleranceAmount!.Value)
            .ThenBy(diff => diff.Entry.Id, StringComparer.Ordinal)
            .ThenBy(diff => diff.Field, StringComparer.Ordinal)];

    private static string Declared(CompiledProfile profile) =>
        string.Join("  ·  ", profile.Document.Paths
            .SelectMany(path => (path.Diff?.Fields ?? new Dictionary<string, FieldDiffDeclaration>(StringComparer.Ordinal)).Select(field => (Path: path.Id, Field: field.Key, field.Value.Tolerance)))
            .Where(item => item.Tolerance.Kind is "Absolute" or "Relative" or "AbsoluteOrRelative")
            .Select(item => $"{item.Field} ({item.Path}): " + item.Tolerance.Kind switch
            {
                "Absolute" => $"absolute {Text(item.Tolerance.Absolute)}",
                "Relative" => $"relative {Text(item.Tolerance.Relative)}",
                _ => $"absolute {Text(item.Tolerance.Absolute)} or relative {Text(item.Tolerance.Relative)}",
            }));

    private static string Text(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";

    private static ReportBody Build(ReportContext context)
    {
        var info = ReportCatalogue.Of(ReportKind.ToleranceUsage);
        var diffs = DataTabs.Diffs(context);
        var rows = Tolerated(context);
        var note = ReportSheet.Body;
        var header = note + 1;
        var first = header + 1;
        var last = Math.Max(first, header + rows.Length);

        var sheet = ReportSheet.Head(context, info)
            .Put(note, 1, $"Tolerances ({context.ProfileId}): {Declared(context.Options.Profile!)}. Boundaries are inclusive.", Styles.Muted)
            .Header(header, context.Labels("Entry", "Counterparty", "Instrument", "Field", "Ours", "Theirs", "Delta", "Tolerance", "% of tolerance used", "Flag"))
            .Fold(rows, (current, diff, index) =>
            {
                var row = first + index;
                var format = DataTabs.FormatFor(diff.Role);
                return current
                    .Put(row, 1, diff.Entry.Short)
                    .Put(row, 2, diff.Entry.Text(ReportRole.Counterparty))
                    .Put(row, 3, diff.Entry.Text(ReportRole.Instrument))
                    .Put(row, 4, diff.Field)
                    .PutFormula(row, 5, ReportSheet.Link(diffs.Cell("ours", diff.Index)), Styles.Link(format))
                    .PutFormula(row, 6, ReportSheet.Link(diffs.Cell("theirs", diff.Index)), Styles.Link(format))
                    .PutFormula(row, 7, ReportSheet.Link(diffs.Cell("delta", diff.Index)), Styles.Link(format))
                    .PutFormula(row, 8, ReportSheet.Link(diffs.Cell("toleranceAmount", diff.Index)), Styles.Link(Formats.Price))
                    .PutFormula(row, 9, $"IF(H{row.ToInvariant()}=0,0,ABS(G{row.ToInvariant()})/H{row.ToInvariant()})", Styles.Number(Formats.Percent, bold: true))
                    .PutFormula(row, 10, $"IF(I{row.ToInvariant()}>=1,\"On the boundary\",\"\")", new CellStyle(Color: Palette.ToleranceText));
            })
            .PutFormula(last + 1, 9, $"IF(MAX(I{first.ToInvariant()}:I{last.ToInvariant()})<=1,\"✓\",\"✗\")", Styles.Check)
            .Put(last + 1, 1, context.Label("Check: no tolerated difference exceeds its tolerance"), Styles.Bold)
            .Rule(new DataBarRule(new CellRange(first, 9, last, 9), "F59E0B", 0m, 1m))
            .Rule(new CellIsRule(new CellRange(first, 10, last, 10), CellOperator.Equal, ["\"On the boundary\""], new DiffStyle(Fill: Palette.Tolerance)))
            .CheckRules(new CellRange(last + 1, 9, last + 1, 9))
            .Widths([13, 13, 22, 11, 14, 14, 12, 11, 18, 16])
            .FreezeAt(first, 2)
            .Filter(header, 1, last, 10);
        return new ReportBody(sheet, [diffs]);
    }
}
