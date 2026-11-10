using System.Globalization;
using System.Runtime.CompilerServices;

namespace RecDiffy.Tests;

// #116 "untested but holds" item 3: an async source that fails or is cancelled after it has yielded rows.
public class AsyncSourceMidStreamTests
{
    [Theory]
    [InlineData("left")]
    [InlineData("right")]
    public async Task An_async_source_that_throws_partway_fails_the_run_with_its_own_exception(string failing)
    {
        var failure = new InvalidOperationException("The feed dropped after one row.");
        var left = failing is "left" ? MidStream.ThrowsAfterFirst("L", failure) : MidStream.Rows("L", 3);
        var right = failing is "right" ? MidStream.ThrowsAfterFirst("R", failure) : MidStream.Rows("R", 3);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Reconciler.RunAsync(MidStream.Profile(), MidStream.Source("left", left), MidStream.Source("right", right), ReferenceDataSet.Empty));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task An_async_source_sees_the_run_token_when_the_run_is_cancelled_partway()
    {
        using var source = new CancellationTokenSource();
        var seen = new MidStream.Seen();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Reconciler.RunAsync(
                MidStream.Profile(),
                MidStream.Source("left", MidStream.CancelsAfterFirst(source, seen)),
                MidStream.Source("right", MidStream.Rows("R", 1)),
                ReferenceDataSet.Empty,
                cancellationToken: source.Token));

        Assert.True(seen.Cancelled);
    }

    // The sync object source checks the token before each row, so a source that ignores the token is not drained.
    [Fact]
    public void A_sync_source_is_not_read_further_once_the_run_is_cancelled()
    {
        using var source = new CancellationTokenSource();
        var pulled = new MidStream.Pulled();

        Assert.Throws<OperationCanceledException>(() =>
            Reconciler.Run(
                MidStream.Profile(),
                RecordSources.FromObjects("left", MidStream.CancelsThenKeepsYielding(source, pulled), MidStream.Map),
                RecordSources.FromObjects("right", [("R1", "1")], MidStream.Map),
                ReferenceDataSet.Empty,
                cancellationToken: source.Token));

        Assert.InRange(pulled.Count, 2, 3);
    }
}

// A one-key profile and row sources that fail, or cancel the run, after they have yielded rows.
internal static class MidStream
{
    public static readonly SourceMap<(string Id, string Qty)> Map = new SourceMap<(string Id, string Qty)>()
        .Text("Id", row => row.Id)
        .Text("Qty", row => row.Qty);

    public static RecordSource Source(string side, IAsyncEnumerable<(string Id, string Qty)> rows) =>
        RecordSources.FromObjects(side, rows, Map);

    public static CompiledProfile Profile() =>
        CultureProfile.Compile(
            """
            {
              "formatVersion": 1,
              "profileId": "async-mid-stream",
              "version": "1.0.0",
              "culture": "en-US",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Id", "type": "Identifier", "role": "Key" },
                { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
              ],
              "normalization": {
                "left": {
                  "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "Id" } ],
                  "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ]
                },
                "right": {
                  "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "Id" } ],
                  "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ]
                }
              },
              "paths": [ { "id": "all", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": [ "Id" ] } ] } ]
            }
            """,
            CultureInfo.InvariantCulture);

    public static async IAsyncEnumerable<(string Id, string Qty)> Rows(string prefix, int count)
    {
        foreach (var index in Enumerable.Range(1, count))
        {
            await Task.Yield();
            yield return ($"{prefix}{index}", "1");
        }
    }

    public static async IAsyncEnumerable<(string Id, string Qty)> ThrowsAfterFirst(string prefix, Exception failure)
    {
        await Task.Yield();
        yield return ($"{prefix}1", "1");
        await Task.Yield();
        throw failure;
    }

    // A well-behaved source: it takes the token the run hands it and stops when it is cancelled.
    public static async IAsyncEnumerable<(string Id, string Qty)> CancelsAfterFirst(
        CancellationTokenSource source,
        Seen seen,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        await Task.Yield();
        yield return ("L1", "1");
        source.Cancel();
        seen.Cancelled = token.IsCancellationRequested;
        token.ThrowIfCancellationRequested();
        yield return ("L2", "1");
    }

    // Cancels the run while it hands out row 2, then keeps yielding without looking at any token.
    public static IEnumerable<(string Id, string Qty)> CancelsThenKeepsYielding(CancellationTokenSource source, Pulled pulled)
    {
        foreach (var index in Enumerable.Range(1, 1_000))
        {
            pulled.Count++;
            if (index is 2)
                source.Cancel();
            yield return ($"L{index}", "1");
        }
    }

    public static async IAsyncEnumerable<(string Id, string Qty)> CancelsThenKeepsYieldingAsync(CancellationTokenSource source, Pulled pulled)
    {
        foreach (var row in CancelsThenKeepsYielding(source, pulled))
        {
            await Task.Yield();
            yield return row;
        }
    }

    public sealed class Seen
    {
        public bool Cancelled { get; set; }
    }

    public sealed class Pulled
    {
        public int Count { get; set; }
    }
}
