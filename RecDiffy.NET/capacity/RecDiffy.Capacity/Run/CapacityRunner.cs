using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using RecDiffy.Excel;
using RecDiffy.Results;

namespace RecDiffy.Capacity;

internal static class CapacityRunner
{
    private const string HarnessVersion = "1.0.0";

    internal static readonly SourceMap<RawTrade> RowMap =
        new SourceMap<RawTrade>()
            .Text("TradeId", trade => trade.TradeId)
            .Text("Counterparty", trade => trade.Counterparty)
            .Text("Instrument", trade => trade.Instrument)
            .Text("TradeDate", trade => trade.TradeDate)
            .Text("Side", trade => trade.Side)
            .Text("Currency", trade => trade.Currency)
            .Text("Quantity", trade => trade.Quantity)
            .Text("Price", trade => trade.Price)
            .Text("NetCash", trade => trade.NetCash);

    public static int Execute(CapacityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var started = DateTimeOffset.UtcNow;
        var source = SourceInfo.Capture(options);
        var host = HostInfo.Capture();
        var runId = RunId(started, source.Commit);
        var folder = options.Out is { Length: > 0 } chosen
            ? Path.GetFullPath(chosen)
            : Path.Combine(Environment.CurrentDirectory, "capacity-runs", runId);
        Directory.CreateDirectory(folder);

        var compiled = Stopwatch.StartNew();
        var profile = Compile();
        compiled.Stop();
        Console.WriteLine($"RecDiffy capacity run {runId}");
        Console.WriteLine($"  sweep   {SweepName(options.Sweep)}");
        Console.WriteLine($"  profile {CapacityProfile.Id} {CapacityProfile.Version} ({profile.Hash}), seed {options.Seed.ToInvariant()}");
        Console.WriteLine($"  output  {folder}");

        return options.Sweep is Sweep.Concurrency
            ? ExecuteConcurrency(options, profile, source, host, runId, started, compiled.Elapsed.TotalSeconds, folder)
            : ExecuteSizes(options, profile, source, host, runId, started, compiled.Elapsed.TotalSeconds, folder);
    }

    private static CompiledProfile Compile() =>
        Reconciler.Compile(ProfileDocument.Parse(CapacityProfile.Json), RuleRegistry.CreateCore());

    private static string SweepName(Sweep sweep) => sweep switch
    {
        Sweep.Concurrency => "concurrency",
        _ => "sizes",
    };

    private static int ExecuteSizes(
        CapacityOptions options,
        CompiledProfile profile,
        ReportSource source,
        ReportHost host,
        string runId,
        DateTimeOffset started,
        double compileSeconds,
        string folder)
    {
        var cases = new List<ReportCase>();
        Warmup(options, profile, folder);
        foreach (var size in options.Sizes)
        {
            var measured = Measure(size, options, profile, folder);
            cases.Add(measured);
            Print(measured);
        }

        var finished = DateTimeOffset.UtcNow;
        var report = BuildReport(options, source, host, runId, started, finished, compileSeconds, profile, cases);
        var paths = ReportFiles.Write(folder, report);

        var wrong = cases.Any(item => item.KnownOutcomeMatches is false);
        return Finish(paths, wrong, "a known outcome does not match the engine's");
    }

    private static int ExecuteConcurrency(
        CapacityOptions options,
        CompiledProfile profile,
        ReportSource source,
        ReportHost host,
        string runId,
        DateTimeOffset started,
        double compileSeconds,
        string folder)
    {
        using var process = Process.GetCurrentProcess();
        var outcome = ConcurrencySweep.Run(profile, Compile, options, process);
        var finished = DateTimeOffset.UtcNow;
        var notes = ConcurrencyNotes(options);
        var report = new CapacityReport(
            2,
            new ReportRun(
                runId,
                HarnessVersion,
                EngineVersion(),
                CapacityProfile.Id,
                CapacityProfile.Version,
                profile.Hash,
                options.Seed,
                options.Repeats,
                SweepName(options.Sweep),
                options.Sizes,
                Environment.CommandLine,
                Timestamp(started),
                Timestamp(finished),
                (finished - started).TotalSeconds,
                compileSeconds),
            source,
            host,
            Array.Empty<ReportCase>(),
            new ReportDerived(null, Array.Empty<ReportScaling>(), UnmeasuredStages()),
            new ReportConcurrency(
                options.ConcurrencyRows,
                options.Repeats,
                host.GcMode,
                host.LogicalProcessors,
                outcome.BaselineFingerprint,
                outcome.AllMatchBaseline,
                outcome.Cases,
                notes),
            Array.Empty<string>());
        var paths = ReportFiles.Write(folder, report);
        PrintConcurrency(outcome);
        return Finish(paths, outcome.AllMatchBaseline is false, "a concurrent run did not match the single-threaded result");
    }

