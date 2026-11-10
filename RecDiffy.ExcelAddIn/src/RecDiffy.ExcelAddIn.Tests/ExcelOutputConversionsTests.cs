using System.Globalization;
using ExcelDna.Integration;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.Tests;

public class ExcelOutputConversionsTests
{
    [Theory]
    [InlineData("T-1")]
    [InlineData("007")]
    [InlineData("=1+1")]
    [InlineData("=HYPERLINK(\"http://example.invalid\")")]
    public void Text_is_written_as_the_same_text(string text) =>
        Assert.Equal(text, ExcelOutputConversions.ToExcelValue(new SheetValue.Text(text)));

    [Theory]
    [InlineData("10", 10d)]
    [InlineData("1.50", 1.5d)]
    [InlineData("0.1", 0.1d)]
    [InlineData("-1234.5678", -1234.5678d)]
    [InlineData("1000000", 1_000_000d)]
    public void A_decimal_a_double_holds_exactly_is_written_as_a_number(string text, double expected) =>
        Assert.Equal(expected, ExcelOutputConversions.ToExcelValue(new SheetValue.Number(Parse(text))));

    [Theory]
    [InlineData("12345678901234567890.123")]
    [InlineData("0.1234567890123456789")]
    [InlineData("79228162514264337593543950335")]
    public void A_decimal_a_double_cannot_hold_is_written_as_its_exact_text(string text) =>
        Assert.Equal(text, ExcelOutputConversions.ToExcelValue(new SheetValue.Number(Parse(text))));

    [Theory]
    [InlineData(2000, 1, 1, 36526d)]
    [InlineData(1900, 1, 1, 1d)]
    [InlineData(1900, 2, 28, 59d)]
    [InlineData(1900, 3, 1, 61d)]
    [InlineData(2026, 11, 10, 46336d)]
    public void A_date_is_written_as_its_1900_serial(int year, int month, int day, double serial) =>
        Assert.Equal(serial, ExcelOutputConversions.ToExcelValue(new SheetValue.Date(new DateTime(year, month, day), HasTime: false)));

    [Fact]
    public void A_date_and_time_keeps_the_time_as_the_fraction_of_a_day() =>
        Assert.Equal(36526.75d, (double)ExcelOutputConversions.ToExcelValue(new SheetValue.Date(new DateTime(2000, 1, 1, 18, 0, 0), HasTime: true)), 9);

    [Fact]
    public void A_date_before_1900_is_written_as_text() =>
        Assert.Equal("1899-12-31", ExcelOutputConversions.ToExcelValue(new SheetValue.Date(new DateTime(1899, 12, 31), HasTime: false)));

    [Fact]
    public void An_empty_cell_is_written_empty() =>
        Assert.Equal(ExcelEmpty.Value, ExcelOutputConversions.ToExcelValue(SheetValue.Empty));

    [Fact]
    public void Text_longer_than_a_cell_holds_is_cut_and_marked()
    {
        var written = Assert.IsType<string>(ExcelOutputConversions.ToExcelValue(new SheetValue.Text(new string('x', 40_000))));

        Assert.Equal(ExcelOutputConversions.MaxCellText, written.Length);
        Assert.EndsWith("…", written, StringComparison.Ordinal);
    }

    private static decimal Parse(string text) => decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
}
