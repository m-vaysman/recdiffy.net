using System.Data;
using System.Globalization;

namespace RecDiffy;

internal sealed partial class DataReaderSource(Func<IDataReader> open, TabularOptions options) : RecordSource
{
    // A set RowKeyColumn replaces the profile key. Null keeps the profile key.
    internal override IReadOnlyList<RawRecord> Materialize(string sideName, string rowKey, CancellationToken cancellation) =>
        ReadAll(sideName, rowKey, cancellation);

    internal override Task<IReadOnlyList<RawRecord>> MaterializeAsync(string sideName, string rowKey, CancellationToken cancellation) =>
        Task.FromResult<IReadOnlyList<RawRecord>>(ReadAll(sideName, rowKey, cancellation));

    private RawRecord[] ReadAll(string sideName, string rowKey, CancellationToken cancellation)
    {
        using var reader = open();
        return Read(reader, sideName, options.RowKeyColumn ?? rowKey, cancellation);
    }

    // A file that ends inside the skipped header rows returns no records and no error.
    private RawRecord[] Read(IDataReader reader, string sideName, string rowKey, CancellationToken cancellation)
    {
        foreach (var _ in Enumerable.Range(0, options.HeaderRowIndex))
        {
            if (reader.Read() is false)
                return [];
        }

        var headers = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        // Named columns are already the header. Consuming a row here drops the first data record.
        if (headers[0] is "")
        {
            if (reader.Read() is false)
                return [];
            headers = [.. Enumerable.Range(0, reader.FieldCount).Select(index => InvariantText.Of(reader.GetValue(index)) ?? "")];
        }

        return ReadBody(reader, sideName, rowKey, headers, cancellation);
    }
}