    private static int Finish((string Results, string Markdown, string Html) paths, bool wrong, string failure)
    {
        var missing = new[] { paths.Results, paths.Markdown, paths.Html }
            .Any(path => new FileInfo(path) is not { Exists: true, Length: > 0 });
        Console.WriteLine($"results  {paths.Results}");
        Console.WriteLine($"markdown {paths.Markdown}");
        Console.WriteLine($"html     {paths.Html}");
        Console.WriteLine(wrong
            ? $"FAILED: {failure}; the numbers are not a measurement."
            : "OK: every measured result matched its known outcome.");
        return wrong || missing ? 1 : 0;
    }

    private static void Warmup(CapacityOptions options, CompiledProfile profile, string runFolder)
    {
        var workload = new Workload(options.Sizes[0], options.BreakRate, options.UnmatchedRate, options.Seed);
        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", workload.Left(), RowMap),
            RecordSources.FromObjects("right", workload.Right(), RowMap),
            ReferenceDataSet.Empty);
        if (options.WriteExcel)
            WriteReports(result, profile, Path.Combine(runFolder, "excel", "warmup"), runFolder);
    }

    private static ReportCase Measure(int size, CapacityOptions options, CompiledProfile profile, string runFolder)
    {
        var workload = new Workload(size, options.BreakRate, options.UnmatchedRate, options.Seed);
        var totalRows = workload.LeftRows + workload.RightRows;
        var samples = new List<ReportSample>();
        var notes = new List<string>();
        var files = new List<ReportFile>();
        var actual = ReportCounts.Zero;
        bool? matched = null;
        var outcome = "Completed";

        for (var repeat = 0; repeat < options.Repeats; repeat++)
        {
            using var timeout = new CancellationTokenSource();
            if (options.CaseTimeoutSeconds > 0)
                timeout.CancelAfter(TimeSpan.FromSeconds(options.CaseTimeoutSeconds));
            try
            {
                var allocated = GC.GetTotalAllocatedBytes();
                var pause = GC.GetTotalPauseDuration().TotalSeconds;
                var gen0 = GC.CollectionCount(0);
                var gen1 = GC.CollectionCount(1);
                var gen2 = GC.CollectionCount(2);

                using var sampler = new MemorySampler();
                var engine = Stopwatch.StartNew();
                var result = Reconciler.Run(
                    profile,
                    RecordSources.FromObjects("left", workload.Left(), RowMap),
                    RecordSources.FromObjects("right", workload.Right(), RowMap),
                    ReferenceDataSet.Empty,
                    new RunOptions(),
                    timeout.Token);
                engine.Stop();
                var excel = Stopwatch.StartNew();
                if (options.WriteExcel)
                    files.AddRange(WriteReports(result, profile, Path.Combine(runFolder, "excel", size.ToInvariant(), repeat.ToInvariant()), runFolder));
                excel.Stop();
                sampler.SampleNow();

                samples.Add(new ReportSample(
                    engine.Elapsed.TotalSeconds,
                    excel.Elapsed.TotalSeconds,
                    engine.Elapsed.TotalSeconds + excel.Elapsed.TotalSeconds,
                    sampler.PeakHeapBytes,
                    sampler.PeakWorkingSetBytes,
                    GC.GetTotalAllocatedBytes() - allocated,
                    GC.GetTotalPauseDuration().TotalSeconds - pause,
                    GC.CollectionCount(0) - gen0,
                    GC.CollectionCount(1) - gen1,
                    GC.CollectionCount(2) - gen2));

                actual = ReportCounts.From(result.Summary);
                var mismatch = Verify(workload, result);
                notes.AddRange(mismatch);
                matched = (matched ?? true) && mismatch.Count is 0;
                outcome = result.Summary.SearchIncomplete > 0 ? "CompletedWithSearchIncomplete" : "Completed";
            }
            catch (OperationCanceledException)
            {
                outcome = "Cancelled";
                matched = null;
                notes.Add($"repeat {repeat.ToInvariant()} was cancelled after {options.CaseTimeoutSeconds.ToInvariant()} s");
            }
            catch (OutOfMemoryException)
            {
                outcome = "OutOfMemory";
                matched = null;
                notes.Add($"repeat {repeat.ToInvariant()} ran out of managed memory");
            }
        }

        return Summarize(size, workload, totalRows, outcome, matched, actual, samples, notes, files);
    }

    private static ReportCase Summarize(
        int size,
        Workload workload,
        int totalRows,
        string outcome,
        bool? matched,
        ReportCounts actual,
        List<ReportSample> samples,
        List<string> notes,
        List<ReportFile> files)
    {
        var ordered = samples.OrderBy(sample => sample.TotalSeconds).ToArray();
        var median = ordered.Length is 0 ? null : ordered[ordered.Length / 2];
        var stages = median is null ? Array.Empty<ReportStage>() : MeasuredStages(median).ToArray();
        var engineSeconds = median?.EngineSeconds ?? 0;
        var endToEnd = engineSeconds + (median?.ExcelSeconds ?? 0);
        return new ReportCase(
            size,
            workload.LeftRows,
            workload.RightRows,
            totalRows,
            outcome,
            matched,
            ReportCounts.Expected(workload.Expected),
            actual,
            ordered,
            median?.TotalSeconds ?? 0,
            Percentile(ordered, 0.95),
            Percentile(ordered, 0.99),
            engineSeconds > 0 ? totalRows / engineSeconds : 0,
            endToEnd > 0 ? totalRows / endToEnd : 0,
            ordered.Length is 0 ? 0 : ordered.Max(sample => sample.PeakManagedHeapBytes),
            ordered.Length is 0 ? 0 : ordered.Max(sample => sample.PeakWorkingSetBytes),
            median?.AllocatedBytes ?? 0,
            median?.GcPauseSeconds ?? 0,
            stages,
            files,
            notes);
    }

    private static IEnumerable<ReportStage> MeasuredStages(ReportSample median)
    {
        var measured = median.EngineSeconds + median.ExcelSeconds;
        yield return new ReportStage("engine", median.EngineSeconds, Share(median.EngineSeconds, measured));
        if (median.ExcelSeconds > 0)
            yield return new ReportStage("excel", median.ExcelSeconds, Share(median.ExcelSeconds, measured));
    }

    private static double Share(double value, double total) => total > 0 ? 100 * value / total : 0;

    private static double Percentile(IReadOnlyList<ReportSample> ordered, double percentile) =>
        ordered.Count is 0
            ? 0
            : ordered[Math.Min(ordered.Count - 1, (int)Math.Ceiling(percentile * ordered.Count) - 1)].TotalSeconds;

    private static IReadOnlyList<string> Verify(Workload workload, ReconciliationResult result)
    {
        var expected = ReportCounts.Expected(workload.Expected);
        var actual = ReportCounts.From(result.Summary);
        var notes = new List<string>();
        if (actual != expected)
            notes.Add($"known outcome mismatch: expected {expected.Describe()}, the engine gave {actual.Describe()}");
        var rows = workload.LeftRows + workload.RightRows;
        if (result.Rows.Count != rows)
            notes.Add($"row results: expected {rows.ToInvariant()}, the engine gave {result.Rows.Count.ToInvariant()}");
        var entries = expected.Matched + expected.LeftOnly + expected.RightOnly;
        if (result.Entries.Count != entries)
            notes.Add($"entries: expected {entries.ToInvariant()}, the engine gave {result.Entries.Count.ToInvariant()}");
        return notes;
    }

    private static IReadOnlyList<ReportFile> WriteReports(ReconciliationResult result, CompiledProfile profile, string folder, string runFolder)
    {
        var outcomes = result.WriteAll(folder, new ExcelReportOptions
        {
            AsOf = new DateOnly(2025, 6, 11),
            Profile = profile,
            Title = "RecDiffy capacity run",
            Reports = [ReportKind.SideBySideCompare],
        });

        return [.. outcomes.Select(outcome => File(outcome, runFolder))];
    }

    private static ReportFile File(ReportOutcome outcome, string runFolder) =>
        outcome.Path is { } path
            ? new ReportFile(outcome.Kind.ToString(), Path.GetRelativePath(runFolder, path), new FileInfo(path).Length, null)
            : new ReportFile(outcome.Kind.ToString(), "", 0, outcome.SkipReason ?? "not written");

    private static CapacityReport BuildReport(
        CapacityOptions options,
        ReportSource source,
        ReportHost host,
        string runId,
        DateTimeOffset started,
        DateTimeOffset finished,
        double compileSeconds,
        CompiledProfile profile,
        IReadOnlyList<ReportCase> cases)
    {
        var withBars = AddBars(cases);
        return new CapacityReport(
            2,
            new ReportRun(
                runId,
                HarnessVersion,
                EngineVersion(),
                CapacityProfile.Id,
                CapacityProfile.Version,
                profile.Hash,
                options.Seed,
                options.Repeats,
                SweepName(options.Sweep),
                options.Sizes,
                Environment.CommandLine,
                Timestamp(started),
                Timestamp(finished),
                (finished - started).TotalSeconds,
                compileSeconds),
            source,
            host,
            withBars,
            new ReportDerived(
                withBars.Where(item => item.Outcome is "Completed" && item.KnownOutcomeMatches is not false).MaxBy(item => item.Size)?.Size,
                Scaling(withBars),
                UnmeasuredStages()),
            null,
            Notes(options, withBars));
    }

    private static IReadOnlyList<ReportCase> AddBars(IReadOnlyList<ReportCase> cases)
    {
        var longest = cases.Count is 0 ? 0 : cases.Max(item => item.LatencySeconds);
        var heaviest = cases.Count is 0 ? 0 : cases.Max(item => item.PeakManagedHeapBytes);
        return
        [
            .. cases.Select(item => item with
            {
                WallTimeBarPercent = Share(item.LatencySeconds, longest),
                PeakHeapBarPercent = Share(item.PeakManagedHeapBytes, heaviest),
            }),
        ];
    }

    private static IReadOnlyList<ReportScaling> Scaling(IReadOnlyList<ReportCase> cases)
    {
        var measured = cases.Where(item => item.Samples.Count > 0 && item.TotalRows > 0).ToArray();
        return [.. measured.Zip(measured.Skip(1)).Select(pair => Step(pair.First, pair.Second))];
    }

    private static ReportScaling Step(ReportCase from, ReportCase to)
    {
        var secondsRatio = from.LatencySeconds > 0 ? to.LatencySeconds / from.LatencySeconds : 0;
        var rowsRatio = from.TotalRows > 0 ? (double)to.TotalRows / from.TotalRows : 0;
        var exponent = secondsRatio > 0 && rowsRatio > 1 ? Math.Log(secondsRatio) / Math.Log(rowsRatio) : 0;
        return new ReportScaling(
            from.Size,
            to.Size,
            secondsRatio,
            rowsRatio,
            exponent,
            exponent <= 1.5 ? "linear or better" : "worse than linear");
    }

    private static IReadOnlyList<ReportUnmeasuredStage> UnmeasuredStages() =>
    [
        new("intake", "Reconciler.Run materializes both sides; the public API exposes no separate stage."),
        new("normalization", "Inside Reconciler.Run; per-stage benchmarks land in P2."),
        new("consolidation", "Inside Reconciler.Run; per-stage benchmarks land in P2."),
        new("matching", "Inside Reconciler.Run; per-stage benchmarks land in P2."),
        new("diff", "Inside Reconciler.Run; per-stage benchmarks land in P2."),
        new("results", "Inside Reconciler.Run; per-stage benchmarks land in P2."),
    ];

    private static IReadOnlyList<string> Notes(CapacityOptions options, IReadOnlyList<ReportCase> cases)
    {
        List<string> notes =
        [
            "Stages inside Reconciler.Run (intake, normalization, consolidation, matching, diff, result building) are inside the engine stage. The public API exposes no split; the per-stage benchmarks are P2 and the large-size split is P4.",
            "One warmup run of the smallest size is discarded before the measurements, so JIT, compile and the first Excel write are not charged to the first case.",
            "The dataset is generated while the engine materializes each side, so the engine stage includes the generator's cost. The generator is a seeded integer mixer, not System.Random, so a seed reproduces a dataset on every runtime.",
            "Excel output writes one report (SideBySideCompare) so the output stage stays bounded. The full report set is P4.",
            "Capacity numbers are comparable only on the same host, runtime and GC mode. This run's host is in the header.",
            "A run that does not match its known outcome is a measurement failure, not a fast run. A run too large to complete is recorded as its outcome.",
        ];
        if (cases.Any(item => item.Outcome is "CompletedWithSearchIncomplete"))
            notes.Add("At least one case completed with rows refused by the search limits. A refused search is not a fast reconciliation.");
        if (options.Repeats < 3 && cases.Any(item => item.Samples.Count > 0))
            notes.Add("Fewer than three measured runs per size: p95 and p99 repeat the samples they fall on, so they are not yet stable.");
        if (options.CaseTimeoutSeconds is 0)
            notes.Add("No case timeout: a run that hangs is stopped by hand.");
        return notes;
    }

    private static string Timestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string RunId(DateTimeOffset started, string commit) =>
        started.ToLocalTime().ToString("yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture)
        + "_"
        + (commit.Length >= 7 ? commit[..7] : "nogit");

    private static string EngineVersion()
    {
        var assembly = typeof(Reconciler).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return informational is { Length: > 0 } text ? text.Split('+', 2)[0] : assembly.GetName().Version?.ToString() ?? "unknown";
    }

    private static IReadOnlyList<string> ConcurrencyNotes(CapacityOptions options)
    {
        List<string> notes =
        [
            "A batch starts all N runs together over the same seeded dataset. Rows per run are fixed, so total work grows with N; the numbers are throughput (rows per second and runs per minute), not total batch time.",
            "One discarded batch per fan-out style runs before the measurements, so JIT and thread start-up are not charged to the first measured batch.",
            "Scaling efficiency is rows per second at N divided by N times rows per second at 1, for the same fan-out style and profile sharing.",
            "Every run's result is compared field by field with a single-threaded baseline: the summary, every entry with its diffs and both projections, and every row with its disposition and diagnostics. One difference fails the run.",
            "Separate profiles are compiled before the batch starts, so the difference between shared and separate is profile sharing, not compile cost.",
            "The GC mode is set by the process (DOTNET_gcServer). This report records the mode the sweep ran under; run the sweep once per mode.",
            "CPU is the process's total processor time over the batch divided by the batch wall time, so it counts every thread the run used.",
            "Not in this phase: culture isolation across threads, a second host, and a dotnet-trace race-detection pass. Both profile sharings and all three fan-out styles are measured.",
            "Per-run latency is one reconciliation's wall time inside the batch; p50, p95 and p99 are over the runs of the batch.",
        ];
        if (options.Repeats < 3)
            notes.Add("Fewer than three batches per level: the reported batch is the median by wall time, and the spread is not shown.");
        return notes;
    }

    private static void PrintConcurrency(ConcurrencyOutcome outcome)
    {
        foreach (var item in outcome.Cases)
        {
            Console.WriteLine(
                $"  {item.Style}/{item.Sharing} N={item.Level.ToInvariant()}: {item.RunsPerMinute.ToInvariant("0.0")} runs/min, "
                + $"{item.RowsPerSecond.ToInvariant("0")} rows/s, {item.ScalingEfficiencyPercent.ToInvariant("0.0")}% efficiency, "
                + $"{item.CoresUsed.ToInvariant("0.0")} cores, p50 {item.LatencyP50Seconds.ToInvariant("0.000")} s"
                + (item.ResultsMatchBaseline ? "" : " [RESULT MISMATCH]"));
        }
    }

    private static void Print(ReportCase measured)
    {
        var heapMegabytes = measured.PeakManagedHeapBytes / 1024.0 / 1024.0;
        var engine = measured.Stages.FirstOrDefault(stage => stage.Name is "engine")?.Seconds ?? 0;
        var excel = measured.Stages.FirstOrDefault(stage => stage.Name is "excel")?.Seconds;
        var stages = excel is null
            ? $"engine {engine.ToInvariant("0.000")} s"
            : $"engine {engine.ToInvariant("0.000")} s, excel {excel.Value.ToInvariant("0.000")} s";
        Console.WriteLine(
            $"  {measured.Size.ToInvariant()} rows/side: {measured.Outcome}, {stages}, "
            + $"{measured.ThroughputRowsPerSecond.ToInvariant("0")} rows/s, peak heap {heapMegabytes.ToInvariant("0.0")} MB"
            + (measured.KnownOutcomeMatches is false ? " [OUTCOME MISMATCH]" : ""));
    }
}
