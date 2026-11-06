using RecDiffy.Excel.Tests.Support;

namespace RecDiffy.Excel.Tests;

public class ApiTests
{
    [Fact]
    public void Names_files_by_id_report_profile_and_as_of_date()
    {
        Assert.Equal("BO1_ExceptionBlotter_fixture-v1_2026-11-04.xlsx", RecDiffyExcel.FileName(ReportKind.ExceptionBlotter, "fixture-v1", new DateOnly(2026, 11, 4)));
        Assert.Equal("MO7_AuditTrail_my-profile-v2_2026-01-31.xlsx", RecDiffyExcel.FileName(ReportKind.AuditTrail, "my profile/v2", new DateOnly(2026, 1, 31)));
    }

    [Fact]
    public void Skips_a_report_whose_required_roles_are_unmapped_and_names_them()
    {
        var options = FixtureV1.Options(ReportKind.ControlTotals) with { FieldMap = FieldMap.FromProfile(FixtureV1.Profile).Without(ReportRole.Quantity).Without(ReportRole.Asset) };
        var outcome = Assert.Single(FixtureV1.Result.WriteAll(Paths.Scratch("unmapped"), options));

        Assert.False(outcome.Written);
        Assert.Null(outcome.Path);
        Assert.Contains("Asset", outcome.SkipReason, StringComparison.Ordinal);
        Assert.Contains("Quantity", outcome.SkipReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Reads_fields_only_through_the_field_map()
    {
        var map = FieldMap.FromProfile(FixtureV1.Profile);
        Assert.Equal("Asset", map[ReportRole.Asset]);
        Assert.Null(map[ReportRole.NetCash]);
        Assert.Equal("AssetClass", map.With(ReportRole.Asset, "AssetClass")[ReportRole.Asset]);
        Assert.Equal("NetCash", FieldMap.FromProfile(SyntheticData.Profile)[ReportRole.NetCash]);
        Assert.Equal("EventAction", FieldMap.FromProfile(SyntheticData.Profile)[ReportRole.LifecycleAction]);
    }

    [Fact]
    public void Applies_label_overrides_to_headings_and_columns()
    {
        var options = FixtureV1.Options() with { Labels = new Dictionary<string, string> { ["Counterparty"] = "Cpty", ["Control totals"] = "Book-to-street totals" } };
        using var stream = new MemoryStream();
        var outcome = FixtureV1.Result.WriteReport(ReportKind.ControlTotals, stream, options);
        Assert.True(outcome.Written, outcome.SkipReason);

        var path = Path.Combine(Paths.Scratch("labels"), outcome.FileName);
        File.WriteAllBytes(path, stream.ToArray());
        var sheet = Book.Read(path)["MO1_ControlTotals"];
        Assert.Equal("MO1 · Book-to-street totals", sheet["A1"].Value);
        Assert.Contains(sheet.Values, cell => cell.Value == "Cpty");
        Assert.DoesNotContain(sheet.Values, cell => cell.Value == "Counterparty");
    }

    [Fact]
    public void Writes_one_report_to_a_stream_and_writes_nothing_when_it_is_skipped()
    {
        using var written = new MemoryStream();
        var outcome = FixtureV1.Result.WriteReport(ReportKind.Scorecard, written, FixtureV1.Options());
        Assert.True(outcome.Written);
        Assert.Null(outcome.Path);
        Assert.True(written.Length > 0);

        using var skipped = new MemoryStream();
        var skip = FixtureV1.Result.WriteReport(ReportKind.CashLadder, skipped, FixtureV1.Options());
        Assert.False(skip.Written);
        Assert.Equal(0, skipped.Length);
    }

    [Fact]
    public void Writes_only_the_selected_reports()
    {
        var outcomes = FixtureV1.Result.WriteAll(Paths.Scratch("selected"), FixtureV1.Options(ReportKind.Scorecard, ReportKind.BreakHeatmap));
        Assert.Equal([ReportKind.Scorecard, ReportKind.BreakHeatmap], outcomes.Select(outcome => outcome.Kind));
    }

    [Fact]
    public void Status_and_age_are_formulas_on_the_as_of_name()
    {
        var outcome = Assert.Single(FixtureV1.Result.WriteAll(Paths.Scratch("asof"), FixtureV1.Options(ReportKind.Ageing)));
        var items = Book.Read(outcome.Path!)["Open_Items"];
        Assert.Contains("AsOf", items["H2"].Formula, StringComparison.Ordinal);
        Assert.Contains("NETWORKDAYS", items["I2"].Formula, StringComparison.Ordinal);
    }

    [LibreOfficeFact]
    public void Changing_the_as_of_date_re_ages_the_report()
    {
        var early = SyntheticData.Result.WriteAll(Paths.Scratch("early"), SyntheticData.Options(ReportKind.Ageing) with { AsOf = new DateOnly(2026, 11, 4) }).Single().Path!;
        var late = SyntheticData.Result.WriteAll(Paths.Scratch("late"), SyntheticData.Options(ReportKind.Ageing) with { AsOf = new DateOnly(2026, 11, 10) }).Single().Path!;
        var books = LibreOffice.Recalculate([early, late]).Select(Book.Read).ToArray();

        decimal Past(Book book) => book["Open_Items"].Values.Count(cell => cell.Address.StartsWith('H') && cell.Value == "Past settle date");
        Assert.True(Past(books[1]) > Past(books[0]));
    }
}
