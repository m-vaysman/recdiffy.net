using System.Diagnostics;
using RecDiffy.Results;

namespace RecDiffy.Capacity;

internal enum FanOutStyle
{
    Threads,
    Tasks,
    Parallel,
}

internal enum ProfileSharing
{
    Shared,
    Separate,
}

internal sealed record ConcurrencyOutcome(
    IReadOnlyList<ReportConcurrencyCase> Cases,
    bool AllMatchBaseline,
    string BaselineFingerprint);

internal sealed record BatchResult(
    double WallSeconds,
    IReadOnlyList<double> LatencySeconds,
    IReadOnlyList<string> Fingerprints,
    long AllocatedBytes,
    double GcPauseSeconds,
    int Gen0,
    int Gen1,
    int Gen2,
    double CpuSeconds,
    long LockContentionCount,
    long PeakHeapBytes,
    long PeakWorkingSetBytes,
    int PeakThreadPoolThreads,
    int PeakPendingWorkItems,
    int PeakThreads)
{
    public bool Matches(string baseline) => Fingerprints.All(fingerprint => string.Equals(fingerprint, baseline, StringComparison.Ordinal));
}

// #377 P3: N runs at once, in three fan-out styles, over one shared profile and over N separate profiles, with every
// run's result compared field by field with a single-threaded baseline.
internal static class ConcurrencySweep
{
    private static readonly FanOutStyle[] Styles = [FanOutStyle.Threads, FanOutStyle.Tasks, FanOutStyle.Parallel];
    private static readonly ProfileSharing[] Sharings = [ProfileSharing.Shared, ProfileSharing.Separate];

    public static ConcurrencyOutcome Run(
        CompiledProfile profile,
        Func<CompiledProfile> compile,
        CapacityOptions options,
        Process process)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(compile);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(process);

        var workload = new Workload(options.ConcurrencyRows, options.BreakRate, options.UnmatchedRate, options.Seed);
        var baseline = One(profile, workload);
        foreach (var style in Styles)
        {
            foreach (var sharing in Sharings)
            {
                IReadOnlyList<CompiledProfile> warm = sharing is ProfileSharing.Separate ? [compile()] : [profile];
                _ = Batch(style, 1, warm, workload, process);
            }
        }

        var levels = EffectiveLevels(options.Levels);
        var cases = new List<ReportConcurrencyCase>();
        var matched = true;

        foreach (var style in Styles)
        {
            foreach (var sharing in Sharings)
            {
                foreach (var level in levels)
                {
                    var (batch, matches) = Measure(style, sharing, level, profile, compile, workload, options, process, baseline.Fingerprint);
                    matched &= matches;
                    cases.Add(Case(style, sharing, level, options, batch, matches));
                }
            }
        }

