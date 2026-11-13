using System.Collections.Immutable;
using RecDiffy.Excel.Model;

namespace RecDiffy.Excel.Reports;

// MO4. Layout from build_xlsx.py (MO4 heatmap): counterparty by break category, each cell a COUNTIFS over this file's own Open_Items tab
// (the same open items as the BO1 blotter), shaded on a single-hue scale, with row and column totals tied to the item count.
internal static class BreakHeatmap
{
    private static readonly ImmutableArray<ReportRole> FieldOrder =
        [ReportRole.Price, ReportRole.Quantity, ReportRole.SettleDate, ReportRole.Fees, ReportRole.Accrued, ReportRole.NetCash, ReportRole.Currency, ReportRole.TradeDate];

    private static readonly ImmutableArray<string> WholeTrade = ["Unmatched ours", "Unmatched theirs", "Ambiguous", "Search incomplete"];

    public static ReportDefinition Definition { get; } = new(ReportKind.BreakHeatmap, [ReportRole.Counterparty], _ => null, Build);

    /// <summary>Break categories: root fields in a fixed order, then the whole-trade item types. Unmatched ours and theirs always show.</summary>
    public static ImmutableArray<string> Categories(ReportContext context)
    {
        var present = context.Facts.OpenItems.Select(item => item.Category).ToImmutableHashSet(StringComparer.Ordinal);
        var ordered = FieldOrder.Select(context.Field).OfType<string>().ToImmutableArray();
        return
        [
            .. ordered.Where(present.Contains),
            .. present.Except(ordered).Except(WholeTrade).Order(StringComparer.Ordinal),
            .. WholeTrade.Where(type => type is "Unmatched ours" or "Unmatched theirs" || present.Contains(type)),
        ];
    }

    private static ReportBody Build(ReportContext context)
    {
        var info = ReportCatalogue.Of(ReportKind.BreakHeatmap);
        var items = DataTabs.OpenItems(context);
        var categories = Categories(context);
        var counterparties = context.Facts.OpenItems
            .GroupBy(item => item.Text(ReportRole.Counterparty), StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key)
            .ToImmutableArray();
        var header = ReportSheet.Body;
        var first = header + 1;
        var total = first + counterparties.Length;
        var totalColumn = 2 + categories.Length;
        var counterparty = items.Range("counterparty");
        var category = items.Range("category");

        var sheet = ReportSheet.Head(context, info)
            .Header(header, [context.Label("Counterparty"), .. categories.Select(context.Label), context.Label("Total")])
            .Fold(counterparties, (current, name, index) =>
            {
                var row = first + index;
                return current
                    .Put(row, 1, name, Styles.Bold)
                    .Fold(categories, (inner, code, position) => inner.PutFormula(
                        row,
                        2 + position,
                        $"COUNTIFS({counterparty},$A{row.ToInvariant()},{category},\"{code.Replace("\"", "\"\"", StringComparison.Ordinal)}\")",
                        new CellStyle(Format: Formats.Count, Align: HorizontalAlign.Center)))
                    .PutFormula(row, totalColumn, $"SUM(B{row.ToInvariant()}:{Address.Column(totalColumn - 1)}{row.ToInvariant()})", new CellStyle(Bold: true, Format: Formats.Count, Align: HorizontalAlign.Center));
            })
            .Put(total, 1, context.Label("Total"), Styles.Total)
            .Fold(Enumerable.Range(2, categories.Length + 1), (current, column, _) => current.PutFormula(
                total,
                column,
                $"SUM({Address.Column(column)}{first.ToInvariant()}:{Address.Column(column)}{Math.Max(first, total - 1).ToInvariant()})",
                Styles.Total with { Format = Formats.Count, Align = HorizontalAlign.Center }))
            .PutFormula(total, totalColumn + 1, $"IF({Address.Column(totalColumn)}{total.ToInvariant()}=COUNTIF({items.Range("entryId")},\"?*\"),\"✓\",\"✗\")", Styles.Check)
            .Put(header, totalColumn + 1, context.Label("Check"), Styles.Header)
            .Put(total + 2, 1, "Counts are COUNTIFS formulas over Open_Items (counterparty × category); the check ties the grand total to the number of open items.", Styles.Note)
            .Rule(new ColorScaleRule(new CellRange(first, 2, total - 1, totalColumn - 1), "FFFFFF", "C2410C"))
            .CheckRules(new CellRange(total, totalColumn + 1, total, totalColumn + 1))
            .Widths([16, .. categories.Select(_ => 13.0), 9, 8])
            .FreezeAt(first, 2);
        return new ReportBody(sheet, [items]);
    }
}
