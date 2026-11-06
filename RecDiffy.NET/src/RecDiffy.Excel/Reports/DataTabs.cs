using System.Collections.Immutable;
using RecDiffy.Excel.Facts;
using RecDiffy.Excel.Model;
using RecDiffy.Results;

namespace RecDiffy.Excel.Reports;

// The data tabs a report links to. Values are the engine's own output, read through the public API.
internal static class DataTabs
{
    public const string EntriesName = "Result_Entries";
    public const string RowsName = "Result_Rows";
    public const string DiffsName = "Result_Diffs";
    public const string OpenItemsName = "Open_Items";
    public const string SideBySideName = "Engine_SideBySide";
    public const string RunInfoName = "Run_Info";
    public const string AsOfName = "AsOf";
    public const string AsOfReference = "Run_Info!$B$3";

    private static readonly ImmutableArray<ReportRole> Measures = [ReportRole.Quantity, ReportRole.Price, ReportRole.Accrued, ReportRole.Fees, ReportRole.NetCash];

    public static string RoleKey(ReportRole role) => char.ToLowerInvariant(role.ToString()[0]) + role.ToString()[1..];

    public static string Ours(ReportRole role) => "ours" + role;

    public static string Theirs(ReportRole role) => "theirs" + role;

    public static string FormatFor(ReportRole? role) => role switch
    {
        ReportRole.Quantity => Formats.Quantity,
        ReportRole.Price => Formats.Price,
        ReportRole.NetCash or ReportRole.Accrued or ReportRole.Fees => Formats.Amount,
        ReportRole.TradeDate or ReportRole.SettleDate => Formats.Date,
        _ => Formats.Price,
    };

    public static string FormatFor(FieldValue value, ReportRole? role) => value switch
    {
        { Date: not null } => Formats.Date,
        { Number: not null } => FormatFor(role),
        _ => Formats.Price,
    };

    public static DataTab Entries(ReportContext context) => DataTab.Build(
        EntriesName,
        [],
        [
            DataColumn<EntryFact>.Of("entryId", entry => entry.Id, width: 34),
            DataColumn<EntryFact>.Of("path", entry => entry.Entry.PathId, width: 10),
            DataColumn<EntryFact>.Of("alignment", entry => entry.Entry.Alignment.ToString(), width: 13),
            DataColumn<EntryFact>.Of("composition", entry => entry.Entry.Composition.ToString(), width: 22),
            DataColumn<EntryFact>.Of("outcome", entry => entry.Entry.Outcome.ToString(), width: 14),
            DataColumn<EntryFact>.Of("committedBy", entry => entry.Entry.Explanation.CommittedBy, width: 11),
            DataColumn<EntryFact>.Of("counterparty", entry => entry.Text(ReportRole.Counterparty), width: 14),
            DataColumn<EntryFact>.Of("instrument", entry => entry.Text(ReportRole.Instrument), width: 16),
            DataColumn<EntryFact>.Of("asset", entry => entry.Text(ReportRole.Asset), width: 10),
            DataColumn<EntryFact>.Of("side", entry => entry.Text(ReportRole.Side), width: 10),
            DataColumn<EntryFact>.Of("theirsSide", entry => entry.Theirs[ReportRole.Side].Text, width: 10),
            DataColumn<EntryFact>.Of("tradeDate", entry => entry.Value(ReportRole.TradeDate).Cell, Formats.Date, 11),
            DataColumn<EntryFact>.Of(Ours(ReportRole.TradeDate), entry => entry.Ours[ReportRole.TradeDate].Cell, Formats.Date, 11),
            DataColumn<EntryFact>.Of(Theirs(ReportRole.TradeDate), entry => entry.Theirs[ReportRole.TradeDate].Cell, Formats.Date, 11),
            DataColumn<EntryFact>.Of("settleDate", entry => entry.Value(ReportRole.SettleDate).Cell, Formats.Date, 11),
            DataColumn<EntryFact>.Of(Ours(ReportRole.SettleDate), entry => entry.Ours[ReportRole.SettleDate].Cell, Formats.Date, 11),
            DataColumn<EntryFact>.Of(Theirs(ReportRole.SettleDate), entry => entry.Theirs[ReportRole.SettleDate].Cell, Formats.Date, 11),
            DataColumn<EntryFact>.Of("currency", entry => entry.Text(ReportRole.Currency), width: 9),
            DataColumn<EntryFact>.Of("theirsCurrency", entry => entry.Theirs[ReportRole.Currency].Text, width: 9),
            DataColumn<EntryFact>.Of("ccySplit", entry => entry.CurrencySplit ? "Y" : "N", width: 8),
            DataColumn<EntryFact>.Of("leftRows", entry => entry.Entry.Left.Count, Formats.Count, 9),
            DataColumn<EntryFact>.Of("rightRows", entry => entry.Entry.Right.Count, Formats.Count, 9),
            DataColumn<EntryFact>.Of("suppressedRows", entry => entry.SuppressedRows, Formats.Count, 9),
            DataColumn<EntryFact>.Of("leftKeys", entry => entry.Keys(Side.Left), width: 24),
            DataColumn<EntryFact>.Of("rightKeys", entry => entry.Keys(Side.Right), width: 24),
            .. Measures.SelectMany(role => new[]
            {
                DataColumn<EntryFact>.Of(Ours(role), entry => entry.Ours[role].Cell, FormatFor(role), 14),
                DataColumn<EntryFact>.Of(Theirs(role), entry => entry.Theirs[role].Cell, FormatFor(role), 14),
            }),
            DataColumn<EntryFact>.Of("rootFields", entry => string.Join(", ", entry.RootFields), width: 14),
            DataColumn<EntryFact>.Of("diffFields", entry => string.Join(", ", entry.Entry.Diffs.Select(diff => diff.Field.Name)), width: 22),
            DataColumn<EntryFact>.Of("diagnostics", entry => string.Join(", ", entry.Diagnostics), width: 20),
        ],
        context.Facts.Entries);

