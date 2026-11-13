using System.Collections.Immutable;
using RecDiffy.Excel.Model;
using RecDiffy.Results;

namespace RecDiffy.Excel.Reports;

// MO2. Ported from build_xlsx.py (MO2 scorecard): KPI tiles and a table by counterparty, every number a formula over Result_Entries and Result_Rows.
internal static class Scorecard
{
    public static ReportDefinition Definition { get; } = new(ReportKind.Scorecard, [ReportRole.Counterparty], _ => null, Build);

    private static ReportBody Build(ReportContext context)
    {
        var info = ReportCatalogue.Of(ReportKind.Scorecard);
        var entries = DataTabs.Entries(context);
        var rows = DataTabs.Rows(context);
        var source = rows.Range("source");
        var rowCounterparty = rows.Range("counterparty");
        var disposition = rows.Range("disposition");
        var lead = rows.Range("itemLead");
        var id = entries.Range("entryId");
        var alignment = entries.Range("alignment");
        var outcome = entries.Range("outcome");
        var committed = entries.Range("committedBy");
        var counterparty = entries.Range("counterparty");
        var leftRows = $"COUNTIF({source},\"left\")";
        var matched = $"COUNTIF({alignment},\"Matched\")";

        ImmutableArray<(string Label, string Formula, string Format)> tiles =
        [
            ("Internal rows", leftRows, Formats.Count),
            ("Counterparty rows", $"COUNTIF({source},\"right\")", Formats.Count),
            ("Result entries", $"COUNTIF({id},\"?*\")", Formats.Count),
            ("Matched entries", matched, Formats.Count),
            ("Auto-match rate", $"IF({leftRows}=0,0,COUNTIFS({source},\"left\",{disposition},\"Matched\")/{leftRows})", Formats.Percent),
            ("Clean-match rate", $"IF({matched}=0,0,(COUNTIF({outcome},\"Exact\")+COUNTIF({outcome},\"WithinTolerance\"))/{matched})", Formats.Percent),
            ("Breaks", $"COUNTIF({outcome},\"Break\")", Formats.Count),
            ("Unmatched ours", $"COUNTIFS({source},\"left\",{disposition},\"LeftOnly\",{lead},1)", Formats.Count),
            ("Unmatched theirs", $"COUNTIFS({source},\"right\",{disposition},\"RightOnly\",{lead},1)", Formats.Count),
            ("Ambiguous", $"COUNTIF({alignment},\"Ambiguous\")", Formats.Count),
        ];
        var r0 = ReportSheet.Body;
        var passRow = r0 + 3 * ((tiles.Length + 4) / 5);
        var passes = context.Facts.Passes;
        var header = passRow + 4;
        var counterparties = context.Facts.Rows
            .GroupBy(row => row.Values[ReportRole.Counterparty].Text, StringComparer.Ordinal)
            .Select(group => (Name: group.Key, Rate: Rate(group.Count(row => row.Source == "left" && row.Row.Disposition is Disposition.Matched), group.Count(row => row.Source == "left"))))
            .OrderByDescending(item => item.Rate)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .Select(item => item.Name)
            .ToImmutableArray();
        var first = header + 1;
        var last = Math.Max(first, header + counterparties.Length);

        var sheet = ReportSheet.Head(context, info)
            .Fold(tiles, (current, tile, index) =>
            {
                var column = 1 + index % 5 * 2;
                var row = r0 + index / 5 * 3;
                return current
                    .Put(row, column, context.Label(tile.Label), new CellStyle(Color: Palette.Muted, Fill: Palette.Card))
                    .Put(row, column + 1, null, new CellStyle(Fill: Palette.Card))
                    .Merge(row, column, row, column + 1)
                    .PutFormula(row + 1, column, tile.Formula, new CellStyle(Bold: true, Size: 16, Fill: Palette.Card, Format: tile.Format, Align: HorizontalAlign.Left))
                    .Put(row + 1, column + 1, null, new CellStyle(Fill: Palette.Card))
                    .Merge(row + 1, column, row + 1, column + 1)
                    .Height(row + 1, 26);
            })
            .Put(passRow, 1, context.Label("Commits by pass"), Styles.Section)
            .Fold(passes, (current, pass, index) => current
                .Put(passRow + 1, 1 + index, pass, Styles.SubHeader)
                .PutFormula(passRow + 2, 1 + index, $"COUNTIF({committed},\"{pass}\")", new CellStyle(Format: Formats.Count, Align: HorizontalAlign.Center)))
            .Header(header, context.Labels("Counterparty", "Internal rows", "Counterparty rows", "Matched entries", "Exact", "Within tolerance", "Breaks", "Unmatched ours", "Unmatched theirs", "Ambiguous", "Auto-match rate", "Clean-match rate"))
            .Fold(counterparties, (current, name, index) =>
            {
                var row = first + index;
                string Entries(string range, string value) => $"COUNTIFS({counterparty},$A{row.ToInvariant()},{range},\"{value}\")";
                return current
                    .Put(row, 1, name, Styles.Bold)
                    .PutFormula(row, 2, $"COUNTIFS({source},\"left\",{rowCounterparty},$A{row.ToInvariant()})", Styles.Number(Formats.Count))
                    .PutFormula(row, 3, $"COUNTIFS({source},\"right\",{rowCounterparty},$A{row.ToInvariant()})", Styles.Number(Formats.Count))
                    .PutFormula(row, 4, Entries(alignment, "Matched"), Styles.Number(Formats.Count))
                    .PutFormula(row, 5, Entries(outcome, "Exact"), Styles.Number(Formats.Count))
                    .PutFormula(row, 6, Entries(outcome, "WithinTolerance"), Styles.Number(Formats.Count))
                    .PutFormula(row, 7, Entries(outcome, "Break"), Styles.Number(Formats.Count))
                    .PutFormula(row, 8, $"COUNTIFS({source},\"left\",{rowCounterparty},$A{row.ToInvariant()},{disposition},\"LeftOnly\",{lead},1)", Styles.Number(Formats.Count))
                    .PutFormula(row, 9, $"COUNTIFS({source},\"right\",{rowCounterparty},$A{row.ToInvariant()},{disposition},\"RightOnly\",{lead},1)", Styles.Number(Formats.Count))
                    .PutFormula(row, 10, Entries(alignment, "Ambiguous"), Styles.Number(Formats.Count))
                    .PutFormula(row, 11, $"IF(B{row.ToInvariant()}=0,0,COUNTIFS({source},\"left\",{rowCounterparty},$A{row.ToInvariant()},{disposition},\"Matched\")/B{row.ToInvariant()})", Styles.Number(Formats.Percent))
                    .PutFormula(row, 12, $"IF(D{row.ToInvariant()}=0,0,(E{row.ToInvariant()}+F{row.ToInvariant()})/D{row.ToInvariant()})", Styles.Number(Formats.Percent));
            })
            .Put(last + 1, 1, context.Label("Total"), Styles.Total)
            .Fold(Enumerable.Range(2, 9), (current, column, _) => current.PutFormula(last + 1, column, $"SUM({Address.Column(column)}{first.ToInvariant()}:{Address.Column(column)}{last.ToInvariant()})", Styles.Total with { Format = Formats.Count }))
            .PutFormula(last + 1, 11, $"IF(AND(B{(last + 1).ToInvariant()}={leftRows},C{(last + 1).ToInvariant()}=COUNTIF({source},\"right\"),D{(last + 1).ToInvariant()}={matched}),\"✓\",\"✗\")", Styles.Total with { Align = HorizontalAlign.Center })
            .Put(last + 1, 12, null, Styles.Total)
            .Put(last + 3, 1, "Every number on this tab is a formula over Result_Entries and Result_Rows. Auto-match rate = internal rows in matched entries ÷ internal rows. Clean-match rate = (exact + within tolerance) ÷ matched entries. Unmatched counts are items from Result_Rows, so rows the engine left without an entry (not routed, or excluded as invalid) are included; a package counts once. Entry counts use the counterparty of the entry's projection.", Styles.Note)
            .Rule(new DataBarRule(new CellRange(first, 11, last, 11), "60A5FA", 0m, 1m))
            .Rule(new CellIsRule(new CellRange(first, 12, last, 12), CellOperator.GreaterThanOrEqual, ["0.95"], new DiffStyle(Palette.OkText, true, false, Palette.Ok)))
            .Rule(new CellIsRule(new CellRange(first, 12, last, 12), CellOperator.Between, ["0.85", "0.9499999"], Styles.Amber))
            .Rule(new CellIsRule(new CellRange(first, 12, last, 12), CellOperator.LessThan, ["0.85"], Styles.Red))
            .CheckRules(new CellRange(last + 1, 11, last + 1, 11))
            .Widths([14, 12, 13, 12, 10, 12, 10, 13, 13, 11, 14, 14])
            .FreezeAt(first, 2)
            .Filter(header, 1, last, 12);
        return new ReportBody(sheet, [entries, rows]);
    }

    private static decimal Rate(int part, int whole) => whole == 0 ? 0m : (decimal)part / whole;
}
