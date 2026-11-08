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

    // Text is trimmed. Decimal and date binders store the value the reader returned.
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

    // Binders run in add order. The last binder on a column overwrites the cell.
    internal IReadOnlyDictionary<string, object?> Project(T row) =>
        _binders.Aggregate(
            new Dictionary<string, object?>(StringComparer.Ordinal),
            (cells, bind) =>
            {
                bind(row, cells);
                return cells;
            });
}
