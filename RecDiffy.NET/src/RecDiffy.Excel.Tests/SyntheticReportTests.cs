using RecDiffy.Excel.Tests.Support;

namespace RecDiffy.Excel.Tests;

// The reports fixture-v1 cannot exercise (lifecycle, allocations, net cash, tolerances), on SyntheticData built with RecordSources.FromObjects
// and a profile that uses lifecycle, packages, group-sum and diff tolerances.
public class SyntheticReportTests
{
    private static readonly Lazy<IReadOnlyList<ReportOutcome>> Outcomes = new(() => SyntheticData.Result.WriteAll(Paths.Scratch("synthetic"), SyntheticData.Options()));

    private static readonly Lazy<IReadOnlyDictionary<ReportKind, Book>> Recalculated = new(() =>
    {
        var written = Outcomes.Value.Where(outcome => outcome.Written).ToArray();
        var paths = LibreOffice.Recalculate([.. written.Select(outcome => outcome.Path!)]);
        return written.Zip(paths).ToDictionary(pair => pair.First.Kind, pair => Book.Read(pair.Second));
    });

    public static TheoryData<ReportKind> All => new(Enum.GetValues<ReportKind>());

    [Theory]
    [MemberData(nameof(All))]
    public void Writes_every_report_with_its_expected_tabs(ReportKind kind)
    {
        var outcome = Outcomes.Value.Single(item => item.Kind == kind);
        Assert.True(outcome.Written, outcome.SkipReason);
        var book = Book.Read(outcome.Path!);
        Assert.Equal(ExpectedTabs.Of(kind), book.SheetNames);
        Assert.Empty(Workbooks.SchemaErrors(outcome.Path!));
    }

    [LibreOfficeFact]
    public void Every_report_recalculates_without_errors_and_every_check_passes()
    {
        var books = Recalculated.Value;
        Assert.Equal(13, books.Count);
        Assert.All(books.Values, book => Assert.Empty(Workbooks.Uncalculated(book).Select(cell => $"{cell.Sheet}!{cell.Cell.Address}")));
        Assert.All(books.Values, book => Assert.Empty(Workbooks.Errors(book).Select(cell => $"{cell.Sheet}!{cell.Cell.Address} {cell.Cell.Formula} = {cell.Cell.Value}")));
        Assert.All(books.Values, book => Assert.Empty(Workbooks.Checks(book).Where(cell => cell.Cell.Value![0] is not '✓').Select(cell => $"{cell.Sheet}!{cell.Cell.Address} {cell.Cell.Formula} = {cell.Cell.Value}")));
    }

    [Fact]
    public void Lifecycle_chain_lists_new_cancel_and_correction_in_link_order_with_the_survivor_last()
    {
        var sheet = Book.Read(Path(ReportKind.LifecycleChains))["BO4_LifecycleChains"];
        var reports = sheet.Values.Where(cell => cell.Address.StartsWith('B') && cell.Value is "R8n" or "R8c" or "R8r").OrderBy(cell => Row(cell.Address)).Select(cell => cell.Value!).ToArray();
        Assert.Equal(["R8n", "R8c", "R8r"], reports);
        Assert.Contains(sheet.Values, cell => cell.Value == "Superseded");
        Assert.Contains(sheet.Values, cell => cell.Value == "Cancel event");
        Assert.Contains(sheet.Values, cell => cell.Value is { } text && text.StartsWith("Survivor", StringComparison.Ordinal));
        Assert.Contains(sheet.Values, cell => cell.Value is { } text && text.Contains("Price 102.604 → 105.149", StringComparison.Ordinal));
    }

    [LibreOfficeFact]
    public void Allocations_add_up_to_the_block()
    {
        var sheet = Recalculated.Value[ReportKind.Allocations]["BO5_Allocations"];
        Assert.Equal(2, sheet.Values.Count(cell => cell.Value == "✓ equal"));
        Assert.Contains(sheet.Values, cell => cell.Value == "FUND-2");
    }

