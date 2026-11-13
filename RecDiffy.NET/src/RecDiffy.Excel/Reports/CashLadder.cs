using System.Collections.Immutable;
using RecDiffy.Excel.Facts;
using RecDiffy.Excel.Model;
using RecDiffy.Results;

namespace RecDiffy.Excel.Reports;

// BO6. Layout from build_xlsx.py (BO6 cash ladder), decomposition from build_views.py: expected cash by settle date and currency, signed by side
// (receive +, pay −), ours against theirs, with the difference split into unmatched ours, unmatched theirs, inside matched entries and unresolved
// (ambiguous or search incomplete) rows. Every cell is a SUMIFS on Result_Rows; each side's cash sits on its own settle date and currency, so a
// settle-date break moves cash between dates and a currency split moves it between currencies. Never totalled across currencies.
internal static class CashLadder
{
    public static ReportDefinition Definition { get; } = new(
        ReportKind.CashLadder,
        [ReportRole.NetCash, ReportRole.Side, ReportRole.SettleDate, ReportRole.Currency],
        _ => null,
        Build);

    private static ReportBody Build(ReportContext context)
    {
        var info = ReportCatalogue.Of(ReportKind.CashLadder);
        var rows = DataTabs.Rows(context);
        var order = Ordering.Currencies(context.Options.CurrencyOrder);
        var lines = context.Facts.Rows
            .Where(row => row is { ItemLead: true, Row.Disposition: not (Disposition.Suppressed or Disposition.Quarantined) })
            .Select(row => (Settle: row.Values[ReportRole.SettleDate].Date, Currency: row.Values[ReportRole.Currency].Text))
            .Where(line => line.Settle is not null && line.Currency.Length > 0)
            .Distinct()
            .OrderBy(line => line.Settle)
            .ThenBy(line => order(line.Currency))
            .ThenBy(line => line.Currency, StringComparer.Ordinal)
            .ToImmutableArray();
        var currencies = context.SortCurrencies(lines.Select(line => line.Currency)).ToImmutableArray();
        var signed = rows.Range("signedCash");
        var source = rows.Range("source");
        var disposition = rows.Range("disposition");
        var settle = rows.Range("settleDate");
        var currency = rows.Range("currency");
        var lead = rows.Range("itemLead");

        string Cash(string side, string? only, int row) =>
            $"SUMIFS({signed},{source},\"{side}\",{settle},$A{row.ToInvariant()},{currency},$B{row.ToInvariant()},{lead},1"
            + (only is null ? $",{disposition},\"<>Suppressed\",{disposition},\"<>Quarantined\")" : $",{disposition},\"{only}\")");

        var note = ReportSheet.Body;
        var header = note + 1;
        var first = header + 1;
        var last = Math.Max(first, header + lines.Length);
        var totalsTitle = last + 2;
        var totalsHeader = totalsTitle + 1;
        var splitTitle = totalsHeader + 1 + currencies.Length + 2;
        var splits = context.Facts.Entries.Where(entry => entry.CurrencySplit).ToImmutableArray();
        ImmutableArray<string> columns =
        [
            .. context.Labels("Settle date", "Ccy", "Items (ours)", "Ours: expected cash", "Theirs: expected cash", "Difference",
                "Unmatched ours", "Unmatched theirs", "Inside matched entries", "Unresolved (ambiguous, search incomplete)", "Check"),
        ];

        var sheet = ReportSheet.Head(context, info)
            .Put(note, 1, "Sign convention: receive +, pay −. Difference = theirs − ours = unmatched ours + unmatched theirs + differences inside matched entries + unresolved rows.", Styles.Muted)
            .Header(header, columns, height: 42)
            .Fold(lines, (current, line, index) =>
            {
                var row = first + index;
                return current
                    .Put(row, 1, line.Settle!.Value, Styles.Number(Formats.Date))
                    .Put(row, 2, line.Currency)
                    .PutFormula(row, 3, $"COUNTIFS({source},\"left\",{settle},$A{row.ToInvariant()},{currency},$B{row.ToInvariant()},{lead},1,{disposition},\"<>Suppressed\",{disposition},\"<>Quarantined\")", Styles.Number(Formats.Count))
                    .PutFormula(row, 4, Cash("left", null, row), Styles.Number(Formats.Amount))
                    .PutFormula(row, 5, Cash("right", null, row), Styles.Number(Formats.Amount))
                    .PutFormula(row, 6, $"E{row.ToInvariant()}-D{row.ToInvariant()}", Styles.Number(Formats.Amount, bold: true))
                    .PutFormula(row, 7, $"-{Cash("left", "LeftOnly", row)}", Styles.Number(Formats.Amount))
                    .PutFormula(row, 8, Cash("right", "RightOnly", row), Styles.Number(Formats.Amount))
                    .PutFormula(row, 9, $"{Cash("right", "Matched", row)}-{Cash("left", "Matched", row)}", Styles.Number(Formats.Amount))
                    .PutFormula(row, 10, $"{Cash("right", "Ambiguous", row)}+{Cash("right", "SearchIncomplete", row)}-{Cash("left", "Ambiguous", row)}-{Cash("left", "SearchIncomplete", row)}", Styles.Number(Formats.Amount))
                    .PutFormula(row, 11, $"IF(ROUND(F{row.ToInvariant()}-(G{row.ToInvariant()}+H{row.ToInvariant()}+I{row.ToInvariant()}+J{row.ToInvariant()}),2)=0,\"✓\",\"✗\")", Styles.Check);
            })
            .Put(totalsTitle, 1, context.Label("Totals by currency (never added across currencies)"), Styles.Section)
            .Header(totalsHeader, [columns[1], "", .. columns[2..]], height: 42)
            .Fold(currencies, (current, code, index) =>
            {
                var row = totalsHeader + 1 + index;
                return current
                    .Put(row, 1, code, Styles.Bold)
                    .Put(row, 2, null)
                    .Fold(new[] { 3, 4, 5, 7, 8, 9, 10 }, (inner, column, _) => inner.PutFormula(
                        row,
                        column,
                        $"SUMIF($B${first.ToInvariant()}:$B${last.ToInvariant()},$A{row.ToInvariant()},{Address.Column(column)}${first.ToInvariant()}:{Address.Column(column)}${last.ToInvariant()})",
                        Styles.Number(column == 3 ? Formats.Count : Formats.Amount)))
                    .PutFormula(row, 6, $"E{row.ToInvariant()}-D{row.ToInvariant()}", Styles.Number(Formats.Amount, bold: true))
                    .PutFormula(row, 11, $"IF(ROUND(F{row.ToInvariant()}-(G{row.ToInvariant()}+H{row.ToInvariant()}+I{row.ToInvariant()}+J{row.ToInvariant()}),2)=0,\"✓\",\"✗\")", Styles.Check);
            })
            .Put(splitTitle, 1, context.Label("Matched pairs whose two sides carry different currencies (each side's cash sits in its own currency above)"), Styles.Section)
            .Header(splitTitle + 1, context.Labels("Entry", "Ours rows", "Theirs rows", "Ours currency", "Theirs currency", "Outcome"), height: 18)
            .Fold(splits, (current, entry, index) => current
                .Put(splitTitle + 2 + index, 1, entry.Short)
                .Put(splitTitle + 2 + index, 2, entry.Keys(Side.Left))
                .Put(splitTitle + 2 + index, 3, entry.Keys(Side.Right))
                .Put(splitTitle + 2 + index, 4, string.Join(", ", entry.OursCurrencies), Styles.RootBreak)
                .Put(splitTitle + 2 + index, 5, string.Join(", ", entry.TheirsCurrencies), Styles.RootBreak)
                .Put(splitTitle + 2 + index, 6, entry.Entry.Outcome.ToString()))
            .Put(splitTitle + 2 + Math.Max(1, splits.Length), 1, splits.IsEmpty ? "None in this run." : "", Styles.Note)
            .Rule(new ExpressionRule(new CellRange(first, 1, last, 1), $"AND(ISNUMBER(A{first.ToInvariant()}),A{first.ToInvariant()}<{DataTabs.AsOfName})", new DiffStyle(Fill: Palette.Sub)))
            .Rule(new ExpressionRule(new CellRange(first, 6, last, 6), $"AND(ISNUMBER(F{first.ToInvariant()}),ROUND(F{first.ToInvariant()},2)<>0)", new DiffStyle(FontColor: Palette.BreakText, Bold: true)))
            .CheckRules(new CellRange(first, 11, last, 11))
            .CheckRules(new CellRange(totalsHeader + 1, 11, totalsHeader + Math.Max(1, currencies.Length), 11))
            .Widths([12, 7, 9, 20, 20, 17, 17, 17, 18, 18, 7])
            .FreezeAt(first, 3)
            .Filter(header, 1, last, 11);
        return new ReportBody(sheet, [rows]);
    }
}
