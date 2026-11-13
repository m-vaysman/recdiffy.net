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
        var rows = items.Any(item => item.Row is not null) ? DataTabs.Rows(context) : null;
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
                .PutFormula(r0 + 1 + index, 4, $"COUNTIF($C${first.ToInvariant()}:$C${last.ToInvariant()},C{(r0 + 1 + index).ToInvariant()})", Styles.Number(Formats.Count)))
            .Fold(types, (current, type, index) => current
                .Put(r0 + 1 + index, 6, type)
                .PutFormula(r0 + 1 + index, 7, $"COUNTIF($D${first.ToInvariant()}:$D${last.ToInvariant()},F{(r0 + 1 + index).ToInvariant()})", Styles.Number(Formats.Count)))
            .Put(total, 3, context.Label("Total"), Styles.Total)
            .PutFormula(total, 4, $"SUM(D{(r0 + 1).ToInvariant()}:D{(r0 + statuses.Length).ToInvariant()})", Styles.Total with { Format = Formats.Count })
            .Put(total, 6, context.Label("Total"), Styles.Total)
            .PutFormula(total, 7, $"SUM(G{(r0 + 1).ToInvariant()}:G{(r0 + types.Length).ToInvariant()})", Styles.Total with { Format = Formats.Count })
            .PutFormula(total, 8, $"IF(AND(D{total.ToInvariant()}=G{total.ToInvariant()},D{total.ToInvariant()}=ROWS($A${first.ToInvariant()}:$A${last.ToInvariant()})-COUNTBLANK($A${first.ToInvariant()}:$A${last.ToInvariant()})),\"✓\",\"✗\")", Styles.Check)
            .Header(header, context.Labels(
                "Rank", "Entry", "Status", "Item type", "Counterparty", "Instrument", "Asset", "Side", "Trade date", "Settle date", "Age (bus. days)",
                "Field", "Ours", "Theirs", "Delta", "Ccy", "Impact", "Suggested action", "Category"))
            .Fold(items, (current, item, index) => Line(current, context, entries, diffs, rows, item, first + index, index + 1, netCash))
            .Put(last + 2, 1, "Impact: full net cash for a settle-date break or an unmatched item; otherwise the absolute net-cash difference; blank when net cash is unknown. Ranked within currency only (no FX rates are applied). Rows the engine left without an entry (not routed, or excluded as invalid) are listed as unmatched. Status and age are formulas on the as-of date (Run_Info!AsOf). Category feeds MO4 and MO5.", Styles.Note)
            .Rule(new CellIsRule(new CellRange(first, 3, last, 3), CellOperator.Equal, ["\"Past settle date\""], Styles.Red))
            .Rule(new CellIsRule(new CellRange(first, 3, last, 3), CellOperator.Equal, ["\"Settles today\""], Styles.Amber))
            .Rule(new CellIsRule(new CellRange(first, 3, last, 3), CellOperator.Equal, ["\"Upcoming\""], new DiffStyle(FontColor: Palette.Muted, Fill: Palette.Attributed)))
            .CheckRules(new CellRange(total, 8, total, 8))
            .Widths([6, 13, 16, 15, 12, 18, 9, 10, 11, 11, 9, 13, 14, 14, 12, 6, 15, 44, 14])
            .FreezeAt(first, 3)
            .Filter(header, 1, last, Columns);
        return new ReportBody(sheet, [entries, diffs, .. rows is null ? Array.Empty<DataTab>() : [rows]]);
    }

    private static Sheet Line(Sheet sheet, ReportContext context, DataTab entries, DataTab diffs, DataTab? rows, OpenItem item, int row, int rank, bool netCash)
    {
        var amountRole = netCash ? ReportRole.NetCash : ReportRole.Quantity;
        var source = Source.Of(item, entries, rows);
        var values = (item.ItemType, item.Diff) switch
        {
            ("Field break", { } diff) => new Amounts(
                Content.Formula(ReportSheet.Link(diffs.Cell("ours", diff.Index))),
                Content.Formula(ReportSheet.Link(diffs.Cell("theirs", diff.Index))),
                diff.Delta is null ? null : Content.Formula(ReportSheet.Link(diffs.Cell("delta", diff.Index))),
                DataTabs.FormatFor(diff.Ours, diff.Role),
                diff.Diff.Delta.Kind is Results.DeltaKind.Days ? Formats.Integer : DataTabs.FormatFor(diff.Role)),
            ("Unmatched ours", _) => new Amounts(Content.Formula(ReportSheet.Link(source.Amount(amountRole, Side.Left))), null, null, DataTabs.FormatFor(amountRole), Formats.Amount),
            ("Unmatched theirs", _) => new Amounts(null, Content.Formula(ReportSheet.Link(source.Amount(amountRole, Side.Right))), null, DataTabs.FormatFor(amountRole), Formats.Amount),
            _ => new Amounts(null, null, null, Formats.Amount, Formats.Amount),
        };
        var cashDiff = item.Entry is { } entry ? context.Facts.DiffsByEntry[entry.Id].FirstOrDefault(diff => diff.Role is ReportRole.NetCash) : null;
        object? impact = (item.Impact, item.ItemType, item.Diff?.Role) switch
        {
            (null, _, _) => null,
            (_, "Field break", ReportRole.SettleDate) => Content.Formula($"ABS({source.Amount(ReportRole.NetCash, Side.Left)})"),
            (_, "Field break", _) when cashDiff is not null => Content.Formula($"ABS({diffs.Cell("delta", cashDiff.Index)})"),
            (_, "Field break", _) => 0m,
            (_, "Unmatched ours", _) => Content.Formula($"ABS({source.Amount(ReportRole.NetCash, Side.Left)})"),
            (_, "Unmatched theirs", _) => Content.Formula($"ABS({source.Amount(ReportRole.NetCash, Side.Right)})"),
            _ => null,
        };
        return sheet
            .Put(row, 1, rank)
            .Put(row, 2, item.Label)
            .PutFormula(row, 3, DataTabs.StatusFormula($"J{row.ToInvariant()}"))
            .Put(row, 4, item.ItemType)
            .Put(row, 5, item.Text(ReportRole.Counterparty))
            .Put(row, 6, item.Text(ReportRole.Instrument))
            .Put(row, 7, item.Text(ReportRole.Asset))
            .Put(row, 8, item.Text(ReportRole.Side))
            .PutFormula(row, 9, ReportSheet.Link(source.TradeDate), Styles.Link(Formats.Date))
            .PutFormula(row, 10, ReportSheet.Link(source.SettleDate), Styles.Link(Formats.Date))
            .PutFormula(row, 11, DataTabs.AgeFormula($"I{row.ToInvariant()}"), Styles.Number(Formats.Integer))
            .Put(row, 12, item.Field)
            .Put(row, 13, values.Ours, Styles.Link(values.Format))
            .Put(row, 14, values.Theirs, Styles.Link(values.Format))
            .Put(row, 15, values.Delta, Styles.Link(values.DeltaFormat))
            .Put(row, 16, item.Text(ReportRole.Currency))
            .Put(row, 17, impact, impact is FormulaContent ? Styles.Link(Formats.Amount) : Styles.Number(Formats.Amount))
            .Put(row, 18, item.Action)
            .PutFormula(row, 19, $"IF(D{row.ToInvariant()}=\"Field break\",L{row.ToInvariant()},D{row.ToInvariant()})");
    }

    // Where an open item's values live: an engine entry on Result_Entries, or a row without an entry on Result_Rows.
    private sealed record Source(string TradeDate, string SettleDate, Func<ReportRole, Side, string> Amount)
    {
        public static Source Of(OpenItem item, DataTab entries, DataTab? rows) => (item.Entry, item.Row, rows) switch
        {
            ({ } entry, _, _) => new Source(
                entries.Cell("tradeDate", entry.Index),
                entries.Cell(item.ItemType switch
                {
                    "Unmatched theirs" => DataTabs.Theirs(ReportRole.SettleDate),
                    "Ambiguous" or "Search incomplete" => "settleDate",
                    _ => DataTabs.Ours(ReportRole.SettleDate),
                }, entry.Index),
                (role, side) => entries.Cell(side is Side.Left ? DataTabs.Ours(role) : DataTabs.Theirs(role), entry.Index)),
            (null, { } fact, { } tab) => new Source(
                tab.Cell("tradeDate", fact.Index),
                tab.Cell("settleDate", fact.Index),
                (role, _) => tab.Cell(DataTabs.RoleKey(role), fact.Index)),
            _ => throw new InvalidOperationException("An open item has neither an entry nor a row."),
        };
    }
}
