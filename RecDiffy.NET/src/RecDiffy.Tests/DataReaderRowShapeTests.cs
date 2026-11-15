using System.Data;
using System.Globalization;

namespace RecDiffy.Tests;

// #403 item 2: each reader row is shaped by Enumerate and ToRow, which now fill a sized array and dictionary directly.
public class DataReaderRowShapeTests
{
    [Fact]
    public void A_reader_row_carries_each_cell_with_its_expected_type_and_trimming()
    {
        using var table = Table();

        var rows = Read(table);

        Assert.Equal(["1", "2"], rows.Select(row => row.Ref.RowKey), StringComparer.Ordinal);

        var first = rows[0];
        Assert.Equal(
            ["Amount", "Blank", "Count", "Id", "Name", "Nothing", "When"],
            first.Cells.Keys.Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
        Assert.Equal("Alice", first.Cells["Name"]);
        Assert.Equal(12.34m, first.Cells["Amount"]);
        Assert.Equal(5m, first.Cells["Count"]);
        Assert.Equal(new DateOnly(2024, 1, 15), first.Cells["When"]);
        Assert.Null(first.Cells["Blank"]);
        Assert.Null(first.Cells["Nothing"]);

        Assert.Equal("Bob", rows[1].Cells["Name"]);
        Assert.Equal(99m, rows[1].Cells["Amount"]);
    }

    [Fact]
    public void Reading_a_thousand_wide_rows_allocates_under_1000_bytes_per_row()
    {
        using var table = WideTable(1_000);

        Read(table);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var rows = Read(table);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var perRow = (double)allocated / rows.Count;

        Assert.True(perRow < 1_000, $"Reading a wide table allocated {perRow.ToInvariant("0.0")} bytes per row.");
    }

    private static IReadOnlyList<RawRecord> Read(DataTable table) =>
        RecordSources.FromDataReader("left", table.CreateDataReader, new TabularOptions()).Materialize("left", "Id", CancellationToken.None);

    private static DataTable Table()
    {
        var table = new DataTable { Locale = CultureInfo.InvariantCulture };
#pragma warning disable IDISP004 // The table owns the columns it adds.
        table.Columns.Add("Id", typeof(string));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Amount", typeof(decimal));
        table.Columns.Add("Count", typeof(int));
        table.Columns.Add("When", typeof(DateTime));
        table.Columns.Add("Blank", typeof(string));
        table.Columns.Add("Nothing", typeof(object));
#pragma warning restore IDISP004
        table.Rows.Add("1", " Alice ", 12.34m, 5, new DateTime(2024, 1, 15), "", DBNull.Value);
        table.Rows.Add("2", "  Bob  ", 99m, 7, new DateTime(2024, 6, 30), "   ", DBNull.Value);
        return table;
    }

    private static DataTable WideTable(int rows)
    {
        var table = new DataTable { Locale = CultureInfo.InvariantCulture };
#pragma warning disable IDISP004 // The table owns the columns it adds.
        table.Columns.Add("Id", typeof(string));
        table.Columns.Add("A", typeof(string));
        table.Columns.Add("B", typeof(string));
        table.Columns.Add("C", typeof(string));
        table.Columns.Add("D", typeof(string));
        table.Columns.Add("E", typeof(string));
        table.Columns.Add("F", typeof(string));
        table.Columns.Add("G", typeof(string));
#pragma warning restore IDISP004
        for (var row = 0; row < rows; row++)
        {
            table.Rows.Add(
                $"id-{row.ToInvariant()}",
                $"a-{row.ToInvariant()}",
                $"b-{row.ToInvariant()}",
                $"c-{row.ToInvariant()}",
                $"d-{row.ToInvariant()}",
                $"e-{row.ToInvariant()}",
                $"f-{row.ToInvariant()}",
                $"g-{row.ToInvariant()}");
        }

        return table;
    }
}