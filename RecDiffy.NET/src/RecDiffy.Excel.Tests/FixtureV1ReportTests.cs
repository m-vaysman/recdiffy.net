using RecDiffy.Excel.Tests.Support;

namespace RecDiffy.Excel.Tests;

// Runs the fixture-v1 workbook through the engine with the WorkbookAcceptanceTests profile and writes every report it supports.
public class FixtureV1ReportTests
{
    private static readonly Lazy<IReadOnlyList<ReportOutcome>> Outcomes = new(() => FixtureV1.Result.WriteAll(Paths.Scratch("fixture-v1"), FixtureV1.Options()));

    // fixture-v1 has no lifecycle chains, allocations, net cash or tolerances; those reports are covered by SyntheticReportTests.
    public static TheoryData<ReportKind> Supported => new(
        ReportKind.ExceptionBlotter,
        ReportKind.SideBySideCompare,
        ReportKind.RawVsNormalized,
        ReportKind.ControlTotals,
        ReportKind.Scorecard,
        ReportKind.CascadeFunnel,
        ReportKind.BreakHeatmap,
        ReportKind.Ageing,
        ReportKind.AuditTrail);

    public static TheoryData<ReportKind> Unsupported => new(
        ReportKind.LifecycleChains,
        ReportKind.Allocations,
        ReportKind.CashLadder,
        ReportKind.ToleranceUsage);

    [Theory]
    [MemberData(nameof(Supported))]
    public void Writes_each_supported_report_with_its_expected_tabs(ReportKind kind)
    {
        var outcome = Outcomes.Value.Single(item => item.Kind == kind);
        Assert.True(outcome.Written, outcome.SkipReason);
        Assert.Equal(RecDiffyExcel.FileName(kind, "fixture-v1", FixtureV1.AsOf), Path.GetFileName(outcome.Path));
        var book = Book.Read(outcome.Path!);
        Assert.Equal(ExpectedTabs.Of(kind), book.SheetNames);
        Assert.Equal("Run_Info!$B$3", book.Names["AsOf"]);
        Assert.Empty(Workbooks.SchemaErrors(outcome.Path!));
    }

    [Theory]
    [MemberData(nameof(Unsupported))]
    public void Skips_the_reports_fixture_v1_has_no_data_or_fields_for_and_says_why(ReportKind kind)
    {
        var outcome = Outcomes.Value.Single(item => item.Kind == kind);
        Assert.False(outcome.Written);
        Assert.Null(outcome.Path);
        Assert.False(string.IsNullOrWhiteSpace(outcome.SkipReason));
    }

    [LibreOfficeFact]
    public void Every_written_report_recalculates_without_errors_and_every_check_passes()
    {
        var written = Outcomes.Value.Where(outcome => outcome.Written).ToArray();
        var recalculated = LibreOffice.Recalculate([.. written.Select(outcome => outcome.Path!)]);
        var books = recalculated.Select(Book.Read).ToArray();

        Assert.All(books, book => Assert.Empty(Workbooks.Uncalculated(book).Select(cell => $"{cell.Sheet}!{cell.Cell.Address}")));
        Assert.All(books, book => Assert.Empty(Workbooks.Errors(book).Select(cell => $"{cell.Sheet}!{cell.Cell.Address} {cell.Cell.Formula} = {cell.Cell.Value}")));
        Assert.All(books, book => Assert.Empty(Workbooks.Checks(book).Where(cell => cell.Cell.Value![0] is not '✓').Select(cell => $"{cell.Sheet}!{cell.Cell.Address} {cell.Cell.Formula} = {cell.Cell.Value}")));
        Assert.Contains(books, book => Workbooks.Checks(book).Any());
    }
}

internal static class ExpectedTabs
{
    public static IReadOnlyList<string> Of(ReportKind kind) => kind switch
    {
        ReportKind.ExceptionBlotter => ["BO1_ExceptionBlotter", "Result_Entries", "Result_Diffs", "Run_Info"],
        ReportKind.SideBySideCompare => ["BO2_SideBySideCompare", "Result_Entries", "Run_Info"],
        ReportKind.RawVsNormalized => ["BO3_RawVsNormalized", "Result_Entries", "Result_Diffs", "Run_Info"],
        ReportKind.LifecycleChains => ["BO4_LifecycleChains", "Result_Rows", "Run_Info"],
        ReportKind.Allocations => ["BO5_Allocations", "Result_Rows", "Run_Info"],
        ReportKind.CashLadder => ["BO6_CashLadder", "Result_Rows", "Run_Info"],
        ReportKind.ControlTotals => ["MO1_ControlTotals", "Result_Rows", "Engine_SideBySide", "Run_Info"],
        ReportKind.Scorecard => ["MO2_Scorecard", "Result_Entries", "Result_Rows", "Run_Info"],
        ReportKind.CascadeFunnel => ["MO3_CascadeFunnel", "Result_Rows", "Run_Info"],
        ReportKind.BreakHeatmap => ["MO4_BreakHeatmap", "Open_Items", "Run_Info"],
        ReportKind.Ageing => ["MO5_Ageing", "Open_Items", "Run_Info"],
        ReportKind.ToleranceUsage => ["MO6_ToleranceUsage", "Result_Diffs", "Run_Info"],
        ReportKind.AuditTrail => ["MO7_AuditTrail", "Run_Info"],
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
