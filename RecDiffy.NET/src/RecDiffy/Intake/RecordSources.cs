using System.Data;
using System.Globalization;

namespace RecDiffy;

public sealed record TabularOptions(int HeaderRowIndex = 0, string? RowKeyColumn = null);

public sealed class SourceMap<T>
{
    private readonly IReadOnlyList<Action<T, Dictionary<string, object?>>> _binders;

    public SourceMap() => _binders = [];

    private SourceMap(IReadOnlyList<Action<T, Dictionary<string, object?>>> binders) => _binders = binders;

    public SourceMap<T> Decimal(string column, Func<T, decimal?> read)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(read);
        return new SourceMap<T>([.. _binders, (row, cells) => cells[column] = read(row)]);
    }

    public SourceMap<T> Text(string column, Func<T, string?> read)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(read);
        return new SourceMap<T>([.. _binders, (row, cells) => cells[column] = read(row)?.Trim()]);
    }

    public SourceMap<T> Date(string column, Func<T, DateOnly?> read)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(read);
        return new SourceMap<T>([.. _binders, (row, cells) => cells[column] = read(row)]);
    }

    internal IReadOnlyDictionary<string, object?> Project(T row) =>
        _binders.Aggregate(
            new Dictionary<string, object?>(StringComparer.Ordinal),
            (cells, bind) =>
            {
                bind(row, cells);
                return cells;
            });
}

public abstract class RecordSource
{
    internal abstract IReadOnlyList<RawRecord> Materialize(string sideName, string rowKey, CancellationToken cancellation);

    internal abstract Task<IReadOnlyList<RawRecord>> MaterializeAsync(string sideName, string rowKey, CancellationToken cancellation);
}

public static class RecordSources
{
    public static RecordSource FromObjects<T>(string name, IEnumerable<T> rows, SourceMap<T> map)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);
        return new ObjectSource<T>(rows, map);
    }

    public static RecordSource FromObjects<T>(string name, IAsyncEnumerable<T> rows, SourceMap<T> map)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);
        return new AsyncObjectSource<T>(rows, map);
    }

    public static RecordSource FromDataReader(string name, Func<IDataReader> open, TabularOptions options)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(options);
        return new DataReaderSource(open, options);
    }

    internal static (string Key, bool Missing) RowIdentity(IReadOnlyDictionary<string, object?> cells, string rowKey, int ordinal)
    {
        if (rowKey.Length == 0)
            return (ordinal.ToString(CultureInfo.InvariantCulture), false);
        var text = cells.TryGetValue(rowKey, out var value) ? value?.ToString()?.Trim() : null;
        return string.IsNullOrEmpty(text) ? ("", true) : (text, false);
    }
}

internal sealed record RawRecord(SourceRef Ref, IReadOnlyDictionary<string, object?> Cells, bool MissingKey = false);

internal sealed class ObjectSource<T>(IEnumerable<T> rows, SourceMap<T> map) : RecordSource
{
    internal override IReadOnlyList<RawRecord> Materialize(string sideName, string rowKey, CancellationToken cancellation) =>
        [.. rows.Select((row, ordinal) => Project(sideName, rowKey, ordinal, row, cancellation))];

    internal override Task<IReadOnlyList<RawRecord>> MaterializeAsync(string sideName, string rowKey, CancellationToken cancellation) =>
        Task.FromResult(Materialize(sideName, rowKey, cancellation));

    private RawRecord Project(string sideName, string rowKey, int ordinal, T row, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var cells = map.Project(row);
        var (key, missing) = RecordSources.RowIdentity(cells, rowKey, ordinal);
        return new RawRecord(new SourceRef(sideName, key, ordinal), cells, missing);
    }
}

internal sealed class AsyncObjectSource<T>(IAsyncEnumerable<T> rows, SourceMap<T> map) : RecordSource
{
    internal override IReadOnlyList<RawRecord> Materialize(string sideName, string rowKey, CancellationToken cancellation) =>
        MaterializeAsync(sideName, rowKey, cancellation).GetAwaiter().GetResult();

    internal override async Task<IReadOnlyList<RawRecord>> MaterializeAsync(string sideName, string rowKey, CancellationToken cancellation)
    {
        var built = new List<RawRecord>();
        var ordinal = 0;
        await foreach (var row in rows.WithCancellation(cancellation).ConfigureAwait(false))
        {
            var cells = map.Project(row);
            var (key, missing) = RecordSources.RowIdentity(cells, rowKey, ordinal);
            built.Add(new RawRecord(new SourceRef(sideName, key, ordinal), cells, missing));
            ordinal++;
        }

        return built;
    }
}

internal sealed class DataReaderSource(Func<IDataReader> open, TabularOptions options) : RecordSource
{
    internal override IReadOnlyList<RawRecord> Materialize(string sideName, string rowKey, CancellationToken cancellation)
    {
        using var reader = open();
        return Read(reader, sideName, options.RowKeyColumn ?? rowKey, cancellation);
    }

    internal override Task<IReadOnlyList<RawRecord>> MaterializeAsync(string sideName, string rowKey, CancellationToken cancellation) =>
        Task.FromResult(Materialize(sideName, rowKey, cancellation));

    private IReadOnlyList<RawRecord> Read(IDataReader reader, string sideName, string rowKey, CancellationToken cancellation)
    {
        foreach (var _ in Enumerable.Range(0, options.HeaderRowIndex))
        {
            if (reader.Read() is false)
                return [];
        }

        // Column names come from the reader, before any row is read, so every row it returns is data.
        var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        if (names is not ["", ..])
            return ReadBody(reader, sideName, rowKey, names, cancellation);

        // A reader that names no columns keeps its header in its first row. This path predates the fix for #116 and is kept as it was.
        if (reader.Read() is false)
            return [];

        var headers = Enumerable.Range(0, reader.FieldCount).Select(index => reader.GetValue(index)?.ToString() ?? "").ToArray();
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

    private static object? CellOf(object? value) => value switch
    {
        null or DBNull => null,
        string text when string.IsNullOrWhiteSpace(text) => null,
        string text => text.Trim(),
        DateTime date => DateOnly.FromDateTime(date),
        double number => Rounding.FromDouble(number),
        float number => Rounding.FromDouble(number),
        decimal or int or long => value,
        _ => value.ToString(),
    };
}
