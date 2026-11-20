namespace RecDiffy;

/// <summary>Push rows into a batch and reconcile them on demand. Every call returns a new value; nothing is changed.</summary>
public static class RecPush
{
    private const string LeftSide = "left";

    private const string RightSide = "right";

    /// <summary>Starts a batch with rows for each side. A row is its source cells by column, as text.</summary>
    public static RecBatch Push(
        this CompiledProfile profile,
        IEnumerable<IReadOnlyDictionary<string, string?>> left,
        IEnumerable<IReadOnlyDictionary<string, string?>> right,
        ReferenceDataSet? reference = null) =>
        Start(profile, reference).Push(left, right);

    /// <summary>Starts a batch with the rows a previous result left open.</summary>
    public static RecBatch Push(this CompiledProfile profile, IReconciliationResult previous, ReferenceDataSet? reference = null) =>
        Start(profile, reference).Push(previous);

    /// <summary>Adds rows to each side.</summary>
    public static RecBatch Push(
        this RecBatch batch,
        IEnumerable<IReadOnlyDictionary<string, string?>> left,
        IEnumerable<IReadOnlyDictionary<string, string?>> right)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return Add(batch, [.. left.Select(RecRow.Of)], [.. right.Select(RecRow.Of)]);
    }

    /// <summary>Adds the rows a previous result left open: rows of unmatched entries, with the rows their lifecycle chains replaced, and quarantined rows.</summary>
    public static RecBatch Push(this RecBatch batch, IReconciliationResult previous)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(previous);
        return Add(batch, Leftovers.Of(previous, batch.Profile.Document, LeftSide), Leftovers.Of(previous, batch.Profile.Document, RightSide));
    }

    /// <summary>Reconciles every row pushed so far, or returns the last view when nothing was pushed since.</summary>
    public static (RecView View, RecBatch Batch) Snapshot(this RecBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return batch.Last switch
        {
            { } view when view.Version == batch.Version => (view, batch),
            _ => Run(batch),
        };
    }

    private static RecBatch Start(CompiledProfile profile, ReferenceDataSet? reference)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new RecBatch(profile, reference ?? ReferenceDataSet.Empty, [], [], 0);
    }

    private static RecBatch Add(RecBatch batch, IReadOnlyList<RecRow> left, IReadOnlyList<RecRow> right) =>
        batch with { Left = batch.Left.AddRange(left), Right = batch.Right.AddRange(right), Version = batch.Version + 1 };

    private static (RecView View, RecBatch Batch) Run(RecBatch batch)
    {
        var result = Reconciler.Run(batch.Profile, RecRow.Source(LeftSide, batch.Left), RecRow.Source(RightSide, batch.Right), batch.Reference);
        var view = new RecView(result, batch.Version);
        return (view, batch with { Last = view });
    }
}