        var baselines = cases.Where(item => item.Level is 1).ToDictionary(item => (item.Style, item.Sharing));
        var fastest = cases.Count is 0 ? 0 : cases.Max(item => item.RowsPerSecond);
        return new ConcurrencyOutcome(
            [.. cases.Select(item => item with
            {
                ScalingEfficiencyPercent = baselines.TryGetValue((item.Style, item.Sharing), out var reference) && reference.RowsPerSecond > 0
                    ? item.RowsPerSecond / (item.Level * reference.RowsPerSecond) * 100
                    : 0,
                ThroughputBarPercent = fastest > 0 ? item.RowsPerSecond / fastest * 100 : 0,
            })],
            matched,
            baseline.Fingerprint);
    }

    private static (BatchResult Batch, bool Matches) Measure(
        FanOutStyle style,
        ProfileSharing sharing,
        int level,
        CompiledProfile profile,
        Func<CompiledProfile> compile,
        Workload workload,
        CapacityOptions options,
        Process process,
        string baseline)
    {
        IReadOnlyList<CompiledProfile> profiles = sharing is ProfileSharing.Separate
            ? [.. Enumerable.Range(0, level).Select(_ => compile())]
            : [profile];
        var batches = new List<BatchResult>(options.Repeats);
        var matches = true;
        for (var repeat = 0; repeat < options.Repeats; repeat++)
        {
            var batch = Batch(style, level, profiles, workload, process);
            matches &= batch.Matches(baseline);
            batches.Add(batch);
        }

        return (batches.OrderBy(batch => batch.WallSeconds).ElementAt(batches.Count / 2), matches);
    }

    private static BatchResult Batch(FanOutStyle style, int level, IReadOnlyList<CompiledProfile> profiles, Workload workload, Process process)
    {
        var latencies = new double[level];
        var fingerprints = new string[level];
        var allocated = GC.GetTotalAllocatedBytes();
        var pause = GC.GetTotalPauseDuration().TotalSeconds;
        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);
        var cpu = process.TotalProcessorTime;
        var contention = Monitor.LockContentionCount;
        double wall;
        long peakHeap;
        long peakWorkingSet;
        int peakThreadPoolThreads;
        int peakPendingWorkItems;
        int peakThreads;
        using (var sampler = new ConcurrencySampler(process))
        {
            var watch = Stopwatch.StartNew();
            Switch(style, level, profiles, workload, latencies, fingerprints);
            watch.Stop();
            sampler.SampleNow();
            wall = watch.Elapsed.TotalSeconds;
            peakHeap = sampler.PeakHeapBytes;
            peakWorkingSet = sampler.PeakWorkingSetBytes;
            peakThreadPoolThreads = sampler.PeakThreadPoolThreads;
            peakPendingWorkItems = sampler.PeakPendingWorkItems;
            peakThreads = sampler.PeakThreads;
        }

        // Process and the counters are read after the sampler stops: Process is not documented as thread-safe.
        return new BatchResult(
            wall,
            latencies,
            fingerprints,
            GC.GetTotalAllocatedBytes() - allocated,
            GC.GetTotalPauseDuration().TotalSeconds - pause,
            GC.CollectionCount(0) - gen0,
            GC.CollectionCount(1) - gen1,
            GC.CollectionCount(2) - gen2,
            (process.TotalProcessorTime - cpu).TotalSeconds,
            Monitor.LockContentionCount - contention,
            peakHeap,
            peakWorkingSet,
            peakThreadPoolThreads,
            peakPendingWorkItems,
            peakThreads);
    }

    private static void Switch(
        FanOutStyle style,
        int level,
        IReadOnlyList<CompiledProfile> profiles,
        Workload workload,
        double[] latencies,
        string[] fingerprints)
    {
        switch (style)
        {
            case FanOutStyle.Threads:
                Threads(level, profiles, workload, latencies, fingerprints);
                break;
            case FanOutStyle.Tasks:
                Tasks(level, profiles, workload, latencies, fingerprints);
                break;
            default:
                ParallelRuns(level, profiles, workload, latencies, fingerprints);
                break;
        }
    }

    private static void Threads(int level, IReadOnlyList<CompiledProfile> profiles, Workload workload, double[] latencies, string[] fingerprints)
    {
        using var gate = new Barrier(level);
        var threads = new Thread[level];
        for (var index = 0; index < level; index++)
        {
            var slot = index;
            threads[slot] = new Thread(() =>
            {
                gate.SignalAndWait();
                (latencies[slot], fingerprints[slot]) = One(profiles[slot % profiles.Count], workload);
            }) { IsBackground = true };
            threads[slot].Start();
        }

        foreach (var thread in threads)
            thread.Join();
    }

    private static void Tasks(int level, IReadOnlyList<CompiledProfile> profiles, Workload workload, double[] latencies, string[] fingerprints)
    {
        using var gate = new ManualResetEventSlim(false);
        var tasks = new Task[level];
        for (var index = 0; index < level; index++)
        {
            var slot = index;
            tasks[slot] = Task.Run(() =>
            {
                gate.Wait();
                (latencies[slot], fingerprints[slot]) = One(profiles[slot % profiles.Count], workload);
            });
        }

        gate.Set();
        Task.WaitAll(tasks);
    }

    private static void ParallelRuns(int level, IReadOnlyList<CompiledProfile> profiles, Workload workload, double[] latencies, string[] fingerprints) =>
        System.Threading.Tasks.Parallel.For(
            0,
            level,
            slot => (latencies[slot], fingerprints[slot]) = One(profiles[slot % profiles.Count], workload));

    private static (double Seconds, string Fingerprint) One(CompiledProfile profile, Workload workload)
    {
        var watch = Stopwatch.StartNew();
        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", workload.Left(), CapacityRunner.RowMap),
            RecordSources.FromObjects("right", workload.Right(), CapacityRunner.RowMap),
            ReferenceDataSet.Empty);
        watch.Stop();
        return (watch.Elapsed.TotalSeconds, ResultFingerprint.Of(result, profile.Document.Fields));
    }

    private static ReportConcurrencyCase Case(
        FanOutStyle style,
        ProfileSharing sharing,
        int level,
        CapacityOptions options,
        BatchResult batch,
        bool matches)
    {
        var rows = (long)level * options.ConcurrencyRows * 2;
        var cores = Environment.ProcessorCount;
        return new ReportConcurrencyCase(
            StyleName(style),
            sharing is ProfileSharing.Separate ? "separate" : "shared",
            level,
            level,
            options.ConcurrencyRows,
            rows,
            batch.WallSeconds,
            level / batch.WallSeconds * 60,
            rows / batch.WallSeconds,
            0,
            batch.CpuSeconds / batch.WallSeconds,
            batch.CpuSeconds / batch.WallSeconds / cores * 100,
            batch.AllocatedBytes,
            batch.AllocatedBytes / batch.WallSeconds,
            batch.GcPauseSeconds,
            batch.Gen0,
            batch.Gen1,
            batch.Gen2,
            batch.PeakHeapBytes,
            batch.PeakWorkingSetBytes,
            batch.PeakThreadPoolThreads,
            batch.PeakPendingWorkItems,
            batch.PeakThreads,
            batch.LockContentionCount,
            Percentile(batch.LatencySeconds, 0.50),
            Percentile(batch.LatencySeconds, 0.95),
            Percentile(batch.LatencySeconds, 0.99),
            matches,
            batch.Fingerprints[0]);
    }

    private static string StyleName(FanOutStyle style) => style switch
    {
        FanOutStyle.Threads => "threads",
        FanOutStyle.Tasks => "tasks",
        _ => "parallel",
    };

    private static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count is 0)
            return 0;
        var ordered = values.Order().ToArray();
        return ordered[Math.Min(ordered.Length - 1, (int)Math.Ceiling(percentile * ordered.Length) - 1)];
    }

    private static IReadOnlyList<int> EffectiveLevels(IReadOnlyList<int> requested)
    {
        var cores = Environment.ProcessorCount;
        return requested.Count > 0
            ? [.. new[] { 1 }.Concat(requested).Distinct().Order()]
            : [.. new[] { 1, 2, 4, 8, cores, cores * 2 }.Distinct().Order()];
    }
}
