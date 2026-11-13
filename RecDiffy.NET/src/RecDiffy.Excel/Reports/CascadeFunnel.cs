using System.Collections.Immutable;
using RecDiffy.Excel.Model;
using RecDiffy.Results;

namespace RecDiffy.Excel.Reports;

// MO3. Layout from build_xlsx.py (MO3 funnel): rows read, lifecycle collapse (Suppressed rows), quarantine, items after package consolidation,
// the items each pass committed in pass order, unresolved items, and the residue, separately for ours and theirs, with a bar chart and a
// check that what is left after the last stage equals the residue. Every count is a formula on Result_Rows; an item is a consolidated row
// (a package counts once), marked by Result_Rows.itemLead.
internal static class CascadeFunnel
{
    public static ReportDefinition Definition { get; } = new(ReportKind.CascadeFunnel, [], _ => null, Build);

    private sealed record Stage(string Name, Func<string, string>? Change, Func<string, string>? Remaining, string Note, bool Residue = false);

    private static ReportBody Build(ReportContext context)
    {
        var info = ReportCatalogue.Of(ReportKind.CascadeFunnel);
        var rows = DataTabs.Rows(context);
        var source = rows.Range("source");
        var disposition = rows.Range("disposition");
        var lead = rows.Range("itemLead");
        var committed = rows.Range("committedBy");
        var facts = context.Facts.Rows;
        var anyQuarantined = facts.Any(row => row.Row.Disposition is Disposition.Quarantined);
        var anyUnresolved = facts.Any(row => row.Row.Disposition is Disposition.Ambiguous or Disposition.SearchIncomplete);
        string Items(string side, string? only) => only is null ? $"SUMIFS({lead},{source},\"{side}\")" : $"SUMIFS({lead},{source},\"{side}\",{disposition},\"{only}\")";
        string Side(string column) => column == "C" ? "left" : "right";

        ImmutableArray<Stage> stages =
        [
            new("Rows read", null, column => $"COUNTIF({source},\"{Side(column)}\")", "Every input row"),
            new("After lifecycle collapse", column => $"-COUNTIFS({source},\"{Side(column)}\",{disposition},\"Suppressed\")", null, "Superseded and cancel rows of a chain are suppressed"),
            .. anyQuarantined
                ? [new Stage("After quarantine", column => $"-COUNTIFS({source},\"{Side(column)}\",{disposition},\"Quarantined\")", null, "Rows the engine quarantined (duplicate conflicts, broken chains or packages)")]
                : Array.Empty<Stage>(),
            new("Items after packages", null, column => $"{Items(Side(column), null)}-{Items(Side(column), "Suppressed")}-{Items(Side(column), "Quarantined")}", "The legs of a package become one item"),
            .. context.Facts.Passes.Select(pass => new Stage(
                $"{pass}",
                column => $"-SUMIFS({lead},{source},\"{Side(column)}\",{committed},\"{pass}\",{disposition},\"Matched\")",
                null,
                $"commits {context.Facts.Entries.Count(entry => entry.Entry.Explanation.CommittedBy == pass && entry.IsMatched)} entries")),
            .. anyUnresolved
                ? [new Stage("Unresolved", column => $"-{Items(Side(column), "Ambiguous")}-{Items(Side(column), "SearchIncomplete")}", null, "Ambiguous or search-incomplete items")]
                : Array.Empty<Stage>(),
            new("Residue", null, column => Items(Side(column), column == "C" ? "LeftOnly" : "RightOnly"), "Unmatched ours and unmatched theirs", Residue: true),
        ];
        var header = ReportSheet.Body;
        var first = header + 1;
        var residue = header + stages.Length;
        var lastStage = residue - 1;
        var check = residue + 1;

        var sheet = ReportSheet.Head(context, info)
            .Header(header, context.Labels("Stage", "Ours: change", "Ours: remaining", "Theirs: change", "Theirs: remaining", "Note"))
            .Fold(stages, (current, stage, index) =>
            {
                var row = first + index;
                var fill = stage.Residue ? Palette.Tolerance : null;
                return current
                    .Put(row, 1, context.Label(stage.Name), Styles.Bold)
                    .Fold(new[] { ("B", "C"), ("D", "E") }, (inner, columns, position) =>
                    {
                        var changeColumn = 2 + position * 2;
                        var remaining = stage switch
                        {
                            { Remaining: { } formula } => formula(columns.Item2),
                            _ => $"{columns.Item2}{(row - 1).ToInvariant()}+{columns.Item1}{row.ToInvariant()}",
                        };
                        var change = stage switch
                        {
                            { Change: { } formula } => formula(columns.Item2),
                            { Residue: false } when index > 0 => $"{columns.Item2}{row.ToInvariant()}-{columns.Item2}{(row - 1).ToInvariant()}",
                            _ => null,
                        };
                        var withChange = change is null
                            ? inner.Put(row, changeColumn, null, Styles.Number(Formats.SignedCount))
                            : inner.PutFormula(row, changeColumn, change, Styles.Number(Formats.SignedCount));
                        return withChange.PutFormula(row, changeColumn + 1, remaining, new CellStyle(Bold: true, Fill: fill, Format: Formats.Count));
                    })
                    .Put(row, 6, stage.Note, new CellStyle(Color: Palette.Muted));
            })
            .Put(check, 1, context.Label("Check: remaining after the last stage = residue"), Styles.Bold)
            .PutFormula(check, 3, $"IF(C{lastStage.ToInvariant()}=C{residue.ToInvariant()},\"✓\",\"✗\")", Styles.Check)
            .PutFormula(check, 5, $"IF(E{lastStage.ToInvariant()}=E{residue.ToInvariant()},\"✓\",\"✗\")", Styles.Check)
            .CheckRules(new CellRange(check, 3, check, 5))
            .Chart(new BarChart(
                context.Label("Items remaining after each stage"),
                new CellAddress(check + 3, 1),
                18,
                9,
                $"{Address.Sheet(info.Tab)}!$A${first.ToInvariant()}:$A${residue.ToInvariant()}",
                [
                    new ChartSeries($"{Address.Sheet(info.Tab)}!$C${header.ToInvariant()}", $"{Address.Sheet(info.Tab)}!$C${first.ToInvariant()}:$C${residue.ToInvariant()}", "1F2937"),
                    new ChartSeries($"{Address.Sheet(info.Tab)}!$E${header.ToInvariant()}", $"{Address.Sheet(info.Tab)}!$E${first.ToInvariant()}:$E${residue.ToInvariant()}", "60A5FA"),
                ],
                context.Label("Items"),
                ReverseCategories: true))
            .Widths([34, 13, 15, 13, 15, 60]);
        return new ReportBody(sheet, [rows]);
    }
}