    public static DataTab Rows(ReportContext context)
    {
        var paying = context.Options.PayingSides.ToImmutableHashSet(StringComparer.Ordinal);
        ImmutableArray<DataColumn<RowFact>> columns =
        [
            DataColumn<RowFact>.Of("source", row => row.Source, width: 8),
            DataColumn<RowFact>.Of("rowKey", row => row.Row.Row.RowKey, width: 16),
            DataColumn<RowFact>.Of("disposition", row => row.Row.Disposition.ToString(), width: 14),
            DataColumn<RowFact>.Of("suppression", row => row.Row.Suppression?.ToString(), width: 12),
            DataColumn<RowFact>.Of("validity", row => row.Row.Validity.ToString(), width: 12),
            DataColumn<RowFact>.Of("entryId", row => row.Entry?.Id, width: 34),
            DataColumn<RowFact>.Of("committedBy", row => row.Row.Disposition is Disposition.Matched ? row.Entry?.Entry.Explanation.CommittedBy : null, width: 11),
            DataColumn<RowFact>.Of("itemLead", row => row.ItemLead ? 1 : 0, Formats.Integer, 8),
            DataColumn<RowFact>.Of("counterparty", row => row.Values[ReportRole.Counterparty].Text, width: 14),
            DataColumn<RowFact>.Of("instrument", row => row.Values[ReportRole.Instrument].Text, width: 16),
            DataColumn<RowFact>.Of("asset", row => row.Values[ReportRole.Asset].Text, width: 10),
            DataColumn<RowFact>.Of("side", row => row.Values[ReportRole.Side].Text, width: 10),
            DataColumn<RowFact>.Of("currency", row => row.Values[ReportRole.Currency].Text, width: 9),
            DataColumn<RowFact>.Of("tradeDate", row => row.Values[ReportRole.TradeDate].Cell, Formats.Date, 11),
            DataColumn<RowFact>.Of("settleDate", row => row.Values[ReportRole.SettleDate].Cell, Formats.Date, 11),
            .. Measures.Select(role => DataColumn<RowFact>.Of(RoleKey(role), row => row.Values[role].Cell, FormatFor(role), 14)),
            DataColumn<RowFact>.Of("book", row => row.Values[ReportRole.Book].Text, width: 10),
            DataColumn<RowFact>.Of("action", row => row.Values[ReportRole.LifecycleAction].Text, width: 9),
            DataColumn<RowFact>.Of("target", row => row.Values[ReportRole.LifecycleTarget].Text, width: 14),
            DataColumn<RowFact>.Of("pairCcySplit", row => row.PairCurrencySplit ? "Y" : "N", width: 9),
            DataColumn<RowFact>.Of("cashSign", row => CashSign(row, paying), Formats.Integer, 8),
            DataColumn<RowFact>.Of("diagnostics", row => string.Join("; ", row.Row.Diagnostics.Select(diagnostic => diagnostic.Code)), width: 20),
        ];
        var letters = columns.Select((column, index) => (column.Key, Letter: Address.Column(index + 1))).ToImmutableDictionary(pair => pair.Key, pair => pair.Letter);
        var netCash = letters[RoleKey(ReportRole.NetCash)];
        var sign = letters["cashSign"];
        var signed = new DataColumn<RowFact>(
            "signedCash",
            (_, row) => Content.Formula($"IF(AND(ISNUMBER({netCash}{row}),ISNUMBER({sign}{row})),{netCash}{row}*{sign}{row},0)"),
            Formats.Amount,
            14);
        return DataTab.Build(
            RowsName,
            [],
            [.. columns, signed],
            context.Facts.Rows);
    }

