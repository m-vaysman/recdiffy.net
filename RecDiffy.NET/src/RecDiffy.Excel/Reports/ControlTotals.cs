using System.Collections.Immutable;
using RecDiffy.Excel.Facts;
using RecDiffy.Excel.Model;
using RecDiffy.Results;

namespace RecDiffy.Excel.Reports;

// MO1. Ported from docs/report-specs/engine-mo1/build_engine_mo1.py: row counts by counterparty and asset (COUNTIFS on Result_Rows),
// the engine's BuildSideBySide quantity by currency with the difference decomposed and tied back to the files, and the
// matched pairs whose two sides carry different currencies.
internal static class ControlTotals
{
    private const int Span = 17;

    public static ReportDefinition Definition { get; } = new(
        ReportKind.ControlTotals,
        [ReportRole.Counterparty, ReportRole.Asset, ReportRole.Currency, ReportRole.Quantity],
        _ => null,
        Build);

    private sealed record Block(string Currency, int Band, int First, ImmutableArray<(int Row, int Line, SideBySideLine Value)> Lines, int Subtotal);

    public static IReadOnlyList<SideBySideBlock> EngineBlocks(ReportContext context)
    {
        var currencyField = context.Field(ReportRole.Currency)!;
        var measure = context.Field(ReportRole.Quantity)!;
        var currencies = context.SortCurrencies(context.Facts.Rows
            .Where(row => row.Row.Disposition is not Disposition.Suppressed)
            .Select(row => row.Values[ReportRole.Currency].Text)
            .Where(currency => currency.Length > 0));
        return [.. currencies.Select(currency => new SideBySideBlock(
            currency,
            measure,
            context.Result.BuildSideBySide(new SideBySideSpec(
                [context.Field(ReportRole.Counterparty)!],
                [context.Field(ReportRole.Asset)!],
                [new MeasureSpec(measure)],
                new FilterSpec(currencyField, currency))).Lines))];
    }

