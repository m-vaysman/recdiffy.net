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
