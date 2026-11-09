using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.Tests;

public class SourceTablesTests
{
    private static readonly ExcelRangePick Range = new("Book.xlsx", "Trades", "$C$5:$E$7", 4, 2, 3, 3);

    [Fact]
    public void The_first_row_names_the_columns_and_the_rest_are_data()
    {
        var read = Assert.IsType<SourceRead.Read>(SourceTables.Build(Range,
        [
            [Cell("Ref"), Cell("Quantity"), Cell("Price")],
            [Cell("T-1"), Cell(10m), Cell(1.5m)],
            [Cell("T-2"), SourceCell.Empty, Cell(true)],
        ]));

        Assert.Equal(["Ref", "Quantity", "Price"], read.Table.Headers);
        Assert.Equal(2, read.Table.Rows.Count);
        Assert.Equal(["T-1", 10m, 1.5m], read.Table.Rows[0]);
        Assert.Equal(["T-2", null, true], read.Table.Rows[1]);
        Assert.Same(Range, read.Range);
    }

    [Fact]
    public void A_number_or_padded_text_in_the_header_row_becomes_its_plain_name()
    {
        var read = Assert.IsType<SourceRead.Read>(SourceTables.Build(Range, [[Cell(2024m), Cell(" Price "), Cell(true)]]));

        Assert.Equal(["2024", "Price", "True"], read.Table.Headers);
        Assert.Empty(read.Table.Rows);
    }

    [Fact]
    public void An_error_cell_rejects_the_read_and_names_the_cell_where_it_sits_in_the_sheet()
    {
        var rejected = Assert.IsType<SourceRead.Rejected>(SourceTables.Build(Range,
        [
            [Cell("Ref"), Cell("Quantity"), Cell("Price")],
            [Cell("T-1"), Cell(10m), Cell(1.5m)],
            [Cell("T-2"), SourceCell.Failed("#N/A"), Cell(2.5m)],
        ]));

        Assert.StartsWith("Cell D7 of [Book.xlsx]Trades holds #N/A.", rejected.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Unnamed_and_repeated_columns_reject_the_read()
    {
        var rejected = Assert.IsType<SourceRead.Rejected>(SourceTables.Build(Range,
            [[Cell("Ref"), SourceCell.Empty, Cell("Ref")]]));

        Assert.Equal(
            "The header row of [Book.xlsx]Trades ($C$5:$E$7) can't be used: column(s) 2 have no name; 'Ref' appear more than once.",
            rejected.Reason);
    }

    [Fact]
    public void Rows_with_no_values_are_dropped_wherever_they_sit()
    {
        var read = Assert.IsType<SourceRead.Read>(SourceTables.Build(Range,
        [
            [Cell("Ref"), Cell("Quantity"), Cell("Price")],
            [SourceCell.Empty, SourceCell.Empty, SourceCell.Empty],
            [Cell("T-1"), Cell(10m), Cell(1.5m)],
            [SourceCell.Empty, SourceCell.Empty, SourceCell.Empty],
            [Cell("T-2"), Cell(20m), Cell(2.5m)],
            [SourceCell.Empty, SourceCell.Empty, SourceCell.Empty],
            [SourceCell.Empty, SourceCell.Empty, SourceCell.Empty],
        ]));

        Assert.Equal([["T-1", 10m, 1.5m], ["T-2", 20m, 2.5m]], read.Table.Rows);
    }

    [Fact]
    public void A_row_of_blank_text_counts_as_empty()
    {
        var read = Assert.IsType<SourceRead.Read>(SourceTables.Build(Range,
        [
            [Cell("Ref"), Cell("Quantity"), Cell("Price")],
            [Cell(" "), Cell(string.Empty), Cell("\t")],
            [Cell("T-1"), Cell(10m), Cell(1.5m)],
        ]));

        Assert.Equal([["T-1", 10m, 1.5m]], read.Table.Rows);
    }

    [Fact]
    public void A_row_with_any_value_is_kept()
    {
        var read = Assert.IsType<SourceRead.Read>(SourceTables.Build(Range,
        [
            [Cell("Ref"), Cell("Quantity"), Cell("Price")],
            [SourceCell.Empty, Cell(0m), SourceCell.Empty],
            [Cell(false), SourceCell.Empty, Cell(" ")],
        ]));

        Assert.Equal([[null, 0m, null], [false, null, " "]], read.Table.Rows);
    }

    [Fact]
    public void A_range_of_only_empty_data_rows_reads_as_no_rows()
    {
        var read = Assert.IsType<SourceRead.Read>(SourceTables.Build(Range,
        [
            [Cell("Ref"), Cell("Quantity"), Cell("Price")],
            [SourceCell.Empty, SourceCell.Empty, SourceCell.Empty],
        ]));

        Assert.Empty(read.Table.Rows);
    }

    [Fact]
    public void An_error_in_an_otherwise_empty_row_still_rejects_the_read()
    {
        var rejected = Assert.IsType<SourceRead.Rejected>(SourceTables.Build(Range,
        [
            [Cell("Ref"), Cell("Quantity"), Cell("Price")],
            [SourceCell.Empty, SourceCell.Failed("#REF!"), SourceCell.Empty],
        ]));

        Assert.StartsWith("Cell D6 of [Book.xlsx]Trades holds #REF!.", rejected.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_range_is_rejected() =>
        Assert.IsType<SourceRead.Rejected>(SourceTables.Build(Range, []));

    [Theory]
    [InlineData(0, 0, "A1")]
    [InlineData(6, 3, "D7")]
    [InlineData(0, 25, "Z1")]
    [InlineData(0, 26, "AA1")]
    [InlineData(1_048_575, 16_383, "XFD1048576")]
    public void Cell_names_follow_excel(int row, int column, string expected) =>
        Assert.Equal(expected, SourceTables.CellName(row, column));

    private static SourceCell Cell(object value) => new(value);
}
