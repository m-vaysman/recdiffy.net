namespace RecDiffy;

internal sealed class CancellableAsyncObjectSource<T>(IAsyncEnumerable<T> rows, SourceMap<T> map) : RecordSource
{
    internal override IReadOnlyList<RawRecord> Materialize(string sideName, string rowKey, CancellationToken cancellation) =>
        MaterializeAsync(sideName, rowKey, cancellation).GetAwaiter().GetResult();

    internal override async Task<IReadOnlyList<RawRecord>> MaterializeAsync(string sideName, string rowKey, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var built = new List<RawRecord>();
        var ordinal = 0;
        await foreach (var row in rows.WithCancellation(cancellation).ConfigureAwait(false))
        {
            cancellation.ThrowIfCancellationRequested();
            var cells = map.Project(row);
            var (key, missing) = RecordSources.RowIdentity(cells, rowKey, ordinal);
            built.Add(new RawRecord(new SourceRef(sideName, key, ordinal), cells, missing));
            ordinal++;
        }

        return built;
    }
}
