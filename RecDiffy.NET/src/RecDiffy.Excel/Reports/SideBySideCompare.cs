using System.Collections.Immutable;
using RecDiffy.Excel.Facts;
using RecDiffy.Excel.Model;
using RecDiffy.Results;

namespace RecDiffy.Excel.Reports;

// BO2. Layout from build_xlsx.py (BO2 side by side): every Matched entry whose outcome is not Exact, ours and theirs for each compared field,
// the Theirs cell coloured from the engine's diff (root break red, within tolerance amber, consequence of another field grey italic).
// Matched pairs whose two sides carry different currencies are listed separately from genuine field differences.
internal static class SideBySideCompare
{
    private static readonly ImmutableArray<ReportRole> Compared = [ReportRole.Quantity, ReportRole.Price, ReportRole.SettleDate, ReportRole.Accrued, ReportRole.Fees, ReportRole.NetCash];

    public static ReportDefinition Definition { get; } = new(
        ReportKind.SideBySideCompare,
        [ReportRole.Counterparty, ReportRole.Instrument, ReportRole.Side, ReportRole.Currency],
        context => Compared.Any(context.Fields.IsMapped) ? null : $"Map at least one of {string.Join(", ", Compared)} in ExcelReportOptions.FieldMap.",
        Build);

    private static ReportBody Build(ReportContext context)
    {
        var info = ReportCatalogue.Of(ReportKind.SideBySideCompare);
        var entries = DataTabs.Entries(context);
        var fields = Compared.Where(context.Fields.IsMapped).ToImmutableArray();
        var shown = fields.Select(role => context.Field(role)!).Append(context.Field(ReportRole.Currency)!).ToImmutableHashSet(StringComparer.Ordinal);
        var currencyField = context.Field(ReportRole.Currency)!;
        var rows = context.Facts.Entries
            .Where(entry => entry.IsMatched && entry.Entry.Outcome is not FieldOutcome.Exact)
            .Where(entry => entry.Entry.Diffs.Any(diff => diff.Field.Name != currencyField))
            .OrderBy(entry => entry.Entry.Outcome is FieldOutcome.Break ? 0 : 1)
            .ThenBy(entry => entry.Id, StringComparer.Ordinal)
            .ToImmutableArray();
        var splits = context.Facts.Entries.Where(entry => entry.CurrencySplit || entry.Entry.Diffs.Any(diff => diff.Field.Name == currencyField)).ToImmutableArray();
        ImmutableArray<string> fixedColumns = [.. context.Labels("Entry", "Counterparty", "Instrument", "Side", "Ccy", "Outcome", "Pass")];
        var netCash = fields.Contains(ReportRole.NetCash);
        var deltaColumn = fixedColumns.Length + 1 + 2 * fields.Length;
        var otherColumn = deltaColumn + (netCash ? 1 : 0);
        var legend = ReportSheet.Body;
        var h1 = legend + 2;
        var first = h1 + 2;
        var last = Math.Max(first, h1 + 1 + rows.Length);

        var sheet = ReportSheet.Head(context, info)
            .Put(legend, 1, null, new CellStyle(Fill: Palette.Break)).Put(legend, 2, context.Label("Root break"), CellStyle.Bare)
            .Put(legend, 4, null, new CellStyle(Fill: Palette.Tolerance)).Put(legend, 5, context.Label("Within tolerance"), CellStyle.Bare)
            .Put(legend, 7, null, new CellStyle(Fill: Palette.Attributed)).Put(legend, 8, context.Label("Consequence of another field"), CellStyle.Bare)
            .Fold(fixedColumns, (current, title, index) => current
                .Put(h1, index + 1, title, Styles.Header)
                .Put(h1 + 1, index + 1, null, Styles.Header)
                .Merge(h1, index + 1, h1 + 1, index + 1))
            .Fold(fields, (current, role, index) =>
            {
                var column = fixedColumns.Length + 1 + 2 * index;
                return current
                    .Put(h1, column, context.Label(context.Field(role)!), Styles.Header with { Align = HorizontalAlign.Center })
                    .Put(h1, column + 1, null, Styles.Header)
                    .Merge(h1, column, h1, column + 1)
                    .Put(h1 + 1, column, context.Label("Ours"), Styles.SubHeader)
                    .Put(h1 + 1, column + 1, context.Label("Theirs"), Styles.SubHeader);
            })
            .Fold(netCash ? [context.Label("Δ NetCash (theirs − ours)")] : Array.Empty<string>(), (current, title, _) => current
                .Put(h1, deltaColumn, title, Styles.Header).Put(h1 + 1, deltaColumn, null, Styles.Header).Merge(h1, deltaColumn, h1 + 1, deltaColumn))
            .Put(h1, otherColumn, context.Label("Other differences"), Styles.Header).Put(h1 + 1, otherColumn, null, Styles.Header).Merge(h1, otherColumn, h1 + 1, otherColumn)
            .Height(h1, 30)
            .Fold(rows, (current, entry, index) => Line(current, context, entries, fields, shown, entry, first + index, deltaColumn, otherColumn, netCash))
            .Put(last + 1, 1, "Values link to Result_Entries (the engine's projections). Colours come from the engine's field diffs. Currency differences are listed below, apart from field differences.", Styles.Note);

        var splitTitle = last + 3;
        var splitHeader = splitTitle + 1;
        var withSplits = sheet
            .Put(splitTitle, 1, context.Label("Matched pairs whose two sides carry different currencies"), Styles.Section)
            .Header(splitHeader, context.Labels("Entry", "Counterparty", "Instrument", "Ours currency", "Theirs currency", "Outcome", "Pass", "Other differences"), height: 18)
            .Fold(splits, (current, entry, index) => current
                .Put(splitHeader + 1 + index, 1, entry.Short)
                .Put(splitHeader + 1 + index, 2, entry.Text(ReportRole.Counterparty))
                .Put(splitHeader + 1 + index, 3, entry.Text(ReportRole.Instrument))
                .Put(splitHeader + 1 + index, 4, string.Join(", ", entry.OursCurrencies), Styles.RootBreak)
                .Put(splitHeader + 1 + index, 5, string.Join(", ", entry.TheirsCurrencies), Styles.RootBreak)
                .Put(splitHeader + 1 + index, 6, entry.Entry.Outcome.ToString())
                .Put(splitHeader + 1 + index, 7, entry.Entry.Explanation.CommittedBy)
                .Put(splitHeader + 1 + index, 8, string.Join(", ", entry.Entry.Diffs.Select(diff => diff.Field.Name).Where(field => field != currencyField))))
            .Put(splitHeader + 1 + Math.Max(1, splits.Length), 1, splits.IsEmpty ? "None in this run." : "", Styles.Note)
            .Widths([13, 13, 22, 10, 7, 15, 9, .. fields.SelectMany(_ => new double[] { 14, 14 }), .. netCash ? new double[] { 15 } : [], 22])
            .FreezeAt(first, 3)
            .Filter(h1 + 1, 1, last, otherColumn);
        return new ReportBody(withSplits, [entries]);
    }