    [LibreOfficeFact]
    public void Tolerance_usage_flags_the_accrued_difference_that_sits_on_the_inclusive_boundary()
    {
        var sheet = Recalculated.Value[ReportKind.ToleranceUsage]["MO6_ToleranceUsage"];
        var boundary = sheet.Values.Single(cell => cell.Value == "On the boundary");
        Assert.Equal(1m, sheet[$"I{Row(boundary.Address)}"].Number);
        Assert.Equal("Accrued", sheet[$"D{Row(boundary.Address)}"].Value);
    }

    [LibreOfficeFact]
    public void Cascade_funnel_counts_the_package_as_one_item_and_the_suppressed_rows_out()
    {
        var sheet = Recalculated.Value[ReportKind.CascadeFunnel]["MO3_CascadeFunnel"];
        decimal Remaining(string stage, string column) => sheet[$"{column}{Row(sheet.Values.Single(cell => cell.Address.StartsWith('A') && cell.Value == stage).Address)}"].Number!.Value;

        Assert.Equal(13m, Remaining("Rows read", "C"));
        Assert.Equal(12m, Remaining("Rows read", "E"));
        Assert.Equal(10m, Remaining("After lifecycle collapse", "E"));
        Assert.Equal(12m, Remaining("Items after packages", "C"));
        Assert.Equal(1m, Remaining("Residue", "C"));
        Assert.Equal(1m, Remaining("Residue", "E"));
    }

    [LibreOfficeFact]
    public void Cash_ladder_moves_a_settle_date_break_between_dates_and_ties_out()
    {
        var sheet = Recalculated.Value[ReportKind.CashLadder]["BO6_CashLadder"];
        var checks = Workbooks.Checks(new Book(["BO6_CashLadder"], new Dictionary<string, IReadOnlyDictionary<string, BookCell>> { ["BO6_CashLadder"] = sheet }, new Dictionary<string, string>())).ToArray();
        Assert.NotEmpty(checks);
        Assert.All(checks, check => Assert.Equal("✓", check.Cell.Value));
        Assert.Contains(sheet.Values, cell => cell.Value == "GBP");
    }

    [LibreOfficeFact]
    public void Control_totals_count_a_package_once_and_add_its_merged_leg_back_to_tie_out_to_the_file()
    {
        var sheet = Recalculated.Value[ReportKind.ControlTotals]["MO1_ControlTotals"];
        var legs = new string([.. sheet.Values.Single(cell => cell.Value is { } text && text.StartsWith("Package legs merged into one item ours", StringComparison.Ordinal)).Address.TakeWhile(char.IsLetter)]);
        var row = Row(sheet.Values.Single(cell => cell.Address.StartsWith('B') && cell.Value == "DELTA").Address);
        var tie = sheet.Values.Where(cell => Row(cell.Address) == row && cell.Formula is not null && cell.Value is "✓" or "✗").Select(cell => cell.Value).ToArray();

        Assert.Equal(1_000_000m, sheet[$"D{row}"].Number);
        Assert.Equal(1_000_000m, sheet[$"{legs}{row}"].Number);
        Assert.All(tie, value => Assert.Equal("✓", value));
        Assert.Equal(2, tie.Length);
    }

    [Fact]
    public void Run_and_write_runs_the_engine_and_writes_every_report()
    {
        var outcomes = RecDiffyExcel.RunAndWrite(
            SyntheticData.Profile,
            SyntheticData.Source("ours", SyntheticData.Ours),
            SyntheticData.Source("theirs", SyntheticData.Theirs),
            ReferenceDataSet.Empty,
            Paths.Scratch("run-and-write"),
            new ExcelReportOptions { AsOf = SyntheticData.AsOf });
        Assert.Equal(13, outcomes.Count);
        Assert.All(outcomes, outcome => Assert.True(outcome.Written, outcome.SkipReason));
        Assert.All(outcomes, outcome => Assert.EndsWith("_synthetic_2026-11-04.xlsx", outcome.FileName, StringComparison.Ordinal));
    }

    private static string Path(ReportKind kind) => Outcomes.Value.Single(outcome => outcome.Kind == kind).Path!;

    private static int Row(string address) => int.Parse(new string([.. address.SkipWhile(char.IsLetter)]), System.Globalization.CultureInfo.InvariantCulture);
}
