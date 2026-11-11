namespace RecDiffy;

internal sealed class ObjectSource<T>(IEnumerable<T> rows, SourceMap<T> map) : RecordSource
{
    internal override IReadOnlyList<RawRecord> Materialize(string sideName, string rowKey, CancellationToken cancellation) =>
        ProjectAll(sideName, rowKey, cancellation);

    internal override Task<IReadOnlyList<RawRecord>> MaterializeAsync(string sideName, string rowKey, CancellationToken cancellation) =>
        Task.FromResult<IReadOnlyList<RawRecord>>(ProjectAll(sideName, rowKey, cancellation));

    private RawRecord[] ProjectAll(string sideName, string rowKey, CancellationToken cancellation) =>
        [.. rows.Select((row, ordinal) => Project(sideName, rowKey, ordinal, row, cancellation))];

    private RawRecord Project(string sideName, string rowKey, int ordinal, T row, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var cells = map.Project(row);
        var (key, missing) = RecordSources.RowIdentity(cells, rowKey, ordinal);
        return new RawRecord(new SourceRef(sideName, key, ordinal), cells, missing);
    }
}

// Replaced by CancellableAsyncObjectSource (#266). AsyncObjectSource never checks the token itself, so an async enumerable
// that does not take the token is read to the end after the run is cancelled.
#pragma warning disable MA0182
[Obsolete("Replaced by CancellableAsyncObjectSource: checks the cancellation token before each row even when the async enumerable does not take the token. See #266.")]
internal sealed class AsyncObjectSource<T>(IAsyncEnumerable<T> rows, SourceMap<T> map) : RecordSource
#pragma warning restore MA0182
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
