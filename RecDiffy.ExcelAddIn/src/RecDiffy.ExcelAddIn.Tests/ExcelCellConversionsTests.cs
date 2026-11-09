using ExcelDna.Integration;

namespace RecDiffy.ExcelAddIn.Tests;

public class ExcelCellConversionsTests
{
    [Fact]
    public void Empty_and_missing_cells_become_null()
    {
        Assert.Equal(SourceCell.Empty, ExcelCellConversions.ToSourceCell(ExcelEmpty.Value));
        Assert.Equal(SourceCell.Empty, ExcelCellConversions.ToSourceCell(ExcelMissing.Value));
    }

    [Fact]
    public void Text_stays_text_as_excel_stores_it() =>
        Assert.Equal(new SourceCell("  007 "), ExcelCellConversions.ToSourceCell("  007 "));

    [Theory]
    [InlineData(10d, "10")]
    [InlineData(0.1d, "0.1")]
    [InlineData(-1234.5678d, "-1234.5678")]
    [InlineData(0.032600000000000004d, "0.032600000000000004")]
    public void A_number_becomes_the_exact_decimal_the_workbook_stores(double number, string expected)
    {
        var cell = ExcelCellConversions.ToSourceCell(number);

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), Assert.IsType<decimal>(cell.Value));
        Assert.Null(cell.Error);
    }

    [Fact]
    public void A_number_no_decimal_holds_exactly_stays_a_double_for_the_engine_to_report() =>
        Assert.Equal(1e30, Assert.IsType<double>(ExcelCellConversions.ToSourceCell(1e30).Value));

    [Fact]
    public void True_and_false_stay_booleans()
    {
        Assert.Equal(new SourceCell(true), ExcelCellConversions.ToSourceCell(true));
        Assert.Equal(new SourceCell(false), ExcelCellConversions.ToSourceCell(false));
    }

    [Theory]
    [InlineData(ExcelError.ExcelErrorNull, "#NULL!")]
    [InlineData(ExcelError.ExcelErrorDiv0, "#DIV/0!")]
    [InlineData(ExcelError.ExcelErrorValue, "#VALUE!")]
    [InlineData(ExcelError.ExcelErrorRef, "#REF!")]
    [InlineData(ExcelError.ExcelErrorName, "#NAME?")]
    [InlineData(ExcelError.ExcelErrorNum, "#NUM!")]
    [InlineData(ExcelError.ExcelErrorNA, "#N/A")]
    [InlineData(ExcelError.ExcelErrorGettingData, "#GETTING_DATA")]
    public void An_error_cell_is_a_failed_conversion_never_text(ExcelError error, string expected)
    {
        var cell = ExcelCellConversions.ToSourceCell(error);

        Assert.Null(cell.Value);
        Assert.Equal(expected, cell.Error);
    }

    [Fact]
    public void The_conversion_is_registered_with_excel_dna_at_startup()
    {
        var method = typeof(ExcelCellConversions).GetMethod(nameof(ExcelCellConversions.ToSourceCell))!;

        Assert.True(method.IsPublic && method.IsStatic);
        Assert.Single(method.GetParameters());
        Assert.NotNull(method.GetCustomAttributes(typeof(ExcelParameterConversionAttribute), inherit: false).SingleOrDefault());
    }
}
