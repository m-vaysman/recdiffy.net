using RecDiffy.Results;

namespace RecDiffy.Capacity;

internal sealed record CapacityReport(
    int SchemaVersion,
    ReportRun Run,
    ReportSource Source,
    ReportHost Host,
    IReadOnlyList<ReportCase> Cases,
    ReportDerived Derived,
    ReportConcurrency? Concurrency,
    IReadOnlyList<string> Notes);

internal sealed record ReportRun(
    string Id,
    string HarnessVersion,
    string EngineVersion,
    string ProfileId,
    string ProfileVersion,
    string ProfileHash,
    int Seed,
    int Repeats,
    string Sweep,
    IReadOnlyList<int> Sizes,
    string CommandLine,
    string StartedAtUtc,
    string FinishedAtUtc,
    double DurationSeconds,
    double CompileSeconds);

internal sealed record ReportSource(string Ref, string Commit, string WorkingTree);

internal sealed record ReportHost(
    string Machine,
    string Os,
    string Architecture,
    int LogicalProcessors,
    string Runtime,
    string Sdk,
    string GcMode,
    string GcLatencyMode,
    long GcHeapLimitBytes);

internal sealed record ReportCounts(
    int Matched,
    int Exact,
    int WithinTolerance,
    int Break,
    int LeftOnly,
    int RightOnly,
    int Ambiguous,
    int SearchIncomplete,
    int Quarantined,
    int Suppressed)
{
    public static ReportCounts Zero { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public static ReportCounts From(RunSummary summary) => new(
        summary.Matched,
        summary.Exact,
        summary.WithinTolerance,
        summary.Break,
        summary.LeftOnly,
        summary.RightOnly,
        summary.Ambiguous,
        summary.SearchIncomplete,
        summary.Quarantined,
        summary.Suppressed);

    public static ReportCounts Expected(ExpectedOutcome expected) => new(
        expected.Matched,
        expected.Exact,
        0,
        expected.Break,
        expected.LeftOnly,
        expected.RightOnly,
        0,
        0,
        0,
        0);

    public string Describe() =>
        $"matched {Matched.ToInvariant()}, exact {Exact.ToInvariant()}, within tolerance {WithinTolerance.ToInvariant()}, "
        + $"break {Break.ToInvariant()}, left only {LeftOnly.ToInvariant()}, right only {RightOnly.ToInvariant()}, "
        + $"ambiguous {Ambiguous.ToInvariant()}, search incomplete {SearchIncomplete.ToInvariant()}, "
        + $"quarantined {Quarantined.ToInvariant()}, suppressed {Suppressed.ToInvariant()}";
}

internal sealed record ReportSample(
    double EngineSeconds,
    double ExcelSeconds,
    double TotalSeconds,
    long PeakManagedHeapBytes,
    long PeakWorkingSetBytes,
    long AllocatedBytes,
    double GcPauseSeconds,
    int Gen0,
    int Gen1,
    int Gen2);

internal sealed record ReportStage(string Name, double Seconds, double SharePercent);

internal sealed record ReportFile(string Kind, string Path, long Bytes, string? SkipReason);

internal sealed record ReportCase(
    int Size,
    int LeftRows,
    int RightRows,
    int TotalRows,
    string Outcome,
    bool? KnownOutcomeMatches,
    ReportCounts Expected,
    ReportCounts Actual,
    IReadOnlyList<ReportSample> Samples,
    double LatencySeconds,
    double LatencyP95Seconds,
    double LatencyP99Seconds,
    double ThroughputRowsPerSecond,
    double EndToEndRowsPerSecond,
    long PeakManagedHeapBytes,
    long PeakWorkingSetBytes,
    long AllocatedBytes,
    double GcPauseSeconds,
    IReadOnlyList<ReportStage> Stages,
    IReadOnlyList<ReportFile> Files,
    IReadOnlyList<string> Notes)
{
    public double WallTimeBarPercent { get; init; }

    public double PeakHeapBarPercent { get; init; }
}

internal sealed record ReportDerived(
    int? LargestCompletedSize,
    IReadOnlyList<ReportScaling> Scaling,
    IReadOnlyList<ReportUnmeasuredStage> UnmeasuredStages);

internal sealed record ReportScaling(int FromSize, int ToSize, double SecondsRatio, double RowsRatio, double Exponent, string Verdict);

internal sealed record ReportUnmeasuredStage(string Name, string Reason);

internal sealed record ReportConcurrency(
    int RowsPerRun,
    int Repeats,
    string GcMode,
    int LogicalProcessors,
    string BaselineFingerprint,
    bool AllMatchBaseline,
    IReadOnlyList<ReportConcurrencyCase> Cases,
    IReadOnlyList<string> Notes);

internal sealed record ReportConcurrencyCase(
    string Style,
    string Sharing,
    int Level,
    int Runs,
    int RowsPerRun,
    long TotalRows,
    double WallSeconds,
    double RunsPerMinute,
    double RowsPerSecond,
    double ScalingEfficiencyPercent,
    double CoresUsed,
    double CoresUsedPercent,
    long AllocatedBytes,
    double AllocatedBytesPerSecond,
    double GcPauseSeconds,
    int Gen0,
    int Gen1,
    int Gen2,
    long PeakManagedHeapBytes,
    long PeakWorkingSetBytes,
    int PeakThreadPoolThreads,
    int PeakPendingWorkItems,
    int PeakThreads,
    long LockContentionCount,
    double LatencyP50Seconds,
    double LatencyP95Seconds,
    double LatencyP99Seconds,
    bool ResultsMatchBaseline,
    string Fingerprint)
{
    public double ThroughputBarPercent { get; init; }
}
