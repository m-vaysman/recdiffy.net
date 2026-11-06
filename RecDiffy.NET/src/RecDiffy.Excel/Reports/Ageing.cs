using System.Collections.Immutable;
using RecDiffy.Excel.Model;

namespace RecDiffy.Excel.Reports;

// MO5. Layout from build_xlsx.py (MO5 ageing): open items by category, aged in business days from trade date to the as-of date, with their
// settlement status. Age and status are formulas on AsOf in this file's own Open_Items tab, so changing Run_Info!AsOf re-ages the report.
// Two checks tie each category's age buckets and its status columns back to its item count.
internal static class Ageing
{
    private static readonly ImmutableArray<(string Label, string Criteria)> Ages =
    [
        ("Age 0 or less", "\"<=0\""), ("Age 1 bus. day", "1"), ("Age 2 bus. days", "2"), ("Age 3 bus. days", "3"), ("Age 4+ bus. days", "\">=4\""),
    ];

    private static readonly ImmutableArray<string> Statuses = ["Past settle date", "Settles today", "Upcoming", "No settle date"];

    public static ReportDefinition Definition { get; } = new(ReportKind.Ageing, [ReportRole.TradeDate, ReportRole.SettleDate], _ => null, Build);

    private static ReportBody Build(ReportContext context)
    {
        var info = ReportCatalogue.Of(ReportKind.Ageing);
        var items = DataTabs.OpenItems(context);
        var categories = BreakHeatmap.Categories(context);
        var category = items.Range("category");
        var age = items.Range("ageBD");
        var status = items.Range("status");
        var header = ReportSheet.Body;
        var first = header + 1;
        var total = first + categories.Length;
        var statusColumn = 2 + Ages.Length;
        var totalColumn = statusColumn + Statuses.Length;
        var other = totalColumn - 1;

        var sheet = ReportSheet.Head(context, info)
            .Header(header, [
                context.Label("Category"),
                .. Ages.Select(bucket => context.Label(bucket.Label)),
                .. Statuses.Select(context.Label),
                context.Label("Items"),
                context.Label("Check"),
            ])
            .Fold(categories, (current, code, index) =>
            {
                var row = first + index;
                var name = $"\"{code.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
                return current
                    .Put(row, 1, code, Styles.Bold)
                    .Fold(Ages, (inner, bucket, position) => inner.PutFormula(row, 2 + position, $"COUNTIFS({category},$A{row},{age},{bucket.Criteria})", new CellStyle(Format: Formats.Count, Align: HorizontalAlign.Center)))
                    .Fold(Statuses, (inner, label, position) => inner.PutFormula(row, statusColumn + position, $"COUNTIFS({category},$A{row},{status},\"{label}\")", new CellStyle(Format: Formats.Count, Align: HorizontalAlign.Center)))
                    .PutFormula(row, totalColumn, $"COUNTIF({category},{name})", new CellStyle(Bold: true, Format: Formats.Count, Align: HorizontalAlign.Center))
                    .PutFormula(
                        row,
                        totalColumn + 1,
                        $"IF(AND(SUM(B{row}:{Address.Column(statusColumn - 1)}{row})+COUNTIFS({category},$A{row},{age},\"\")={Address.Column(totalColumn)}{row},SUM({Address.Column(statusColumn)}{row}:{Address.Column(other)}{row})={Address.Column(totalColumn)}{row}),\"✓\",\"✗\")",
                        Styles.Check);
            })
            .Put(total, 1, context.Label("Total"), Styles.Total)
            .Fold(Enumerable.Range(2, totalColumn - 1), (current, column, _) => current.PutFormula(
                total,
                column,
                $"SUM({Address.Column(column)}{first}:{Address.Column(column)}{Math.Max(first, total - 1)})",
                Styles.Total with { Format = Formats.Count, Align = HorizontalAlign.Center }))
            .PutFormula(total, totalColumn + 1, $"IF({Address.Column(totalColumn)}{total}=COUNTIF({items.Range("entryId")},\"?*\"),\"✓\",\"✗\")", Styles.Total with { Align = HorizontalAlign.Center })
            .Put(total + 2, 1, "Age = business days from trade date to the as-of date (Monday to Friday, no holiday calendar), computed in Open_Items. Change Run_Info!AsOf to re-age. The check ties each row's age buckets and its status columns to its item count.", Styles.Note)
            .Rule(new ColorScaleRule(new CellRange(first, 2, total - 1, statusColumn - 1), "FFFFFF", "6D28D9"))
            .Rule(new CellIsRule(new CellRange(first, statusColumn, total - 1, statusColumn), CellOperator.GreaterThan, ["0"], Styles.Red))
            .CheckRules(new CellRange(first, totalColumn + 1, total, totalColumn + 1))
            .Widths([18, .. Ages.Select(_ => 11.0), 15, 13, 11, 12, 8, 8])
            .FreezeAt(first, 2);
        return new ReportBody(sheet, [items]);
    }
}
