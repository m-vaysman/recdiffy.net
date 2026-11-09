using System.Data;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.Tests;

public class RunTablesTests
{
    private static readonly DateTime TradeDay = new(2026, 11, 5);

    [Theory]
    [InlineData(false, 46331)]
    [InlineData(true, 44869)]
    public void A_serial_in_a_date_column_becomes_its_date_in_the_workbooks_date_system(bool uses1904, int serial)
    {
        Assert.Equal(TradeDay, RunTables.ForColumn((decimal)serial, isDate: true, uses1904));
        Assert.Equal(TradeDay, RunTables.ForColumn((double)serial, isDate: true, uses1904));
    }

    [Fact]
    public void A_date_with_a_time_keeps_its_time()
    {
        Assert.Equal(TradeDay.AddHours(14).AddMinutes(30), RunTables.ForColumn(46331.6041666667m, isDate: true, uses1904: false));
    }

    [Theory]
    [InlineData("2026-11-05")]
    [InlineData(true)]
    [InlineData(null)]
    public void A_date_column_leaves_values_that_are_not_serials_alone(object? value)
    {
        Assert.Equal(value, RunTables.ForColumn(value, isDate: true, uses1904: false));
    }

    [Fact]
    public void Other_columns_pass_through_unchanged_as_exact_decimals()
    {
        var value = RunTables.ForColumn(46331m, isDate: false, uses1904: false);

        Assert.Equal(46331m, Assert.IsType<decimal>(value));
    }

    [Fact]
    public void A_serial_Excel_has_no_date_for_stays_a_number()
    {
        Assert.Equal(60m, RunTables.ForColumn(60m, isDate: true, uses1904: false));
        Assert.Equal(-1m, RunTables.ForColumn(-1m, isDate: true, uses1904: false));
    }

    [Fact]
    public void The_data_table_has_the_headers_and_converts_only_the_declared_date_columns()
    {
        var table = new SourceTable(["Ref", "TradeDate", "Serial"], [["T-1", 46331m, 46331m], ["T-2", null, 1.5m]]) { Uses1904 = false };

        using var data = RunTables.ToDataTable(table, new HashSet<string>(["TradeDate"], StringComparer.Ordinal));

        Assert.Equal(["Ref", "TradeDate", "Serial"], data.Columns.Cast<DataColumn>().Select(column => column.ColumnName), StringComparer.Ordinal);
        Assert.Equal(["T-1", TradeDay, 46331m], data.Rows[0].ItemArray);
        Assert.Equal(["T-2", DBNull.Value, 1.5m], data.Rows[1].ItemArray);
    }

    [Fact]
    public void The_table_keeps_each_data_rows_sheet_row_number_past_dropped_empty_rows()
    {
        var range = new ExcelRangePick("Book.xlsx", "Trades", "$C$5:$D$9", 4, 2, 5, 2);

        var read = Assert.IsType<SourceRead.Read>(SourceTables.Build(range,
        [
            [new SourceCell("Ref"), new SourceCell("Quantity")],
            [new SourceCell("T-1"), new SourceCell(10m)],
            [SourceCell.Empty, SourceCell.Empty],
            [new SourceCell("T-3"), new SourceCell(30m)],
            [new SourceCell("T-4"), SourceCell.Empty],
        ]));

        Assert.Equal([6, 8, 9], read.Table.SheetRows);
    }
}
