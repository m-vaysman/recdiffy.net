namespace RecDiffy.Tests;

public class AsyncSourceCancelledReadTests
{
    // The same rows as AsyncSourceMidStreamTests' sync case, as an async enumerable that does not take the token.
    // The run is cancelled while it reads row 2, so it should not ask for row 4.
    [Fact(Skip = "🔴 #266: AsyncObjectSource never checks the token itself, so it reads all 1,000 rows after the run is cancelled.")]
    public async Task An_async_source_is_not_read_further_once_the_run_is_cancelled()
    {
        using var source = new CancellationTokenSource();
        var pulled = new MidStream.Pulled();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Reconciler.RunAsync(
                MidStream.Profile(),
                MidStream.Source("left", MidStream.CancelsThenKeepsYieldingAsync(source, pulled)),
                MidStream.Source("right", MidStream.Rows("R", 1)),
                ReferenceDataSet.Empty,
                cancellationToken: source.Token));

        Assert.InRange(pulled.Count, 2, 3);
    }
}
