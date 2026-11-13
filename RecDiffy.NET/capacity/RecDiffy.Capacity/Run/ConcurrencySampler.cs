using System.Diagnostics;

namespace RecDiffy.Capacity;

// Samples the batch between counters polls: managed heap, working set, thread-pool size and queue, and OS threads.
internal sealed class ConcurrencySampler : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50);

    private readonly Process _process;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private long _peakHeapBytes;
    private long _peakWorkingSetBytes;
    private long _peakThreadPoolThreads;
    private long _peakPendingWorkItems;
    private long _peakThreads;

    public ConcurrencySampler(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        _process = process;
        Record();
        _loop = Task.Run(LoopAsync);
    }

    public long PeakHeapBytes => Volatile.Read(ref _peakHeapBytes);

    public long PeakWorkingSetBytes => Volatile.Read(ref _peakWorkingSetBytes);

    public int PeakThreadPoolThreads => (int)Volatile.Read(ref _peakThreadPoolThreads);

    public int PeakPendingWorkItems => (int)Volatile.Read(ref _peakPendingWorkItems);

    public int PeakThreads => (int)Volatile.Read(ref _peakThreads);

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
        Update(ref _peakThreadPoolThreads, ThreadPool.ThreadCount);
        Update(ref _peakPendingWorkItems, ThreadPool.PendingWorkItemCount);
        Update(ref _peakThreads, Threads());
    }

    private int Threads()
    {
        try
        {
            _process.Refresh();
            return _process.Threads.Count;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
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
