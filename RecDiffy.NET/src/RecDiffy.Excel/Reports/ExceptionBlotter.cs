using System.Collections.Immutable;
using RecDiffy.Excel.Facts;
using RecDiffy.Excel.Model;

namespace RecDiffy.Excel.Reports;

// BO1. Layout from build_xlsx.py (BO1 blotter), calculations from build_views.py: one line per open item (a Break entry on its root field,
// or an unmatched, ambiguous or search-incomplete entry as a whole trade), ranked by settlement status, currency and impact.
internal static class ExceptionBlotter
{
    private sealed record Amounts(object? Ours, object? Theirs, object? Delta, string Format, string DeltaFormat);

    private const int Columns = 19;

    public static ReportDefinition Definition { get; } = new(
        ReportKind.ExceptionBlotter,
        [ReportRole.Counterparty, ReportRole.Instrument, ReportRole.Side, ReportRole.TradeDate, ReportRole.SettleDate, ReportRole.Currency],
        _ => null,
        Build);

    private static ReportBody Build(ReportContext context)
    {
        var info = ReportCatalogue.Of(ReportKind.ExceptionBlotter);
        var entries = DataTabs.Entries(context);
        var diffs = DataTabs.Diffs(context);
        var items = context.Facts.OpenItems;
        ImmutableArray<string> statuses =
        [
            "Past settle date", "Settles today", "Upcoming",
            .. items.Any(item => item.SettleDate.Date is null) ? ["No settle date"] : Array.Empty<string>(),
        ];
        ImmutableArray<string> types =
        [
            "Field break", "Unmatched ours", "Unmatched theirs",
            .. new[] { "Ambiguous", "Search incomplete" }.Where(type => items.Any(item => item.ItemType == type)),
        ];
        var r0 = ReportSheet.Body;
        var summaryRows = Math.Max(statuses.Length, types.Length);
        var total = r0 + 1 + summaryRows;
        var header = total + 2;
        var first = header + 1;
        var last = Math.Max(first, header + items.Length);
        var netCash = context.Fields.IsMapped(ReportRole.NetCash);

        var sheet = ReportSheet.Head(context, info)
            .Put(r0, 3, context.Label("Open items by status"), Styles.Section)
            .Put(r0, 6, context.Label("Open items by type"), Styles.Section)
            .Fold(statuses, (current, status, index) => current
                .Put(r0 + 1 + index, 3, status)
                .PutFormula(r0 + 1 + index, 4, $"COUNTIF($C${first}:$C${last},C{r0 + 1 + index})", Styles.Number(Formats.Count)))
            .Fold(types, (current, type, index) => current
                .Put(r0 + 1 + index, 6, type)
                .PutFormula(r0 + 1 + index, 7, $"COUNTIF($D${first}:$D${last},F{r0 + 1 + index})", Styles.Number(Formats.Count)))
            .Put(total, 3, context.Label("Total"), Styles.Total)
            .PutFormula(total, 4, $"SUM(D{r0 + 1}:D{r0 + statuses.Length})", Styles.Total with { Format = Formats.Count })
            .Put(total, 6, context.Label("Total"), Styles.Total)
            .PutFormula(total, 7, $"SUM(G{r0 + 1}:G{r0 + types.Length})", Styles.Total with { Format = Formats.Count })
            .PutFormula(total, 8, $"IF(AND(D{total}=G{total},D{total}=ROWS($A${first}:$A${last})-COUNTBLANK($A${first}:$A${last})),\"✓\",\"✗\")", Styles.Check)
            .Header(header, context.Labels(
                "Rank", "Entry", "Status", "Item type", "Counterparty", "Instrument", "Asset", "Side", "Trade date", "Settle date", "Age (bus. days)",
                "Field", "Ours", "Theirs", "Delta", "Ccy", "Impact", "Suggested action", "Category"))
            .Fold(items, (current, item, index) => Line(current, context, entries, diffs, item, first + index, index + 1, netCash))
            .Put(last + 2, 1, "Impact: full net cash for a settle-date break or an unmatched item; otherwise the absolute net-cash difference. Ranked within currency only (no FX rates are applied). Status and age are formulas on the as-of date (Run_Info!AsOf). Category feeds MO4 and MO5.", Styles.Note)
            .Rule(new CellIsRule(new CellRange(first, 3, last, 3), CellOperator.Equal, ["\"Past settle date\""], Styles.Red))
            .Rule(new CellIsRule(new CellRange(first, 3, last, 3), CellOperator.Equal, ["\"Settles today\""], Styles.Amber))
            .Rule(new CellIsRule(new CellRange(first, 3, last, 3), CellOperator.Equal, ["\"Upcoming\""], new DiffStyle(FontColor: Palette.Muted, Fill: Palette.Attributed)))
            .CheckRules(new CellRange(total, 8, total, 8))
            .Widths([6, 13, 16, 15, 12, 18, 9, 10, 11, 11, 9, 13, 14, 14, 12, 6, 15, 44, 14])
            .FreezeAt(first, 3)
            .Filter(header, 1, last, Columns);
        return new ReportBody(sheet, [entries, diffs]);
    }