    private static Sheet Line(
        Sheet sheet,
        ReportContext context,
        DataTab entries,
        ImmutableArray<ReportRole> fields,
        ImmutableHashSet<string> shown,
        EntryFact entry,
        int row,
        int deltaColumn,
        int otherColumn,
        bool netCash)
    {
        var diffs = context.Facts.DiffsByEntry[entry.Id].ToImmutableDictionary(diff => diff.Field, StringComparer.Ordinal);
        var outcome = entry.Entry.Outcome is FieldOutcome.Break ? Styles.RootBreak : Styles.WithinTolerance;
        var netColumn = 8 + 2 * fields.IndexOf(ReportRole.NetCash);
        return sheet
            .Put(row, 1, entry.Short)
            .Put(row, 2, entry.Text(ReportRole.Counterparty))
            .Put(row, 3, entry.Text(ReportRole.Instrument))
            .Put(row, 4, entry.Text(ReportRole.Side))
            .Put(row, 5, entry.Text(ReportRole.Currency))
            .Put(row, 6, entry.Entry.Outcome.ToString(), outcome)
            .Put(row, 7, entry.Entry.Explanation.CommittedBy)
            .Fold(fields, (current, role, index) =>
            {
                var column = 8 + 2 * index;
                var format = DataTabs.FormatFor(role);
                var theirs = diffs.TryGetValue(context.Field(role)!, out var diff) ? DiffStyleFor(diff.State) with { Format = format } : Styles.Link(format);
                return current
                    .PutFormula(row, column, ReportSheet.Link(entries.Cell(DataTabs.Ours(role), entry.Index)), Styles.Link(format))
                    .PutFormula(row, column + 1, ReportSheet.Link(entries.Cell(DataTabs.Theirs(role), entry.Index)), theirs);
            })
            .Fold(netCash ? [netColumn] : Array.Empty<int>(), (current, column, _) =>
                current.PutFormula(row, deltaColumn, $"{Address.Column(column + 1)}{row.ToInvariant()}-{Address.Column(column)}{row.ToInvariant()}", Styles.Number(Formats.Amount, bold: true)))
            .Put(row, otherColumn, string.Join(", ", entry.Entry.Diffs.Select(diff => diff.Field.Name).Where(field => shown.Contains(field) is false)));
    }

    public static CellStyle DiffStyleFor(DiffState state) => state switch
    {
        DiffState.Break => Styles.RootBreak,
        DiffState.Tolerance => Styles.WithinTolerance,
        _ => Styles.Consequence,
    };
}
