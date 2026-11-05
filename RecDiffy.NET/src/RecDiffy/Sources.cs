namespace RecDiffy.Engine;

internal sealed class ObjectSource<T> : RecordSource
{
    private readonly string _name;
    private readonly IEnumerable<T> _rows;
    private readonly SourceMap<T> _map;
    public ObjectSource(string name, IEnumerable<T> rows, SourceMap<T> map)
    {
        _name = name;
        _rows = rows;
        _map = map;
    }

    internal override IReadOnlyList<RawRow> Materialize(string sideName, string rowKey, CancellationToken cancellation) =>
        Read(_rows, sideName, rowKey, cancellation);

    internal override Task<IReadOnlyList<RawRow>> MaterializeAsync(string sideName, string rowKey, CancellationToken cancellation) =>
        Task.FromResult(Materialize(sideName, rowKey, cancellation));

    private IReadOnlyList<RawRow> Read(IEnumerable<T> rows, string sideName, string rowKey, CancellationToken cancellation)
    {
        var list = new List<RawRow>();
        var ordinal = 0;
        foreach (var row in rows)
        {
            cancellation.ThrowIfCancellationRequested();
            var draft = new RawRow { Ref = new SourceRef(sideName, "", ordinal) };
            foreach (var bind in _map.Binders)
                bind(row, draft);
            var key = draft.TryGet(rowKey, out var cell)
                ? cell.Text ?? cell.Number?.ToString(CultureInfo.InvariantCulture) ?? ordinal.ToString(CultureInfo.InvariantCulture)
                : ordinal.ToString(CultureInfo.InvariantCulture);
            var raw = new RawRow { Ref = new SourceRef(sideName, key, ordinal) };
            foreach (var pair in draft.Cells)
                raw.Set(pair.Key, pair.Value);
            list.Add(raw);
            ordinal++;
        }
        return list;
    }
}

internal sealed class AsyncObjectSource<T> : RecordSource
{
    private readonly string _name;
    private readonly IAsyncEnumerable<T> _rows;
    private readonly SourceMap<T> _map;
    public AsyncObjectSource(string name, IAsyncEnumerable<T> rows, SourceMap<T> map)
    {
        _name = name;
        _rows = rows;
        _map = map;
    }

    internal override IReadOnlyList<RawRow> Materialize(string sideName, string rowKey, CancellationToken cancellation) =>
        MaterializeAsync(sideName, rowKey, cancellation).GetAwaiter().GetResult();

    internal override async Task<IReadOnlyList<RawRow>> MaterializeAsync(string sideName, string rowKey, CancellationToken cancellation)
    {
        var buffer = new List<T>();
        await foreach (var row in _rows.WithCancellation(cancellation).ConfigureAwait(false))
            buffer.Add(row);
        return new ObjectSource<T>(_name, buffer, _map).Materialize(sideName, rowKey, cancellation);
    }
}

internal sealed class DataReaderSource : RecordSource
{
    private readonly string _name;
    private readonly Func<IDataReader> _open;
    private readonly TabularOptions _options;
    public DataReaderSource(string name, Func<IDataReader> open, TabularOptions options)
    {
        _name = name;
        _open = open;
        _options = options;
    }

    internal override IReadOnlyList<RawRow> Materialize(string sideName, string rowKey, CancellationToken cancellation)
    {
        using var reader = _open();
        var headers = new List<string>();
        var rows = new List<Dictionary<string, object?>>();
        var index = 0;
        while (reader.Read())
        {
            cancellation.ThrowIfCancellationRequested();
            if (index++ < _options.HeaderRowIndex)
                continue;
            if (headers.Count == 0)
            {
                for (var i = 0; i < reader.FieldCount; i++)
                    headers.Add(Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? $"c{i}");
                continue;
            }
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < headers.Count && i < reader.FieldCount; i++)
                values[headers[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(values);
        }
        var key = _options.RowKeyColumn ?? rowKey;
        return rows.Select((values, ordinal) => ToRaw(sideName, key, ordinal, values)).ToArray();
    }

    internal override Task<IReadOnlyList<RawRow>> MaterializeAsync(string sideName, string rowKey, CancellationToken cancellation) =>
        Task.FromResult(Materialize(sideName, rowKey, cancellation));

    private static RawRow ToRaw(string side, string keyColumn, int ordinal, Dictionary<string, object?> values)
    {
        var key = values.TryGetValue(keyColumn, out var keyValue) ? Convert.ToString(keyValue, CultureInfo.InvariantCulture) ?? ordinal.ToString(CultureInfo.InvariantCulture) : ordinal.ToString(CultureInfo.InvariantCulture);
        var raw = new RawRow { Ref = new SourceRef(side, key, ordinal) };
        foreach (var pair in values)
            raw.Set(pair.Key, Box(pair.Value));
        return raw;
    }

    internal static RawValue Box(object? value) => value switch
    {
        null or DBNull => RawValue.Null(),
        DateOnly date => new RawValue(false, null, null, date),
        DateTime time => new RawValue(false, null, null, DateOnly.FromDateTime(time)),
        decimal number => new RawValue(false, null, number, null),
        double number => new RawValue(false, null, Rounding.FromDouble(number), null),
        float number => new RawValue(false, null, Rounding.FromDouble(number), null),
        int number => new RawValue(false, null, number, null),
        long number => new RawValue(false, null, number, null),
        string text when string.IsNullOrWhiteSpace(text) => RawValue.Null(),
        string text => new RawValue(false, text.Trim(), null, null),
        _ => new RawValue(false, Convert.ToString(value, CultureInfo.InvariantCulture), null, null),
    };
}
