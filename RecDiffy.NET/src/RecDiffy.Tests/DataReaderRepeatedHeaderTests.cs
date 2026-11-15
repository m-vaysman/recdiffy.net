using System.Data;

namespace RecDiffy.Tests;

// #403 item 2: a reader that gives two columns the same name is an error, not a row whose second column silently
// replaces the first. The row is filled with Dictionary.Add for that reason; these tests fail if it becomes an indexer.
public class DataReaderRepeatedHeaderTests
{
    [Fact]
    public void Two_columns_with_the_same_name_are_rejected()
    {
        var source = RecordSources.FromDataReader("left", () => new NamedReader(["Id", "Price", "Price"], [["T1", "10", "11"]]), new TabularOptions());

        var error = Assert.Throws<ArgumentException>(() => source.Materialize("left", "Id", CancellationToken.None));
        Assert.Contains("Price", error.Message, StringComparison.Ordinal);
    }

    // With no column names, the first row is the header; the same rule holds for a header read that way.
    [Fact]
    public void A_header_row_that_repeats_a_name_is_rejected()
    {
        var source = RecordSources.FromDataReader("left", () => new NamedReader(["", "", ""], [["Id", "Price", "Price"], ["T1", "10", "11"]]), new TabularOptions());

        var error = Assert.Throws<ArgumentException>(() => source.Materialize("left", "Id", CancellationToken.None));
        Assert.Contains("Price", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Distinct_names_are_read_in_reader_order()
    {
        var source = RecordSources.FromDataReader("left", () => new NamedReader(["Id", "Price", "Fee"], [["T1", "10", "1"], ["T2", "20", "2"]]), new TabularOptions());

        var rows = source.Materialize("left", "Id", CancellationToken.None);

        Assert.Equal(["T1", "T2"], rows.Select(row => row.Ref.RowKey), StringComparer.Ordinal);
        Assert.Equal("20", rows[1].Cells["Price"]);
        Assert.Equal("2", rows[1].Cells["Fee"]);
    }

    // A reader that returns whatever names it is given, repeated ones included, which a DataTable cannot hold.
    private sealed class NamedReader(string[] names, string[][] rows) : IDataReader
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