    private static object? CashSign(RowFact row, ImmutableHashSet<string> paying) => row.Values[ReportRole.Side].Text switch
    {
        "" => null,
        var side when paying.Contains(side) => -1,
        _ => 1,
    };

    public static DataTab Diffs(ReportContext context) => DataTab.Build(
        DiffsName,
        [],
        [
            DataColumn<DiffFact>.Of("entryId", diff => diff.Entry.Id, width: 34),
            DataColumn<DiffFact>.Of("path", diff => diff.Entry.Entry.PathId, width: 10),
            DataColumn<DiffFact>.Of("field", diff => diff.Field, width: 13),
            DataColumn<DiffFact>.Of("outcome", diff => diff.Diff.Outcome.ToString(), width: 15),
            DataColumn<DiffFact>.Of("state", diff => diff.State.ToString(), width: 11),
            new DataColumn<DiffFact>("ours", (diff, _) => diff.Ours.Cell, Width: 14, FormatFor: diff => FormatFor(diff.Ours, diff.Role)),
            new DataColumn<DiffFact>("theirs", (diff, _) => diff.Theirs.Cell, Width: 14, FormatFor: diff => FormatFor(diff.Theirs, diff.Role)),
            new DataColumn<DiffFact>("delta", (diff, _) => diff.Delta, Width: 14, FormatFor: diff => diff.Diff.Delta.Kind is DeltaKind.Days ? Formats.Integer : FormatFor(diff.Role) is Formats.Date ? Formats.Integer : FormatFor(diff.Role)),
            DataColumn<DiffFact>.Of("deltaKind", diff => diff.Diff.Delta.Kind.ToString(), width: 10),
            DataColumn<DiffFact>.Of("tolerance", diff => diff.Diff.Tolerance.Value, width: 12),
            DataColumn<DiffFact>.Of("toleranceAmount", diff => diff.ToleranceAmount, Formats.Price, 14),
            DataColumn<DiffFact>.Of("attributedTo", diff => diff.Diff.AttributedTo.IsDefaultOrEmpty ? null : string.Join(", ", diff.Diff.AttributedTo), width: 16),
        ],
        context.Facts.Diffs);

    public static DataTab OpenItems(ReportContext context) => DataTab.Build(
        OpenItemsName,
        [],
        [
            DataColumn<OpenItem>.Of("entryId", item => item.Key, width: 34),
            DataColumn<OpenItem>.Of("itemType", item => item.ItemType, width: 16),
            DataColumn<OpenItem>.Of("category", item => item.Category, width: 16),
            DataColumn<OpenItem>.Of("counterparty", item => item.Text(ReportRole.Counterparty), width: 14),
            DataColumn<OpenItem>.Of("currency", item => item.Text(ReportRole.Currency), width: 9),
            DataColumn<OpenItem>.Of("tradeDate", item => item.Value(ReportRole.TradeDate).Cell, Formats.Date, 11),
            DataColumn<OpenItem>.Of("settleDate", item => item.SettleDate.Cell, Formats.Date, 11),
            new DataColumn<OpenItem>("status", (_, row) => Content.Formula(StatusFormula($"G{row}")), Width: 16),
            new DataColumn<OpenItem>("ageBD", (_, row) => Content.Formula(AgeFormula($"F{row}")), Formats.Integer, 8),
        ],
        context.Facts.OpenItems);

    public static string StatusFormula(string settle) =>
        $"IF({settle}=\"\",\"No settle date\",IF({settle}<{AsOfName},\"Past settle date\",IF({settle}={AsOfName},\"Settles today\",\"Upcoming\")))";

    public static string AgeFormula(string trade) => $"IF({trade}=\"\",\"\",NETWORKDAYS({trade},{AsOfName})-1)";

