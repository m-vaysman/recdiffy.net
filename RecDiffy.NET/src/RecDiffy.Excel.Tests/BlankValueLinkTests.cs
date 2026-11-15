using System.Globalization;
using System.Text.RegularExpressions;
using RecDiffy.Excel.Reports;
using RecDiffy.Excel.Tests.Support;
using RecDiffy.Results;

namespace RecDiffy.Excel.Tests;

// #378: a report cell that links to a data-tab cell the engine left empty (a null value) must read blank, not 0 or 1900-01-00, so a blank can be
// told from a real zero and BO1's blank guards fire. The data has the issue's two pairs (a blank ISIN and a blank settle date on the right), rows
// with no settle date, no trade date and no net cash, a real zero net cash, and a block whose net cash is unknown.
public class BlankValueLinkTests
{
    private static readonly DateOnly AsOf = new(2026, 11, 4);

    private static readonly Regex Guarded = new("^IF\\(ISBLANK\\((?<ref>[^()]+)\\),\"\",\\k<ref>\\)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

    private static readonly Regex Bare = new(@"^(?:Result_Entries|Result_Rows|Result_Diffs|Open_Items)!\$[A-Z]+\$\d+$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

    private static readonly string[] DataTabNames = ["Result_Entries", "Result_Rows", "Result_Diffs", "Open_Items", "Engine_SideBySide", "Run_Info"];

    private sealed record Trade(string Id, string Instrument, string? Quantity, string? NetCash, string? Isin, string? TradeDate = "2026-11-02", string? SettleDate = "2026-11-04");

    private static readonly Lazy<(CompiledProfile Profile, IReconciliationResult Result)> Run = new(() =>
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore());
        Trade[] ours =
        [
            new("L1", "BOND-A", "1000", "99500.00", "US0378331005"),
            new("L2", "EQ-1", "500", "10000.00", "US5949181045"),
            new("L3", "EQ-2", "300", null, null, SettleDate: null),
            new("L4", "EQ-3", "400", "400.00", null, TradeDate: null, SettleDate: "2026-11-06"),
            new("L5", "EQ-4", "100", "0.00", null),
            new("L6", "EQ-5", "250", "2500.00", null),
            new("G1a", "EQ-6", "300", "3000.00", null),
            new("G1b", "EQ-6", "200", "2000.00", null),
            new("G1c", "EQ-6", "500", "5000.00", null),
            new("L7", "EQ-7", "120", "1000.00", null),
            new("G2a", "EQ-8", "100", null, null),
            new("G2b", "EQ-8", "200", "2000.00", null),
            new("G3a", "EQ-9", "50", null, null),
            new("G3b", "EQ-9", "50", null, null),
            new("G4a", "EQ-10", "10", null, null),
            new("G4b", "EQ-10", "10", null, null, SettleDate: "2026-11-05"),
        ];
        Trade[] theirs =
        [
            new("R1", "BOND-A", "1000", "99500.00", null),
            new("R3", "EQ-1", "500", "10000.00", "US5949181045", SettleDate: null),
            new("R6", "EQ-5", "250", null, null),
            new("G1", "EQ-6", "1000", null, null),
            new("R7", "EQ-7", "120", "1000.30", null),
            new("G2", "EQ-8", "300", "3000.00", null),
            new("G3", "EQ-9", "100", null, null),
            new("G4", "EQ-10", "20", null, null),
        ];
        return (profile, Reconciler.Run(profile, RecordSources.FromObjects("ours", ours, Map()), RecordSources.FromObjects("theirs", theirs, Map()), ReferenceDataSet.Empty));
    });

    private static readonly Lazy<IReadOnlyList<ReportOutcome>> Written = new(() => Run.Value.Result.WriteAll(Paths.Scratch("blank-links"), Options()));

    private static readonly Lazy<IReadOnlyDictionary<ReportKind, Book>> Recalculated = new(() =>
    {
        var written = Written.Value.Where(outcome => outcome.Written).ToArray();
        var paths = LibreOffice.Recalculate([.. written.Select(outcome => outcome.Path!)]);
        return written.Zip(paths).ToDictionary(pair => pair.First.Kind, pair => Book.Read(pair.Second));
    });

