using System.Data;
using System.Globalization;

namespace RecDiffy;

internal sealed partial class DataReaderSource
{
    private static RawRecord[] ReadBody(IDataReader reader, string sideName, string rowKey, string[] headers, CancellationToken cancellation) =>
        Enumerate(reader, cancellation)
            .Select((record, ordinal) => ToRow(sideName, rowKey, headers, record, ordinal))
            .ToArray();

    private static IEnumerable<object?[]> Enumerate(IDataReader reader, CancellationToken cancellation)
    {
        while (reader.Read())
        {
            cancellation.ThrowIfCancellationRequested();
            // A sized array avoids the range iterator and Select delegate per cell (#403 item 2, a measured loop).
            var row = new object?[reader.FieldCount];
            for (var index = 0; index < row.Length; index++)
                row[index] = reader.GetValue(index);
            yield return row;
        }
    }

    private static RawRecord ToRow(string sideName, string rowKey, string[] headers, object?[] values, int ordinal)
    {
        // A sized dictionary and loop avoid the Zip iterator and the tuple per cell (#403 item 2, a measured loop).
        var cells = new Dictionary<string, object?>(headers.Length, StringComparer.Ordinal);
        var count = Math.Min(headers.Length, values.Length);
        for (var index = 0; index < count; index++)
            cells.Add(headers[index], CellOf(values[index]));
        var (key, missing) = RecordSources.RowIdentity(cells, rowKey, ordinal);
        return new RawRecord(new SourceRef(sideName, key, ordinal), cells, missing);
    }

    // Floating point passes through unconverted, so decimal.parse reports it (#158). Every integer type becomes a decimal.
    // A DateTime loses its time, whitespace becomes null, and anything else becomes invariant text, whatever the server's culture.
    private static object? CellOf(object? value) => value switch
    {
        null or DBNull => null,
        string text when string.IsNullOrWhiteSpace(text) => null,
        string text => text.Trim(),
        DateTime date => DateOnly.FromDateTime(date),
        double or float or Half => value,
        decimal => value,
        int number => (decimal)number,
        long number => (decimal)number,
        short number => (decimal)number,
        byte number => (decimal)number,
        sbyte number => (decimal)number,
        ushort number => (decimal)number,
        uint number => (decimal)number,
        ulong number => (decimal)number,
        _ => InvariantText.Of(value),
    };
}