    private static Sheet Line(Sheet sheet, ReportContext context, DataTab entries, DataTab diffs, OpenItem item, int row, int rank, bool netCash)
    {
        var entry = item.Entry;
        var settleKey = item.ItemType switch
        {
            "Unmatched theirs" => DataTabs.Theirs(ReportRole.SettleDate),
            "Ambiguous" or "Search incomplete" => "settleDate",
            _ => DataTabs.Ours(ReportRole.SettleDate),
        };
        var amountRole = netCash ? ReportRole.NetCash : ReportRole.Quantity;
        string Entry(string key) => ReportSheet.Link(entries.Cell(key, entry.Index));
        var values = (item.ItemType, item.Diff) switch
        {
            ("Field break", { } diff) => new Amounts(
                Content.Formula(ReportSheet.Link(diffs.Cell("ours", diff.Index))),
                Content.Formula(ReportSheet.Link(diffs.Cell("theirs", diff.Index))),
                diff.Delta is null ? null : Content.Formula(ReportSheet.Link(diffs.Cell("delta", diff.Index))),
                DataTabs.FormatFor(diff.Ours, diff.Role),
                diff.Diff.Delta.Kind is Results.DeltaKind.Days ? Formats.Integer : DataTabs.FormatFor(diff.Role)),
            ("Unmatched ours", _) => new Amounts(Content.Formula(Entry(DataTabs.Ours(amountRole))), null, null, DataTabs.FormatFor(amountRole), Formats.Amount),
            ("Unmatched theirs", _) => new Amounts(null, Content.Formula(Entry(DataTabs.Theirs(amountRole))), null, DataTabs.FormatFor(amountRole), Formats.Amount),
            _ => new Amounts(null, null, null, Formats.Amount, Formats.Amount),
        };
        var cashDiff = context.Facts.DiffsByEntry[entry.Id].FirstOrDefault(diff => diff.Role is ReportRole.NetCash);
        object? impact = (netCash, item.ItemType, item.Diff?.Role) switch
        {
            (false, _, _) => null,
            (true, "Field break", ReportRole.SettleDate) => Content.Formula($"ABS({entries.Cell(DataTabs.Ours(ReportRole.NetCash), entry.Index)})"),
            (true, "Field break", _) when cashDiff is not null => Content.Formula($"ABS({diffs.Cell("delta", cashDiff.Index)})"),
            (true, "Field break", _) => 0m,
            (true, "Unmatched ours", _) => Content.Formula($"ABS({entries.Cell(DataTabs.Ours(ReportRole.NetCash), entry.Index)})"),
            (true, "Unmatched theirs", _) => Content.Formula($"ABS({entries.Cell(DataTabs.Theirs(ReportRole.NetCash), entry.Index)})"),
            _ => null,
        };
        return sheet
            .Put(row, 1, rank)
            .Put(row, 2, entry.Short)
            .PutFormula(row, 3, DataTabs.StatusFormula($"J{row}"))
            .Put(row, 4, item.ItemType)
            .Put(row, 5, entry.Text(ReportRole.Counterparty))
            .Put(row, 6, entry.Text(ReportRole.Instrument))
            .Put(row, 7, entry.Text(ReportRole.Asset))
            .Put(row, 8, entry.Text(ReportRole.Side))
            .PutFormula(row, 9, Entry("tradeDate"), Styles.Link(Formats.Date))
            .PutFormula(row, 10, Entry(settleKey), Styles.Link(Formats.Date))
            .PutFormula(row, 11, DataTabs.AgeFormula($"I{row}"), Styles.Number(Formats.Integer))
            .Put(row, 12, item.Field)
            .Put(row, 13, values.Ours, Styles.Link(values.Format))
            .Put(row, 14, values.Theirs, Styles.Link(values.Format))
            .Put(row, 15, values.Delta, Styles.Link(values.DeltaFormat))
            .Put(row, 16, entry.Text(ReportRole.Currency))
            .Put(row, 17, impact, impact is FormulaContent ? Styles.Link(Formats.Amount) : Styles.Number(Formats.Amount))
            .Put(row, 18, item.Action)
            .PutFormula(row, 19, $"IF(D{row}=\"Field break\",L{row},D{row})");
    }
}