    private static ExcelReportOptions Options() => new() { AsOf = AsOf, Profile = Run.Value.Profile };

    [Fact]
    public void Link_guards_a_reference_so_an_empty_target_reads_blank()
    {
        Assert.Equal("=IF(ISBLANK(Result_Diffs!$G$2),\"\",Result_Diffs!$G$2)", ReportSheet.Link("Result_Diffs!$G$2"));
        Assert.Equal("=IF(ISBLANK(Result_Entries!$Q$3),\"\",Result_Entries!$Q$3)", ReportSheet.Link("Result_Entries!$Q$3"));
    }

    [Fact]
    public void The_engine_reports_the_blank_values_as_null_and_the_pairs_as_matched_breaks()
    {
        var facts = RecDiffyExcel.Context(Run.Value.Result, Options()).Facts;
        var isin = Assert.Single(facts.Diffs, diff => diff.Field == "Isin");
        var settle = Assert.Single(facts.Diffs, diff => diff.Field == "SettleDate" && diff.Entry.Keys(Side.Left) == "L2");

        Assert.True(isin.Theirs.IsNull);
        Assert.False(isin.Ours.IsNull);
        Assert.True(settle.Theirs.IsNull);
        Assert.All(new[] { isin.Entry, settle.Entry }, entry => Assert.Equal((Alignment.Matched, FieldOutcome.Break), (entry.Entry.Alignment, entry.Entry.Outcome)));
    }

    [Fact]
    public void The_data_has_a_many_to_one_block_so_the_allocations_report_is_written()
    {
        Assert.True(Written.Value.Single(outcome => outcome.Kind == ReportKind.Allocations).Written);
        Assert.Equal(4, Run.Value.Result.Entries.Count(entry => entry.Composition.HasFlag(Composition.ManyToOne)));
    }

    [Fact]
    public void The_data_has_a_within_tolerance_difference_so_the_tolerance_report_is_written()
    {
        Assert.True(Written.Value.Single(outcome => outcome.Kind == ReportKind.ToleranceUsage).Written);
        Assert.Contains(Run.Value.Result.Entries, entry => entry.Outcome is FieldOutcome.WithinTolerance);
    }

    // With no null in the input, a group whose members disagree on a value projects null for it, so a report that used to show 0 now shows blank.
    [Fact]
    public void A_group_whose_members_disagree_on_the_settle_date_has_none_to_show()
    {
        var item = RecDiffyExcel.Context(Run.Value.Result, Options()).Facts.OpenItems.Single(open => open.Entry is { } entry && entry.Keys(Side.Left).Contains("G4a", StringComparison.Ordinal));

        Assert.Null(item.SettleDate.Date);
    }

    [Theory]
    [InlineData("Isin", "L1")]
    [InlineData("SettleDate", "L2")]
    public void The_blotter_links_a_blank_theirs_value_through_a_guard_and_the_target_is_empty(string field, string leftKey)
    {
        var books = Books();
        var blotter = books[ReportKind.ExceptionBlotter]["BO1_ExceptionBlotter"];
        var row = RowOf(blotter, "B", Label(leftKey));
        Assert.Equal(field, blotter[$"L{row.ToInvariant()}"].Value);

        var theirs = blotter[$"N{row.ToInvariant()}"];

        Assert.Matches(Guarded, theirs.Formula!);
        Assert.Null(Target(books[ReportKind.ExceptionBlotter], theirs.Formula!)?.Value);
    }

    [Fact]
    public void The_differences_table_and_the_normalized_row_of_the_raw_report_link_through_the_guard()
    {
        var book = Books()[ReportKind.RawVsNormalized];
        var sheet = book["BO3_RawVsNormalized"];
        var blankTheirs = sheet.Values.Where(cell => cell.Formula is not null && Guarded.IsMatch(cell.Formula) && Target(book, cell.Formula) is null or { Value: null }).ToArray();

        Assert.NotEmpty(blankTheirs);
        Assert.DoesNotContain(sheet.Values, cell => cell.Formula is not null && Bare.IsMatch(cell.Formula));
    }

