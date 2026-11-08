using System.Data;
using System.Globalization;

namespace RecDiffy;

public abstract class RecordSource
{
    internal abstract IReadOnlyList<RawRecord> Materialize(string sideName, string rowKey, CancellationToken cancellation);

    internal abstract Task<IReadOnlyList<RawRecord>> MaterializeAsync(string sideName, string rowKey, CancellationToken cancellation);
}

public static class RecordSources
{
    // The name is checked and then dropped. Materialize receives the side name from the reconciler.
    public static RecordSource FromObjects<T>(string name, IEnumerable<T> rows, SourceMap<T> map)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);
        return new ObjectSource<T>(rows, map);
    }

    // The name is checked and then dropped. Materialize receives the side name from the reconciler.
    public static RecordSource FromObjects<T>(string name, IAsyncEnumerable<T> rows, SourceMap<T> map)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);
        return new AsyncObjectSource<T>(rows, map);
    }

    // The name is checked and then dropped. Materialize receives the side name from the reconciler.
    public static RecordSource FromDataReader(string name, Func<IDataReader> open, TabularOptions options)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(options);
        return new DataReaderSource(open, options);
    }

    // An empty key name means the ordinal is the identity.
    // A blank cell in a named key is missing, not an empty identity.
    internal static (string Key, bool Missing) RowIdentity(IReadOnlyDictionary<string, object?> cells, string rowKey, int ordinal)
    {
        if (rowKey.Length == 0)
            return (ordinal.ToString(CultureInfo.InvariantCulture), false);
        var text = cells.TryGetValue(rowKey, out var value) ? InvariantText.Of(value)?.Trim() : null;
        return string.IsNullOrEmpty(text) ? ("", true) : (text, false);
    }
}

internal sealed record RawRecord(SourceRef Ref, IReadOnlyDictionary<string, object?> Cells, bool MissingKey = false);