    private static ReportBody Build(ReportContext context)
    {
        var info = ReportCatalogue.Of(ReportKind.ControlTotals);
        var rows = DataTabs.Rows(context);
        var engine = EngineBlocks(context);
        var sideBySide = DataTabs.SideBySide(context, engine);
        var source = rows.Range("source");
        var disposition = rows.Range("disposition");
        var counterparty = rows.Range("counterparty");
        var asset = rows.Range("asset");
        var currency = rows.Range("currency");
        var quantity = rows.Range(DataTabs.RoleKey(ReportRole.Quantity));
        var split = rows.Range("pairCcySplit");

        // Part 1 · counts
        var others = context.Facts.Rows.Any(row => row.Row.Disposition is Disposition.Suppressed or Disposition.Quarantined or Disposition.SearchIncomplete);
        var pairs = context.Facts.Rows
            .Select(row => (Counterparty: row.Values[ReportRole.Counterparty].Text, Asset: row.Values[ReportRole.Asset].Text))
            .Distinct()
            .OrderBy(pair => pair.Counterparty, StringComparer.Ordinal)
            .ThenBy(pair => pair.Asset, StringComparer.Ordinal)
            .ToImmutableArray();
        var r0 = ReportSheet.Body + 1;
        var p1First = r0 + 2;
        var p1Last = p1First + pairs.Length - 1;
        var p1Total = p1Last + 1;
        var checkColumn = others ? 13 : 11;
        ImmutableArray<(string Side, string Disposition)> buckets =
        [
            ("left", "Matched"), ("right", "Matched"), ("left", "LeftOnly"), ("right", "RightOnly"), ("left", "Ambiguous"), ("right", "Ambiguous"),
        ];
        string Other(string side, int row) =>
            $"COUNTIFS({source},\"{side}\",{counterparty},$A{row},{asset},$B{row})-COUNTIFS({source},\"{side}\",{disposition},\"Matched\",{counterparty},$A{row},{asset},$B{row})" +
            $"-COUNTIFS({source},\"{side}\",{disposition},\"{(side == "left" ? "LeftOnly" : "RightOnly")}\",{counterparty},$A{row},{asset},$B{row})-COUNTIFS({source},\"{side}\",{disposition},\"Ambiguous\",{counterparty},$A{row},{asset},$B{row})";
        string Condition(int row) => others
            ? $"AND(C{row}=E{row}+G{row}+I{row}+K{row},D{row}=F{row}+H{row}+J{row}+L{row})"
            : $"AND(C{row}=E{row}+G{row}+I{row},D{row}=F{row}+H{row}+J{row})";
        string RowCheck(int row) => $"IF({Condition(row)},\"✓\",\"✗\")";

        var part1 = ReportSheet.Head(context, info)
            .Put(r0, 1, context.Label("Part 1 · Row counts by counterparty and asset (COUNTIFS on Result_Rows)"), Styles.Section)
            .Header(r0 + 1, context.Labels(
            [
                "Counterparty", "Asset", "Rows ours", "Rows theirs", "Matched ours", "Matched theirs", "Unmatched ours", "Unmatched theirs", "Ambiguous ours", "Ambiguous theirs",
                .. others ? ["Other ours (suppressed, quarantined, search incomplete)", "Other theirs"] : Array.Empty<string>(),
                "Check",
            ]), height: 42)
            .Fold(pairs, (sheet, pair, index) =>
            {
                var row = p1First + index;
                var criteria = $"{counterparty},$A{row},{asset},$B{row}";
                return sheet
                    .Put(row, 1, pair.Counterparty, Styles.Bold)
                    .Put(row, 2, pair.Asset)
                    .PutFormula(row, 3, $"COUNTIFS({source},\"left\",{criteria})", Styles.Number(Formats.Count))
                    .PutFormula(row, 4, $"COUNTIFS({source},\"right\",{criteria})", Styles.Number(Formats.Count))
                    .Fold(buckets, (inner, bucket, position) => inner.PutFormula(
                        row,
                        5 + position,
                        $"COUNTIFS({source},\"{bucket.Side}\",{disposition},\"{bucket.Disposition}\",{criteria})",
                        Styles.Number(Formats.Count)))
                    .Fold(others ? ["left", "right"] : Array.Empty<string>(), (inner, side, position) => inner.PutFormula(row, 11 + position, Other(side, row), Styles.Number(Formats.Count)))
                    .PutFormula(row, checkColumn, RowCheck(row), Styles.Check);
            })
            .Put(p1Total, 1, context.Label("Total"), Styles.Total)
            .Put(p1Total, 2, null, Styles.Total)
            .Fold(Enumerable.Range(3, checkColumn - 3), (sheet, column, _) =>
                sheet.PutFormula(p1Total, column, $"SUM({Address.Column(column)}{p1First}:{Address.Column(column)}{Math.Max(p1First, p1Last)})", Styles.Total with { Format = Formats.Count }))
            .PutFormula(
                p1Total,
                checkColumn,
                $"IF(AND(C{p1Total}=COUNTIF({source},\"left\"),D{p1Total}=COUNTIF({source},\"right\"),{Condition(p1Total)}),\"✓\",\"✗\")",
                Styles.Total with { Align = HorizontalAlign.Center });

        // Part 2 · quantity side-by-side
        var p2Title = p1Total + 2;
        var p2Head = p2Title + 1;
        var blocks = engine.Aggregate(
            (Blocks: ImmutableList<Block>.Empty, Next: p2Head + 1, Line: 0),
            (state, block) =>
            {
                var lines = block.Lines.Select((line, index) => (Row: state.Next + 1 + index, Line: state.Line + index, Value: line)).ToImmutableArray();
                var built = new Block(block.Currency, state.Next, state.Next + 1, lines, state.Next + 1 + lines.Length);
                return (state.Blocks.Add(built), built.Subtotal + 1, state.Line + lines.Length);
            }).Blocks;
        var p2Last = blocks.IsEmpty ? p2Head : blocks[^1].Subtotal;
        var extra = others ? 2 : 0;
        var fileOurs = 15 + extra;
        var fileTheirs = 16 + extra;
        var tie = 17 + extra;
        var span = Span + extra;
        ImmutableArray<int> summed = [4, 5, 6, 7, 8, 9, 11, 12, 13, 14, .. others ? [15, 16] : Array.Empty<int>(), fileOurs, fileTheirs];
        string Col(int column) => Address.Column(column);
        string Tie(int row) => others
            ? $"IF(AND({Col(fileOurs)}{row}=D{row}+M{row}+O{row},{Col(fileTheirs)}{row}=E{row}+N{row}+P{row}),\"✓\",\"✗\")"
            : $"IF(AND({Col(fileOurs)}{row}=D{row}+M{row},{Col(fileTheirs)}{row}=E{row}+N{row}),\"✓\",\"✗\")";
        string OtherSum(string side, string criteria) =>
            string.Join("+", new[] { "Suppressed", "Quarantined", "SearchIncomplete" }.Select(state => $"SUMIFS({quantity},{source},\"{side}\",{disposition},\"{state}\",{criteria})"));

        var part2 = part1
            .Put(p2Title, 1, context.Label("Part 2 · Quantity side-by-side by currency (engine BuildSideBySide), with the difference decomposed and tied back to the files"), Styles.Section)
            .Header(p2Head, context.Labels(
                [
                    "Currency", "Counterparty", "Asset", "Ours", "Theirs", "Δ (theirs − ours)", "Unmatched ours", "Unmatched theirs", "Inside matched", "Check",
                    "Inside matched: currency split", "Inside matched: field differences", "Ambiguous ours (not in totals)", "Ambiguous theirs (not in totals)",
                    .. others ? ["Suppressed, quarantined, search incomplete ours (not in totals)", "Suppressed, quarantined, search incomplete theirs (not in totals)"] : Array.Empty<string>(),
                    "File total ours", "File total theirs", "Tie-out",
                ]), height: others ? 66 : 42)
            .Fold(blocks, (sheet, block, _) => sheet
                .Put(block.Band, 1, $"{block.Currency}  ·  totals in {block.Currency} only", Styles.Band)
                .Merge(block.Band, 1, block.Band, span)
                .Fold(block.Lines, (inner, line, _) =>
                {
                    var row = line.Row;
                    var criteria = $"{counterparty},$B{row},{asset},$C{row},{currency},$A{row}";
                    string Engine(string column) => $"={DataTabs.SideBySideName}!{column}{sideBySide.RowOf(line.Line)}";
                    return inner
                        .Put(row, 1, block.Currency)
                        .Put(row, 2, line.Value.Row, Styles.Bold)
                        .Put(row, 3, line.Value.Column)
                        .PutFormula(row, 4, Engine("D"), Styles.Link(Formats.Quantity))
                        .PutFormula(row, 5, Engine("E"), Styles.Link(Formats.Quantity))
                        .PutFormula(row, 6, $"E{row}-D{row}", Styles.Number(Formats.Quantity, bold: true))
                        .PutFormula(row, 7, Engine("G"), Styles.Link(Formats.Quantity))
                        .PutFormula(row, 8, Engine("H"), Styles.Link(Formats.Quantity))
                        .PutFormula(row, 9, Engine("I"), Styles.Link(Formats.Quantity))
                        .PutFormula(row, 10, $"IF(AND(F{row}=G{row}+H{row}+I{row},F{row}={DataTabs.SideBySideName}!F{sideBySide.RowOf(line.Line)}),\"✓\",\"✗\")", Styles.Check)
                        .PutFormula(row, 11, $"SUMIFS({quantity},{source},\"right\",{disposition},\"Matched\",{split},\"Y\",{criteria})-SUMIFS({quantity},{source},\"left\",{disposition},\"Matched\",{split},\"Y\",{criteria})", Styles.Number(Formats.Quantity))
                        .PutFormula(row, 12, $"I{row}-K{row}", Styles.Number(Formats.Quantity))
                        .PutFormula(row, 13, $"SUMIFS({quantity},{source},\"left\",{disposition},\"Ambiguous\",{criteria})", Styles.Number(Formats.Quantity))
                        .PutFormula(row, 14, $"SUMIFS({quantity},{source},\"right\",{disposition},\"Ambiguous\",{criteria})", Styles.Number(Formats.Quantity))
                        .Fold(others ? ["left", "right"] : Array.Empty<string>(), (cells, side, position) => cells.PutFormula(row, 15 + position, OtherSum(side, criteria), Styles.Number(Formats.Quantity)))
                        .PutFormula(row, fileOurs, $"SUMIFS({quantity},{source},\"left\",{criteria})", Styles.Number(Formats.Quantity))
                        .PutFormula(row, fileTheirs, $"SUMIFS({quantity},{source},\"right\",{criteria})", Styles.Number(Formats.Quantity))
                        .PutFormula(row, tie, Tie(row), Styles.Check);
                })
                .Put(block.Subtotal, 1, block.Currency, Styles.Total)
                .Put(block.Subtotal, 2, $"{context.Label("Subtotal")} {block.Currency}", Styles.Total)
                .Put(block.Subtotal, 3, null, Styles.Total)
                .Fold(summed, (inner, column, _) => inner.PutFormula(
                    block.Subtotal,
                    column,
                    $"SUM({Address.Column(column)}{block.First}:{Address.Column(column)}{Math.Max(block.First, block.Subtotal - 1)})",
                    Styles.Total with { Format = Formats.Quantity }))
                .PutFormula(block.Subtotal, 10, $"IF(F{block.Subtotal}=G{block.Subtotal}+H{block.Subtotal}+I{block.Subtotal},\"✓\",\"✗\")", Styles.Total with { Align = HorizontalAlign.Center })
                .PutFormula(block.Subtotal, tie, Tie(block.Subtotal), Styles.Total with { Align = HorizontalAlign.Center }))
            .Put(p2Last + 1, 1, "Quantities are summed within a currency only. Units differ by asset (face for bonds, shares for equity, notional for FX and swaps), so compare a line across its own row, not down the column.", Styles.Note);

        // Part 3 · currency-split pairs
        var splits = context.Facts.Entries.Where(entry => entry.CurrencySplit).ToImmutableArray();
        var p3Title = p2Last + 3;
        var p3Head = p3Title + 1;
        var notesRow = p3Head + 1 + splits.Length + 1;
        ImmutableArray<string> notes =
        [
            "Reading the engine view:",
            "• Each row is placed in the currency block of its own currency. A matched pair booked in one currency but reported in another puts our quantity in one block and theirs in the other, so both blocks show an \"inside matched\" amount that is really one currency break. Column K separates that from genuine field differences.",
            others
                ? "• The engine leaves Ambiguous, SearchIncomplete, Quarantined and Suppressed rows out of BuildSideBySide, so its Ours and Theirs do not equal the file totals. Columns M to S add them back and tie each line to the files."
                : "• The engine leaves Ambiguous rows out of BuildSideBySide, so its Ours and Theirs do not equal the file totals. Columns M to Q add them back and tie each line to the files.",
        ];
        var part3 = part2
            .Put(p3Title, 1, context.Label("Part 3 · Matched pairs whose two sides carry different currencies (their halves sit in different currency blocks above)"), Styles.Section)
            .Header(p3Head, context.Labels("Entry", "Ours rows", "Theirs rows", "Counterparty", "Asset", "Ours currency", "Theirs currency", "Quantity ours", "Quantity theirs", "Matched by", "Outcome", "Break fields"), height: 30)
            .Fold(splits, (sheet, entry, index) =>
            {
                var row = p3Head + 1 + index;
                var ours = context.Facts.RowByRef[entry.Entry.Left[0]];
                var theirs = context.Facts.RowByRef[entry.Entry.Right[0]];
                var red = new CellStyle(Color: Palette.BreakText);
                return sheet
                    .Put(row, 1, entry.Short)
                    .Put(row, 2, entry.Keys(Side.Left))
                    .Put(row, 3, entry.Keys(Side.Right))
                    .Put(row, 4, ours.Values[ReportRole.Counterparty].Text)
                    .Put(row, 5, ours.Values[ReportRole.Asset].Text)
                    .Put(row, 6, string.Join(", ", entry.OursCurrencies), red)
                    .Put(row, 7, string.Join(", ", entry.TheirsCurrencies), red)
                    .PutFormula(row, 8, ReportSheet.Link(rows.Cell(DataTabs.RoleKey(ReportRole.Quantity), ours.Index)), Styles.Link(Formats.Quantity))
                    .PutFormula(row, 9, ReportSheet.Link(rows.Cell(DataTabs.RoleKey(ReportRole.Quantity), theirs.Index)), Styles.Link(Formats.Quantity))
                    .Put(row, 10, entry.Entry.Explanation.CommittedBy)
                    .Put(row, 11, entry.Entry.Outcome.ToString())
                    .Put(row, 12, string.Join(", ", entry.Entry.Diffs.Select(diff => diff.Field.Name)));
            })
            .Fold(notes, (sheet, note, index) => sheet
                .Put(notesRow + index, 1, note, CellStyle.Bare with { Wrap = true, Bold = index == 0, VAlign = VerticalAlign.Top })
                .Merge(notesRow + index, 1, notesRow + index, span)
                .Height(notesRow + index, index == 0 ? 15 : 28));

        var sheet = part3
            .Widths([14, 16, 12, 15, 15, 16, 15, 15, 15, 11, 15, 15, 15, 15, .. others ? [17.0, 17.0] : Array.Empty<double>(), 15, 15, 8])
            .FreezeAt(p1First, 1)
            .Rule(new ExpressionRule(new CellRange(p2Head + 1, 6, p2Last, 6), $"AND(ISNUMBER(F{p2Head + 1}),F{p2Head + 1}<>0)", Styles.Red))
            .CheckRules(new CellRange(p1First, checkColumn, p1Total, checkColumn))
            .CheckRules(new CellRange(p2Head + 1, 10, p2Last, 10))
            .CheckRules(new CellRange(p2Head + 1, tie, p2Last, tie))
            .Rule(new ExpressionRule(new CellRange(p2Head + 1, 11, p2Last, 11), $"AND(ISNUMBER(K{p2Head + 1}),K{p2Head + 1}<>0)", Styles.Amber));
        return new ReportBody(sheet, [rows, sideBySide]);
    }
}