    [Fact]
    public void No_report_tab_links_to_a_data_cell_without_the_guard()
    {
        var bare = new[] { Books(), Synthetic(), FixtureBooks() }
            .SelectMany(books => books.Values)
            .SelectMany(book => book.Sheets.Where(sheet => DataTabNames.Contains(sheet.Key) is false).SelectMany(sheet => sheet.Value.Values.Where(cell => cell.Formula is not null && Bare.IsMatch(cell.Formula)).Select(cell => $"{sheet.Key}!{cell.Address} ={cell.Formula}")))
            .ToArray();

        Assert.Empty(bare);
    }

    [Fact]
    public void The_status_and_age_formulas_still_test_the_linked_cell_for_blank()
    {
        var blotter = Books()[ReportKind.ExceptionBlotter]["BO1_ExceptionBlotter"];
        var row = RowOf(blotter, "B", Label("L3"));

        Assert.Contains($"IF(J{row.ToInvariant()}=\"\"", blotter[$"C{row.ToInvariant()}"].Formula, StringComparison.Ordinal);
        Assert.Matches(Guarded, blotter[$"J{row.ToInvariant()}"].Formula!);
        Assert.Matches(Guarded, blotter[$"I{row.ToInvariant()}"].Formula!);
    }

    [Fact]
    public void Allocation_sums_and_checks_are_unknown_when_any_amount_in_the_group_is_blank()
    {
        var sheet = Books()[ReportKind.Allocations]["BO5_Allocations"];
        var checks = sheet.Values.Where(cell => cell.Formula is not null && cell.Formula.Contains("equal", StringComparison.Ordinal)).Select(cell => cell.Formula!).ToArray();
        var sums = sheet.Values.Where(cell => cell.Formula is not null && cell.Formula.Contains("SUM(", StringComparison.Ordinal)).Select(cell => cell.Formula!).ToArray();

        Assert.NotEmpty(checks);
        Assert.NotEmpty(sums);
        Assert.All(checks, formula => Assert.Matches("^IF\\(COUNT\\([DF]\\d+:[DF]\\d+\\)=\\d+,IF\\(.*\"– amount unknown\"\\)$", formula));
        Assert.All(checks, formula => Assert.DoesNotContain("N(", formula, StringComparison.Ordinal));
        Assert.All(sums, formula => Assert.Matches("^IF\\(COUNT\\([DF]\\d+:[DF]\\d+\\)=\\d+,SUM\\([DF]\\d+:[DF]\\d+\\),\"\"\\)$", formula));
    }

    [Fact]
    public void The_side_by_side_delta_is_blank_when_either_side_is_unknown()
    {
        var sheet = Books()[ReportKind.SideBySideCompare]["BO2_SideBySideCompare"];
        var delta = sheet.Values.Where(cell => cell.Formula is not null && cell.Formula.Contains("ISNUMBER", StringComparison.Ordinal)).ToArray();

        Assert.NotEmpty(delta);
        Assert.All(delta, cell => Assert.Matches("^IF\\(AND\\(ISNUMBER\\([A-Z]+\\d+\\),ISNUMBER\\([A-Z]+\\d+\\)\\),[A-Z]+\\d+-[A-Z]+\\d+,\"\"\\)$", cell.Formula!));
    }

