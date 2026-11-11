namespace RecDiffy.Tests;

public class CancellableAsyncObjectSourceTests
{
    [Fact]
    public async Task Right_async_source_without_enumerator_cancellation_stops_once_the_run_token_is_cancelled()
    {
        using var source = new CancellationTokenSource();
        var pulled = new MidStream.Pulled();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Reconciler.RunAsync(
                MidStream.Profile(),
                MidStream.Source("left", MidStream.Rows("L", 1)),
                MidStream.Source("right", MidStream.CancelsThenKeepsYieldingAsync(source, pulled)),
                ReferenceDataSet.Empty,
                cancellationToken: source.Token));

        Assert.InRange(pulled.Count, 2, 3);
    }

    [Fact]
    public void Sync_run_on_an_async_source_without_enumerator_cancellation_stops_once_the_run_token_is_cancelled()
    {
        using var source = new CancellationTokenSource();
        var pulled = new MidStream.Pulled();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            Reconciler.Run(
                MidStream.Profile(),
                MidStream.Source("left", MidStream.CancelsThenKeepsYieldingAsync(source, pulled)),
                MidStream.Source("right", MidStream.Rows("R", 1)),
                ReferenceDataSet.Empty,
                cancellationToken: source.Token));

        Assert.InRange(pulled.Count, 2, 3);
    }

    [Fact]
    public async Task Pre_cancelled_token_does_not_pull_any_rows_from_an_async_source()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var pulled = new MidStream.Pulled();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Reconciler.RunAsync(
                MidStream.Profile(),
                MidStream.Source("left", MidStream.CancelsThenKeepsYieldingAsync(source, pulled)),
                MidStream.Source("right", MidStream.Rows("R", 1)),
                ReferenceDataSet.Empty,
                cancellationToken: source.Token));

        Assert.Equal(0, pulled.Count);
    }
}
