using System.Globalization;
using RecDiffy.Excel.Tests.Support;
using RecDiffy.Results;

namespace RecDiffy.Excel.Tests;

// Findings from the GROK_sniff review of #90. A dataset with an Equity-only route: Bond rows are never routed, one Equity row is excluded as
// invalid (required Quantity missing), and one unmatched Equity row has no net cash. The engine shows the first three as LeftOnly or RightOnly
// rows with no entry, so RunSummary does not count them (#92); the Excel reports must still list and count them.
public class ReviewFindingsTests
{
    private static readonly DateOnly AsOf = new(2026, 11, 4);

    private static readonly Lazy<(CompiledProfile Profile, ReconciliationResult Result)> Run = new(() =>
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore());
        Trade[] ours =
        [
            new("L1", "ACME", "Equity", "100", "1000.00"),
            new("L2", "ACME", "Bond", "500", "500.00"),
            new("L3", "BETA", "Equity", "7", null),
            new("L4", "BETA", "Equity", null, "70.00"),
        ];
        Trade[] theirs = [new("R1", "ACME", "Equity", "100", "1000.00"), new("R2", "ACME", "Bond", "900", "900.00")];
        return (profile, Reconciler.Run(profile, RecordSources.FromObjects("ours", ours, Map()), RecordSources.FromObjects("theirs", theirs, Map()), ReferenceDataSet.Empty));
    });

    private static readonly Lazy<IReadOnlyList<ReportOutcome>> Written = new(() =>
        Run.Value.Result.WriteAll(Paths.Scratch("review"), new ExcelReportOptions { AsOf = AsOf, Profile = Run.Value.Profile }));

    [Fact]
    public void The_engine_shows_unrouted_and_invalid_rows_without_an_entry_and_leaves_them_out_of_the_summary()
    {
        var result = Run.Value.Result;
        var orphans = result.Rows.Where(row => row.EntryId is null).Select(row => $"{row.Row.RowKey}:{row.Disposition}:{row.Validity}").Order().ToArray();

        Assert.Equal(["L2:LeftOnly:Valid", "L4:LeftOnly:Invalid", "R2:RightOnly:Valid"], orphans);
        Assert.Equal(1, result.Summary.LeftOnly);
        Assert.Equal(0, result.Summary.RightOnly);
    }

    [Fact]
    public void Open_items_include_rows_the_engine_left_without_an_entry()
    {
        var byLabel = Items().ToDictionary(RowsOf, StringComparer.Ordinal);

        Assert.Equal(["L2", "L3", "L4", "R2"], byLabel.Keys.Order());
        Assert.NotNull(byLabel["L3"].Entry);
        Assert.Equal(("Unmatched ours", "(not routed)"), (byLabel["L2"].ItemType, byLabel["L2"].Field));
        Assert.Equal(("Unmatched ours", "(excluded: invalid)"), (byLabel["L4"].ItemType, byLabel["L4"].Field));
        Assert.Equal(("Unmatched theirs", "(not routed)"), (byLabel["R2"].ItemType, byLabel["R2"].Field));
        Assert.Contains("RequiredMissing", byLabel["L4"].Action, StringComparison.Ordinal);
    }

    [Fact]
    public void Impact_is_blank_when_net_cash_is_unknown_not_a_measured_zero()
    {
        var l3 = Items().Single(item => RowsOf(item) == "L3");
        Assert.Null(l3.Impact);
        Assert.Equal(500m, Items().Single(item => RowsOf(item) == "L2").Impact);

        var blotter = Book.Read(Written.Value.Single(outcome => outcome.Kind == ReportKind.ExceptionBlotter).Path!)["BO1_ExceptionBlotter"];
        var row = Row(blotter.Values.Single(cell => cell.Address.StartsWith('B') && cell.Value == l3.Label).Address);
        Assert.False(blotter.TryGetValue($"Q{row}", out var impact) && (impact.Formula is not null || impact.Value is not null));
    }

    [LibreOfficeFact]
    public void Blotter_heatmap_ageing_and_scorecard_count_the_rows_without_an_entry_and_every_check_passes()
    {
        var written = Written.Value.Where(outcome => outcome.Written).ToArray();
        var books = written.Zip(LibreOffice.Recalculate([.. written.Select(outcome => outcome.Path!)]).Select(Book.Read)).ToDictionary(pair => pair.First.Kind, pair => pair.Second);

        Assert.All(books.Values, book => Assert.Empty(Workbooks.Errors(book).Select(cell => $"{cell.Sheet}!{cell.Cell.Address} {cell.Cell.Formula}")));
        Assert.All(books.Values, book => Assert.Empty(Workbooks.Checks(book).Where(cell => cell.Cell.Value![0] is not '✓').Select(cell => $"{cell.Sheet}!{cell.Cell.Address} {cell.Cell.Formula}")));

        var blotter = books[ReportKind.ExceptionBlotter]["BO1_ExceptionBlotter"];
        string[] labels = [.. Items().Select(item => item.Label).Order(StringComparer.Ordinal)];
        Assert.Equal(labels, blotter.Values.Where(cell => cell.Address.StartsWith('B') && labels.Contains(cell.Value)).Select(cell => cell.Value!).Order(StringComparer.Ordinal));
        Assert.Contains(blotter.Values, cell => cell.Value == "(not routed)");
        Assert.Contains(blotter.Values, cell => cell.Value == "(excluded: invalid)");

        var scorecard = books[ReportKind.Scorecard]["MO2_Scorecard"];
        decimal Tile(string label) => scorecard[Below(scorecard.Values.Where(cell => cell.Value == label).MinBy(cell => Row(cell.Address))!.Address)].Number!.Value;
        Assert.Equal(3m, Tile("Unmatched ours"));
        Assert.Equal(1m, Tile("Unmatched theirs"));

        var heatmap = books[ReportKind.BreakHeatmap]["MO4_BreakHeatmap"];
        var total = Row(heatmap.Values.Single(cell => cell.Address.StartsWith('A') && cell.Value == "Total").Address);
        Assert.Contains(heatmap.Values, cell => Row(cell.Address) == total && cell.Number == 4m);
    }

    [Theory]
    [InlineData("../../tmp", "MO2_Scorecard_tmp_2026-11-04.xlsx")]
    [InlineData("..", "MO2_Scorecard_profile_2026-11-04.xlsx")]
    [InlineData("   ", "MO2_Scorecard_profile_2026-11-04.xlsx")]
    [InlineData("v1..2", "MO2_Scorecard_v1-2_2026-11-04.xlsx")]
    [InlineData("C:\\reports\\desk", "MO2_Scorecard_C-reports-desk_2026-11-04.xlsx")]
    [InlineData("desk:2026|v1*?<>\"", "MO2_Scorecard_desk-2026-v1_2026-11-04.xlsx")]
    [InlineData("fixture-v1", "MO2_Scorecard_fixture-v1_2026-11-04.xlsx")]
    public void A_profile_id_becomes_one_clean_file_name_segment(string profileId, string expected) =>
        Assert.Equal(expected, RecDiffyExcel.FileName(ReportKind.Scorecard, profileId, AsOf));

    [Fact]
    public void Run_and_write_and_write_all_observe_cancellation_and_write_nothing()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var runFolder = Path.Combine(Paths.Scratch("cancel"), "run");
        var writeFolder = Path.Combine(Paths.Scratch("cancel"), "write");
        Trade[] rows = [new("L1", "ACME", "Equity", "100", "1000.00")];

        Assert.ThrowsAny<OperationCanceledException>(() => RecDiffyExcel.RunAndWrite(
            Run.Value.Profile,
            RecordSources.FromObjects("ours", rows, Map()),
            RecordSources.FromObjects("theirs", rows, Map()),
            ReferenceDataSet.Empty,
            runFolder,
            new ExcelReportOptions { AsOf = AsOf },
            cancelled.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => Run.Value.Result.WriteAll(writeFolder, new ExcelReportOptions { AsOf = AsOf, Profile = Run.Value.Profile }, cancelled.Token));
        Assert.False(Directory.Exists(runFolder));
        Assert.False(Directory.Exists(writeFolder));
    }

    private static IReadOnlyList<Facts.OpenItem> Items() =>
        RecDiffyExcel.Context(Run.Value.Result, new ExcelReportOptions { AsOf = AsOf, Profile = Run.Value.Profile }).Facts.OpenItems;

    private static string RowsOf(Facts.OpenItem item) => item.Entry is { } entry ? entry.Keys(Side.Left) + entry.Keys(Side.Right) : item.Row!.Row.Row.RowKey;

    private static int Row(string address) => int.Parse(new string([.. address.SkipWhile(char.IsLetter)]), CultureInfo.InvariantCulture);

    private static string Below(string address) => $"{new string([.. address.TakeWhile(char.IsLetter)])}{Row(address) + 1}";

    private sealed record Trade(string Id, string Counterparty, string Asset, string? Quantity, string? NetCash);

    private static SourceMap<Trade> Map() =>
        new SourceMap<Trade>()
            .Text("Id", trade => trade.Id)
            .Text("Counterparty", trade => trade.Counterparty)
            .Text("Instrument", trade => trade.Asset + "-1")
            .Text("Asset", trade => trade.Asset)
            .Text("Side", _ => "BUY")
            .Text("TradeDate", _ => "2026-11-02")
            .Text("SettleDate", _ => "2026-11-04")
            .Text("Currency", _ => "USD")
            .Text("Quantity", trade => trade.Quantity)
            .Text("NetCash", trade => trade.NetCash);

    private const string Normalization = """
        {
          "identify": [
            { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
            { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" },
            { "rule": "symbol.map", "from": "Asset", "to": "Asset" },
            { "rule": "symbol.map", "from": "Side", "to": "Side" },
            { "rule": "symbol.map", "from": "Currency", "to": "Currency" }
          ],
          "parse": [
            { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
            { "rule": "date.parse", "from": "SettleDate", "to": "SettleDate", "formats": ["yyyy-MM-dd"] },
            { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
            { "rule": "decimal.parse", "from": "NetCash", "to": "NetCash" }
          ],
          "validate": [ { "rule": "required.present", "fields": ["Quantity"] } ]
        }
        """;

    private static string Profile => $$"""
        {
          "formatVersion": 1,
          "profileId": "review-findings",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Instrument", "type": "Symbol", "role": "Key" },
            { "name": "Asset", "type": "Symbol", "role": "Key" },
            { "name": "Side", "type": "Symbol", "role": "Key" },
            { "name": "TradeDate", "type": "Date", "role": "Key" },
            { "name": "SettleDate", "type": "Date", "role": "Economic" },
            { "name": "Currency", "type": "Symbol", "role": "Economic" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": { "left": {{Normalization}}, "right": {{Normalization}} },
          "paths": [ {
            "id": "equity",
            "route": { "rule": "route.byField", "field": "Asset", "equals": "Equity" },
            "passes": [ { "id": "P2", "pass": "match.naturalKey", "key": ["Counterparty", "Instrument", "TradeDate", "Side", "Quantity"] } ]
          } ]
        }
        """;
}