    [Fact]
    public void Tolerance_usage_does_no_arithmetic_on_a_blank_delta()
    {
        foreach (var sheet in new[] { Synthetic()[ReportKind.ToleranceUsage]["MO6_ToleranceUsage"], Books()[ReportKind.ToleranceUsage]["MO6_ToleranceUsage"] })
        {
            var used = sheet.Values.Where(cell => cell.Formula is not null && cell.Formula.Contains("/H", StringComparison.Ordinal)).ToArray();
            var flag = sheet.Values.Where(cell => cell.Formula is not null && cell.Formula.Contains("On the boundary", StringComparison.Ordinal)).ToArray();

            Assert.NotEmpty(used);
            Assert.All(used, cell => Assert.Matches("^IF\\(AND\\(ISNUMBER\\(G\\d+\\),ISNUMBER\\(H\\d+\\)\\),IF\\(H\\d+=0,0,ABS\\(G\\d+\\)/H\\d+\\),\"\"\\)$", cell.Formula!));
            Assert.All(flag, cell => Assert.Contains("ISNUMBER(I", cell.Formula, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void An_impact_formula_is_only_written_where_its_net_cash_cell_is_filled()
    {
        var books = Books();
        var blotter = books[ReportKind.ExceptionBlotter]["BO1_ExceptionBlotter"];
        var impacts = blotter.Values.Where(cell => cell.Address.StartsWith('Q') && cell.Formula is { } formula && formula.StartsWith("ABS(", StringComparison.Ordinal)).ToArray();

        Assert.NotEmpty(impacts);
        Assert.All(impacts, cell => Assert.NotNull(Target(books[ReportKind.ExceptionBlotter], cell.Formula!)?.Value));
    }

    [LibreOfficeFact]
    public void Blank_values_recalculate_to_blank_and_a_real_zero_stays_zero()
    {
        var books = Recalculated.Value;
        Assert.All(books.Values, book => Assert.Empty(Workbooks.Errors(book).Select(cell => $"{cell.Sheet}!{cell.Cell.Address} {cell.Cell.Formula} = {cell.Cell.Value}")));

        var blotter = books[ReportKind.ExceptionBlotter]["BO1_ExceptionBlotter"];
        var isinRow = RowOf(blotter, "B", Label("L1"));
        var settleRow = RowOf(blotter, "B", Label("L2"));
        Assert.True(string.IsNullOrEmpty(blotter[$"N{isinRow.ToInvariant()}"].Value), $"Isin Theirs read {blotter[$"N{isinRow.ToInvariant()}"].Value}");
        Assert.True(string.IsNullOrEmpty(blotter[$"N{settleRow.ToInvariant()}"].Value), $"SettleDate Theirs read {blotter[$"N{settleRow.ToInvariant()}"].Value}");

        var noSettle = RowOf(blotter, "B", Label("L3"));
        Assert.Equal("No settle date", blotter[$"C{noSettle.ToInvariant()}"].Value);
        Assert.True(string.IsNullOrEmpty(blotter[$"J{noSettle.ToInvariant()}"].Value));

        var noTrade = RowOf(blotter, "B", Label("L4"));
        Assert.True(string.IsNullOrEmpty(blotter[$"I{noTrade.ToInvariant()}"].Value));
        Assert.True(string.IsNullOrEmpty(blotter[$"K{noTrade.ToInvariant()}"].Value));

        var zero = RowOf(blotter, "B", Label("L5"));
        Assert.Equal(0m, blotter[$"M{zero.ToInvariant()}"].Number);
    }

    [LibreOfficeFact]
    public void The_side_by_side_delta_is_blank_for_an_unknown_net_cash()
    {
        var sheet = Recalculated.Value[ReportKind.SideBySideCompare]["BO2_SideBySideCompare"];
        var column = new string([.. sheet.Values.First(cell => cell.Value?.StartsWith("Δ NetCash", StringComparison.Ordinal) == true).Address.TakeWhile(char.IsLetter)]);
        var row = Row(sheet.Values.Single(cell => cell.Address.StartsWith('C') && cell.Value == "EQ-5").Address);

        Assert.True(string.IsNullOrEmpty(sheet[$"{column}{row.ToInvariant()}"].Value));
    }

    [LibreOfficeFact]
    public void An_unknown_amount_in_an_allocation_group_never_reads_as_zero_or_equal()
    {
        var sheet = Recalculated.Value[ReportKind.Allocations]["BO5_Allocations"];
        var sums = sheet.Values.Where(cell => cell.Formula is { } formula && formula.StartsWith("IF(COUNT(", StringComparison.Ordinal) && formula.Contains(",SUM(", StringComparison.Ordinal)).ToArray();
        var blankFor = sums.Where(cell => cell.Address.StartsWith('F')).Count(cell => string.IsNullOrEmpty(cell.Value));

        // Quantities are all known and tie in the four groups; net cash is unknown in the block of G1, in a member of G2 and everywhere in G3 and G4.
        Assert.Equal(4, sheet.Values.Count(cell => cell.Value == "✓ equal"));
        Assert.Equal(4, sheet.Values.Count(cell => cell.Value == "– amount unknown"));
        Assert.Equal(3, blankFor);
        Assert.DoesNotContain(sheet.Values, cell => cell.Value is { } text && text.StartsWith("✗", StringComparison.Ordinal));
    }

    [LibreOfficeFact]
    public void Tolerance_usage_evaluates_and_its_check_passes()
    {
        var sheet = Recalculated.Value[ReportKind.ToleranceUsage]["MO6_ToleranceUsage"];
        var used = sheet.Values.Single(cell => cell.Formula is { } formula && formula.Contains("/H", StringComparison.Ordinal));
        var check = sheet.Values.Single(cell => cell.Formula is { } formula && formula.StartsWith("IF(MAX(I", StringComparison.Ordinal));

        Assert.InRange(used.Number!.Value, 0.5999m, 0.6001m);
        Assert.Equal("✓", check.Value);
        Assert.DoesNotContain(sheet.Values, cell => cell.Value == "On the boundary");
    }

    [LibreOfficeFact]
    public void The_blotter_counts_the_item_with_no_settle_date_and_its_check_passes()
    {
        var blotter = Recalculated.Value[ReportKind.ExceptionBlotter]["BO1_ExceptionBlotter"];
        var expected = RecDiffyExcel.Context(Run.Value.Result, Options()).Facts.OpenItems.Count(item => item.SettleDate.Date is null);
        var label = blotter.Values.Single(cell => cell.Address.StartsWith('C') && cell.Formula is null && cell.Value == "No settle date");
        var count = blotter[$"D{Row(label.Address).ToInvariant()}"];
        var check = blotter.Values.Single(cell => cell.Formula is { } formula && formula.Contains("COUNTBLANK", StringComparison.Ordinal));

        Assert.True(expected >= 1);
        Assert.Equal((decimal)expected, count.Number);
        Assert.Equal("✓", check.Value);
    }

    [LibreOfficeFact]
    public void The_signed_cash_of_a_row_with_unknown_net_cash_is_blank_and_the_cash_ladder_still_ties()
    {
        var books = Recalculated.Value;
        var rows = books[ReportKind.CashLadder]["Result_Rows"];
        var column = new string([.. rows.Values.Single(cell => cell.Value == "signedCash").Address.TakeWhile(char.IsLetter)]);
        var signed = rows.Values.Where(cell => cell.Address.StartsWith(column, StringComparison.Ordinal) && cell.Formula is not null).ToArray();

        Assert.Contains(signed, cell => string.IsNullOrEmpty(cell.Value));
        Assert.Contains(signed, cell => cell.Number is not null);
        Assert.DoesNotContain(Workbooks.Checks(books[ReportKind.CashLadder]), cell => cell.Cell.Value![0] is not '✓');
    }

    private static string Label(string rowKey) =>
        RecDiffyExcel.Context(Run.Value.Result, Options()).Facts.OpenItems
            .Single(item => item.Entry is { } entry ? entry.Keys(Side.Left) == rowKey : item.Row!.Row.Row.RowKey == rowKey)
            .Label;

    private static IReadOnlyDictionary<ReportKind, Book> Books() =>
        Written.Value.Where(outcome => outcome.Written).ToDictionary(outcome => outcome.Kind, outcome => Book.Read(outcome.Path!));

    private static IReadOnlyDictionary<ReportKind, Book> Synthetic() =>
        SyntheticData.Result.WriteAll(Paths.Scratch("blank-links-synthetic"), SyntheticData.Options()).Where(outcome => outcome.Written).ToDictionary(outcome => outcome.Kind, outcome => Book.Read(outcome.Path!));

    private static IReadOnlyDictionary<ReportKind, Book> FixtureBooks() =>
        FixtureV1.Result.WriteAll(Paths.Scratch("blank-links-fixture"), FixtureV1.Options()).Where(outcome => outcome.Written).ToDictionary(outcome => outcome.Kind, outcome => Book.Read(outcome.Path!));

    // Resolves a guarded reference, or the one inside ABS(...), such as Result_Diffs!$G$2, to its cell; null when the writer left the cell out.
    private static BookCell? Target(Book book, string formula)
    {
        var reference = Guarded.Match(formula) is { Success: true } match ? match.Groups["ref"].Value : formula[(formula.IndexOf('(', StringComparison.Ordinal) + 1)..formula.IndexOf(')', StringComparison.Ordinal)];
        var split = reference.Split('!');
        return book.Sheets[split[0]].TryGetValue(split[1].Replace("$", "", StringComparison.Ordinal), out var cell) ? cell : null;
    }

    private static int RowOf(IReadOnlyDictionary<string, BookCell> sheet, string column, string value) =>
        Row(sheet.Values.Single(cell => cell.Address.StartsWith(column, StringComparison.Ordinal) && cell.Address.Skip(column.Length).All(char.IsDigit) && cell.Value == value).Address);

    private static int Row(string address) => int.Parse(new string([.. address.SkipWhile(char.IsLetter)]), CultureInfo.InvariantCulture);

    private static SourceMap<Trade> Map() =>
        new SourceMap<Trade>()
            .Text("Id", trade => trade.Id)
            .Text("Counterparty", _ => "ACME")
            .Text("Instrument", trade => trade.Instrument)
            .Text("Side", _ => "BUY")
            .Text("TradeDate", trade => trade.TradeDate)
            .Text("SettleDate", trade => trade.SettleDate)
            .Text("Currency", _ => "USD")
            .Text("Quantity", trade => trade.Quantity)
            .Text("NetCash", trade => trade.NetCash)
            .Text("Isin", trade => trade.Isin);

    private const string Normalization = """
        {
          "identify": [
            { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
            { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" },
            { "rule": "symbol.map", "from": "Side", "to": "Side" },
            { "rule": "symbol.map", "from": "Currency", "to": "Currency" },
            { "rule": "text.copy", "from": "Isin", "to": "Isin" }
          ],
          "parse": [
            { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
            { "rule": "date.parse", "from": "SettleDate", "to": "SettleDate", "formats": ["yyyy-MM-dd"] },
            { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
            { "rule": "decimal.parse", "from": "NetCash", "to": "NetCash" }
          ]
        }
        """;

    private static string Profile => $$"""
        {
          "formatVersion": 1,
          "profileId": "blank-links",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Instrument", "type": "Symbol", "role": "Key" },
            { "name": "Side", "type": "Symbol", "role": "Key" },
            { "name": "TradeDate", "type": "Date", "role": "Key" },
            { "name": "SettleDate", "type": "Date", "role": "Economic" },
            { "name": "Currency", "type": "Symbol", "role": "Economic" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Isin", "type": "Text", "role": "Economic" }
          ],
          "normalization": { "left": {{Normalization}}, "right": {{Normalization}} },
          "paths": [ {
            "id": "trades",
            "route": { "rule": "route.all" },
            "passes": [
              { "id": "P2", "pass": "match.naturalKey", "key": ["Counterparty", "Instrument", "TradeDate", "Side", "Quantity"] },
              { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Counterparty", "Instrument", "TradeDate", "Side"], "conserve": ["Quantity"] }
            ],
            "diff": { "fields": { "NetCash": { "tolerance": { "absolute": "0.50" } } } }
          } ]
        }
        """;
}