    public static DataTab SideBySide(ReportContext context, IReadOnlyList<SideBySideBlock> blocks)
    {
        var lines = blocks.SelectMany(block => block.Lines.Select(line => (block.Currency, Line: line, Cell: line.Cells[block.Measure]))).ToArray();
        return DataTab.Build(
            SideBySideName,
            [
                $"Engine side-by-side: ReconciliationResult.BuildSideBySide(rows [{context.Field(ReportRole.Counterparty)}], columns [{context.Field(ReportRole.Asset)}], measure {context.Field(ReportRole.Quantity)}, filter {context.Field(ReportRole.Currency)} = <ccy>), one call per currency",
                "Values exactly as returned. LeftOnly is negated by the engine; Delta = Right − Left = LeftOnly + RightOnly + MatchedDelta. Ambiguous, SearchIncomplete, Quarantined and Suppressed rows are not counted.",
            ],
            [
                DataColumn<(string Currency, SideBySideLine Line, SideBySideCell Cell)>.Of("Currency (filter)", line => line.Currency, width: 16),
                DataColumn<(string Currency, SideBySideLine Line, SideBySideCell Cell)>.Of($"Row: {context.Field(ReportRole.Counterparty)}", line => line.Line.Row, width: 18),
                DataColumn<(string Currency, SideBySideLine Line, SideBySideCell Cell)>.Of($"Column: {context.Field(ReportRole.Asset)}", line => line.Line.Column, width: 16),
                DataColumn<(string Currency, SideBySideLine Line, SideBySideCell Cell)>.Of("Left", line => line.Cell.Left, Formats.Quantity, 16),
                DataColumn<(string Currency, SideBySideLine Line, SideBySideCell Cell)>.Of("Right", line => line.Cell.Right, Formats.Quantity, 16),
                DataColumn<(string Currency, SideBySideLine Line, SideBySideCell Cell)>.Of("Delta", line => line.Cell.Delta, Formats.Quantity, 16),
                DataColumn<(string Currency, SideBySideLine Line, SideBySideCell Cell)>.Of("LeftOnly", line => line.Cell.LeftOnly, Formats.Quantity, 16),
                DataColumn<(string Currency, SideBySideLine Line, SideBySideCell Cell)>.Of("RightOnly", line => line.Cell.RightOnly, Formats.Quantity, 16),
                DataColumn<(string Currency, SideBySideLine Line, SideBySideCell Cell)>.Of("MatchedDelta", line => line.Cell.MatchedDelta, Formats.Quantity, 16),
            ],
            lines);
    }

    public static Sheet RunInfo(ReportContext context, ReportInfo info)
    {
        var run = context.Result.Run;
        var summary = context.Result.Summary;
        var version = typeof(DataTabs).Assembly.GetName().Version?.ToString() ?? "";
        ImmutableArray<(string Key, object? Value)> facts =
        [
            ("Title", context.Options.Title),
            ("Report", $"{info.Id} {info.Title} ({info.Tab})"),
            ("Profile id", context.ProfileId),
            ("Profile version", context.Options.Profile?.Version),
            ("Profile hash", run.ProfileHash),
            ("Reference data", run.ReferenceId),
            ("Reference hash", run.ReferenceHash),
            ("Engine version", run.EngineVersion),
            ("Writer", $"RecDiffy.Excel {version}"),
            ("Rows read: left", context.Result.Rows.Count(row => row.Row.Source == "left")),
            ("Rows read: right", context.Result.Rows.Count(row => row.Row.Source == "right")),
            ("Result entries", context.Result.Entries.Count),
        ];
        ImmutableArray<(string Key, int Value)> counts =
        [
            ("Matched", summary.Matched),
            ("Exact", summary.Exact),
            ("WithinTolerance", summary.WithinTolerance),
            ("Break", summary.Break),
            ("LeftOnly", summary.LeftOnly),
            ("RightOnly", summary.RightOnly),
            ("Ambiguous", summary.Ambiguous),
            ("SearchIncomplete", summary.SearchIncomplete),
            ("Quarantined", summary.Quarantined),
            ("Suppressed", summary.Suppressed),
        ];
        var summaryRow = 5 + facts.Length + 1;
        return Sheet.Empty(RunInfoName)
            .Put(1, 1, "How this report was produced", Styles.Title)
            .Put(3, 1, "As-of date", Styles.Total)
            .Put(3, 2, context.Options.AsOf, new CellStyle(Bold: true, Fill: Palette.Input, Format: Formats.Date, Align: HorizontalAlign.Left))
            .Put(3, 3, "Named cell AsOf. Ageing and settlement status are formulas on it: change it to re-age the report.", Styles.Muted)
            .Fold(facts, (sheet, fact, index) => sheet
                .Put(5 + index, 1, fact.Key, new CellStyle(Bold: true, Fill: Palette.Sub))
                .Put(5 + index, 2, fact.Value, fact.Value is int ? new CellStyle(Format: Formats.Count, Align: HorizontalAlign.Left) : CellStyle.Plain with { Wrap = true }))
            .Put(summaryRow, 1, "RunSummary", Styles.Section)
            .Header(summaryRow + 1, ["Field", "Value"], height: 18)
            .Fold(counts, (sheet, count, index) => sheet
                .Put(summaryRow + 2 + index, 1, count.Key)
                .Put(summaryRow + 2 + index, 2, count.Value, new CellStyle(Format: Formats.Count, Align: HorizontalAlign.Left)))
            .Widths([24, 90, 40]);
    }
}

internal sealed record SideBySideBlock(string Currency, string Measure, IReadOnlyList<SideBySideLine> Lines);
