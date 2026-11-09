using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.Tests;

public class ExcelDatesTests
{
    [Theory]
    [InlineData("D1", CellShape.Date)]
    [InlineData("D4", CellShape.Date)]
    [InlineData("D5", CellShape.Date)]
    [InlineData("D6", CellShape.Time)]
    [InlineData("D9", CellShape.Time)]
    [InlineData("G", CellShape.Value)]
    [InlineData("F2", CellShape.Value)]
    [InlineData(",2", CellShape.Value)]
    [InlineData("C2-", CellShape.Value)]
    [InlineData("P0", CellShape.Value)]
    [InlineData("D10", CellShape.Value)]
    [InlineData("", CellShape.Value)]
    public void Excel_format_codes_D1_to_D5_are_dates_and_D6_to_D9_are_times(string code, CellShape expected) =>
        Assert.Equal(expected, ExcelDates.ShapeOf(code));

    [Theory]
    [InlineData("yyyy-mm-dd", CellShape.Date)]
    [InlineData("m/d/yyyy", CellShape.Date)]
    [InlineData("dd.mm.yyyy", CellShape.Date)]
    [InlineData("mmmm d, yyyy", CellShape.Date)]
    [InlineData("[$-409]mmmm d, yyyy;@", CellShape.Date)]
    [InlineData("ddd", CellShape.Date)]
    [InlineData("mmm", CellShape.Date)]
    [InlineData("m/d/yyyy h:mm", CellShape.Date)]
    [InlineData("h:mm", CellShape.Time)]
    [InlineData("h:mm AM/PM", CellShape.Time)]
    [InlineData("[h]:mm:ss", CellShape.Time)]
    [InlineData("mm:ss", CellShape.Time)]
    [InlineData("General", CellShape.Value)]
    [InlineData("0.00", CellShape.Value)]
    [InlineData("#,##0.00 \"days\"", CellShape.Value)]
    [InlineData("0 \\d", CellShape.Value)]
    [InlineData("[Red]#,##0;[Blue]-#,##0", CellShape.Value)]
    [InlineData("[$€-407] #,##0.00", CellShape.Value)]
    [InlineData("0.00E+00", CellShape.Value)]
    [InlineData("@", CellShape.Value)]
    [InlineData("0;\"-\";yyyy", CellShape.Value)]
    public void A_number_format_is_a_date_or_a_time_only_when_its_codes_say_so(string format, CellShape expected) =>
        Assert.Equal(expected, ExcelDates.ShapeOfFormat(format));

    [Theory]
    [InlineData(46331, false, 2026, 11, 5)]
    [InlineData(44869, true, 2026, 11, 5)]
    [InlineData(1, false, 1900, 1, 1)]
    [InlineData(59, false, 1900, 2, 28)]
    [InlineData(61, false, 1900, 3, 1)]
    [InlineData(0, true, 1904, 1, 1)]
    public void A_serial_becomes_its_date_in_the_workbooks_date_system(double serial, bool uses1904, int year, int month, int day) =>
        Assert.Equal(new DateTime(year, month, day), ExcelDates.DateOf(serial, uses1904));

    [Theory]
    [InlineData(60)]
    [InlineData(0.5)]
    [InlineData(-1)]
    [InlineData(3e6)]
    public void Serials_with_no_real_date_in_the_1900_system_stay_numbers(double serial) =>
        Assert.Null(ExcelDates.DateOf(serial, uses1904: false));

    [Fact]
    public void A_date_with_a_time_keeps_the_time_rounded_to_the_second() =>
        Assert.Equal(new DateTime(2026, 11, 5, 14, 30, 0), ExcelDates.DateOf(46331.604166666664, uses1904: false));

    [Theory]
    [InlineData(0.5, 12, 0, 0)]
    [InlineData(0.604166666, 14, 30, 0)]
    [InlineData(46331.75, 18, 0, 0)]
    [InlineData(0.99999999, 0, 0, 0)]
    public void A_time_keeps_only_the_time_of_day(double serial, int hour, int minute, int second) =>
        Assert.Equal(new TimeOnly(hour, minute, second), ExcelDates.TimeOf(serial));

    [Fact]
    public void Only_numbers_shown_as_dates_or_times_change()
    {
        Assert.Equal(new DateTime(2026, 11, 5), ExcelDates.Typed(46331m, CellShape.Date, uses1904: false));
        Assert.Equal(new TimeOnly(12, 0), ExcelDates.Typed(0.5, CellShape.Time, uses1904: false));
        Assert.Equal(46331m, ExcelDates.Typed(46331m, CellShape.Value, uses1904: false));
        Assert.Equal("2026-11-05", ExcelDates.Typed("2026-11-05", CellShape.Date, uses1904: false));
        Assert.Equal(60m, ExcelDates.Typed(60m, CellShape.Date, uses1904: false));
        Assert.Null(ExcelDates.Typed(null, CellShape.Date, uses1904: false));
    }

    [Fact]
    public void The_kept_rows_are_the_data_rows_with_values_counted_from_the_header()
    {
        IReadOnlyList<IReadOnlyList<SourceCell>> rows =
        [
            [new SourceCell("Ref")],
            [new SourceCell("T-1")],
            [SourceCell.Empty],
            [new SourceCell(" ")],
            [new SourceCell("T-2")],
        ];

        Assert.Equal([1, 4], SourceTables.KeptRows(rows));
        Assert.Empty(SourceTables.KeptRows([[new SourceCell("Ref")]]));
        Assert.Empty(SourceTables.KeptRows([]));
    }

    [Fact]
    public void A_read_carries_a_preview_of_the_kept_rows()
    {
        var range = new ExcelRangePick("Book.xlsx", "Trades", "$A$1:$A$4", 0, 0, 4, 1);

        var read = Assert.IsType<SourceRead.Read>(SourceTables.Build(range, [[new SourceCell("Ref")], [new SourceCell("T-1")], [SourceCell.Empty], [new SourceCell("T-2")]]));

        Assert.Equal([["T-1"], ["T-2"]], read.Preview.Rows);
        Assert.Equal(2, read.Preview.RowCount);
    }
}
