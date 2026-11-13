namespace RecDiffy.Capacity;

internal sealed class MemorySampler : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(25);

    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private long _peakHeapBytes;
    private long _peakWorkingSetBytes;

    public MemorySampler()
    {
        Record();
        _loop = Task.Run(LoopAsync);
    }

    public long PeakHeapBytes => Volatile.Read(ref _peakHeapBytes);

    public long PeakWorkingSetBytes => Volatile.Read(ref _peakWorkingSetBytes);

    public void SampleNow() => Record();

    public void Dispose()
    {
        _stop.Cancel();
        _loop.GetAwaiter().GetResult();
        _stop.Dispose();
    }

    private async Task LoopAsync()
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
                Record();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Record()
    {
        Update(ref _peakHeapBytes, GC.GetGCMemoryInfo().HeapSizeBytes);
        Update(ref _peakWorkingSetBytes, Environment.WorkingSet);
    }

    private static void Update(ref long target, long value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var prior = Interlocked.CompareExchange(ref target, value, current);
            if (prior == current)
                return;
            current = prior;
        }
    }
}
