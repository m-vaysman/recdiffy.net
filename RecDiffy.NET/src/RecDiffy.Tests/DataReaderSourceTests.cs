using System.Data;

namespace RecDiffy.Tests;

// #116 item 2: DataReaderSource read one row before asking for column names, so a reader that names its columns lost
// its first data row. Names now come from the reader before any row is read.
public class DataReaderSourceTests
{
    [Fact]
    public void DataReaderSource_reads_all_rows_without_consuming_the_first_data_row_as_header()
    {
        using var table = Table(("1", "A"), ("2", "B"), ("3", "C"));

        var rows = Read(table, new TabularOptions());

        Assert.Equal(["1", "2", "3"], rows.Select(row => row.Ref.RowKey), StringComparer.Ordinal);
        Assert.Equal("A", rows[0].Cells["Name"]);
    }

    // HeaderRowIndex still skips that many rows first. Before the fix it skipped them and then lost one more.
    [Fact]
    public void HeaderRowIndex_skips_exactly_that_many_rows_before_the_data()
    {
        using var table = Table(("note", "x"), ("1", "A"), ("2", "B"));

        var rows = Read(table, new TabularOptions(HeaderRowIndex: 1));

        Assert.Equal(["1", "2"], rows.Select(row => row.Ref.RowKey), StringComparer.Ordinal);
    }

    // The older path is kept: a reader that names no columns has its header in its first row.
    [Fact]
    public void A_reader_that_names_no_columns_still_takes_its_header_from_the_first_row()
    {
        var rows = RecordSources.FromDataReader("left", () => new ArrayReader(["", ""], [["Id", "Name"], ["1", "A"], ["2", "B"]]), new TabularOptions())
            .Materialize("left", "Id", CancellationToken.None);

        Assert.Equal(["1", "2"], rows.Select(row => row.Ref.RowKey), StringComparer.Ordinal);
        Assert.Equal("A", rows[0].Cells["Name"]);
    }

    [Fact]
    public void A_reader_with_no_rows_gives_no_records()
    {
        using var table = Table();

        Assert.Empty(Read(table, new TabularOptions()));
    }

    private static IReadOnlyList<RawRecord> Read(DataTable table, TabularOptions options) =>
        RecordSources.FromDataReader("left", table.CreateDataReader, options).Materialize("left", "Id", CancellationToken.None);

    private static DataTable Table(params (string Id, string Name)[] rows)
    {
        var table = new DataTable { Locale = System.Globalization.CultureInfo.InvariantCulture };
#pragma warning disable IDISP004 // The table owns the columns it adds.
        table.Columns.Add("Id", typeof(string));
        table.Columns.Add("Name", typeof(string));
#pragma warning restore IDISP004
        foreach (var (id, name) in rows)
            table.Rows.Add(id, name);
        return table;
    }

    // A reader that returns whatever names it is given, including empty ones, as some tabular readers do.
    private sealed class ArrayReader(string[] names, string[][] rows) : IDataReader
    {
        private int _row = -1;

        public int FieldCount => names.Length;

        public object this[int i] => GetValue(i);

        public object this[string name] => GetValue(GetOrdinal(name));

        public int Depth => 0;

        public bool IsClosed => false;

        public int RecordsAffected => -1;

        public string GetName(int i) => names[i];

        public object GetValue(int i) => rows[_row][i];

        public bool Read() => ++_row < rows.Length;

        public bool IsDBNull(int i) => false;

        public int GetOrdinal(string name) => Array.IndexOf(names, name);

        public void Dispose()
        {
        }

        public void Close()
        {
        }

        public bool NextResult() => false;

        public DataTable GetSchemaTable() => throw new NotSupportedException();

        public bool GetBoolean(int i) => throw new NotSupportedException();

        public byte GetByte(int i) => throw new NotSupportedException();

        public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => throw new NotSupportedException();

        public char GetChar(int i) => throw new NotSupportedException();

        public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => throw new NotSupportedException();

        public IDataReader GetData(int i) => throw new NotSupportedException();

        public string GetDataTypeName(int i) => "string";

        public DateTime GetDateTime(int i) => throw new NotSupportedException();

        public decimal GetDecimal(int i) => throw new NotSupportedException();

        public double GetDouble(int i) => throw new NotSupportedException();

        public Type GetFieldType(int i) => typeof(string);

        public float GetFloat(int i) => throw new NotSupportedException();

        public Guid GetGuid(int i) => throw new NotSupportedException();

        public short GetInt16(int i) => throw new NotSupportedException();

        public int GetInt32(int i) => throw new NotSupportedException();

        public long GetInt64(int i) => throw new NotSupportedException();

        public string GetString(int i) => rows[_row][i];

        public int GetValues(object[] values) => throw new NotSupportedException();
    }
}
