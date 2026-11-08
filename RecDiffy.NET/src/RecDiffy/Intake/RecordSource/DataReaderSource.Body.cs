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
            yield return [.. Enumerable.Range(0, reader.FieldCount).Select(reader.GetValue)];
        }
    }

    private static RawRecord ToRow(string sideName, string rowKey, string[] headers, object?[] values, int ordinal)
    {
        var cells = headers.Zip(values, (header, value) => (header, Cell: CellOf(value))).ToDictionary(pair => pair.header, pair => pair.Cell, StringComparer.Ordinal);
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
