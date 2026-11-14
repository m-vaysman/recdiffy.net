using RecDiffy.Results;

namespace RecDiffy.IO.Tests.OracleMatrix;

// Fixture v1 against its Expected Results tab, read from the workbook through FromExcel, with the profile and reference
// data of WorkbookAcceptanceTests. Those tests check each case's rows; these add what AC-1 to AC-5 ask beyond that:
// no other entries, composition, the field a break is blamed on, and row accounting.
public class FixtureV1OracleTests
{
    private static readonly Lazy<IReconciliationResult> Result = new(() => FixtureV1.Run(
        FileSources.FromExcel("left", OracleDatasets.FixtureV1.Workbook, new ExcelOptions { HasHeader = true, Sheet = ExcelSheet.Numbered(1) }),
        FileSources.FromExcel("right", OracleDatasets.FixtureV1.Workbook, new ExcelOptions { HasHeader = true, Sheet = ExcelSheet.Numbered(2) })));

    private static OracleComparison Compare() => new(Result.Value, OracleDatasets.FixtureV1.Cases());

    // The Expected Results tab names a break by its source column; the profile's field for each.
    private static string? Field(string column) => column switch
    {
        "Direction" => "Side",
        "InstrumentID" => "Instrument",
        "PriceOrRate" => "Price",
        "SettleDate" or "MaturityDate" or "Currency" or "Quantity" or "FixedRate" => column,
        _ => null,
    };

    [Fact]
    public void AC1_each_case_is_one_entry_with_exactly_its_rows_and_there_are_no_other_entries() =>
        OracleAssert.None(Compare().Membership());

    [Fact]
    public void AC2_each_entry_has_the_alignment_composition_and_outcome_its_label_maps_to() =>
        OracleAssert.None(Compare().AllAxes(allocationsArePackages: false));

    [Fact]
    public void AC3_each_break_is_rooted_in_the_field_the_oracle_names_and_no_other() =>
        OracleAssert.None(Compare().BreakRoots(Field));

    [Fact]
    public void AC5_every_input_row_has_exactly_one_disposition() =>
        OracleAssert.None(Compare().RowAccounting(OracleDatasets.FixtureV1.LeftRows(), OracleDatasets.FixtureV1.RightRows()));
}
