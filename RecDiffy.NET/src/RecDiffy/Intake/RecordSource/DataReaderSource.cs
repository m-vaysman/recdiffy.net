using System.Data;
using System.Globalization;

namespace RecDiffy;

internal sealed class DataReaderSource(Func<IDataReader> open, TabularOptions options) : RecordSource
{
    // A set RowKeyColumn replaces the profile key. Null keeps the profile key.
    internal override IReadOnlyList<RawRecord> Materialize(string sideName, string rowKey, CancellationToken cancellation)
    {
        using var reader = open();
        return Read(reader, sideName, options.RowKeyColumn ?? rowKey, cancellation);
    }

    internal override Task<IReadOnlyList<RawRecord>> MaterializeAsync(string sideName, string rowKey, CancellationToken cancellation) =>
        Task.FromResult(Materialize(sideName, rowKey, cancellation));

    // A file that ends inside the skipped header rows returns no records and no error.
    private IReadOnlyList<RawRecord> Read(IDataReader reader, string sideName, string rowKey, CancellationToken cancellation)
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
            headers = [.. Enumerable.Range(0, reader.FieldCount).Select(index => reader.GetValue(index)?.ToString() ?? "")];
        }

        return ReadBody(reader, sideName, rowKey, headers, cancellation);
    }

    private static IReadOnlyList<RawRecord> ReadBody(IDataReader reader, string sideName, string rowKey, string[] headers, CancellationToken cancellation) =>
        Enumerate(reader, cancellation)
            .Select((record, ordinal) => ToRow(sideName, rowKey, headers, record, ordinal))
            .ToArray();

    private static IEnumerable<object?[]> Enumerate(IDataReader reader, CancellationToken cancellation)
    {
        while (reader.Read())
        {
            cancellation.ThrowIfCancellationRequested();
            yield return [.. Enumerable.Range(0, reader.FieldCount).Select(reader.GetValue)];
        }
    }

    private static RawRecord ToRow(string sideName, string rowKey, string[] headers, object?[] values, int ordinal)
    {
        var cells = headers.Zip(values, (header, value) => (header, Cell: CellOf(value))).ToDictionary(pair => pair.header, pair => pair.Cell, StringComparer.Ordinal);
        var (key, missing) = RecordSources.RowIdentity(cells, rowKey, ordinal);
        return new RawRecord(new SourceRef(sideName, key, ordinal), cells, missing);
    }

    // A double that fits becomes a decimal, and one that does not stays invariant text.
    // A DateTime loses its time, and whitespace becomes null.
    private static object? CellOf(object? value) => value switch
    {
        null or DBNull => null,
        string text when string.IsNullOrWhiteSpace(text) => null,
        string text => text.Trim(),
        DateTime date => DateOnly.FromDateTime(date),
        double number when Rounding.TryFromDouble(number, out var parsed) => parsed,
        float number when Rounding.TryFromDouble(number, out var parsed) => parsed,
        double number => number.ToString(CultureInfo.InvariantCulture),
        float number => number.ToString(CultureInfo.InvariantCulture),
        decimal or int or long => value,
        _ => value.ToString(),
    };
}
