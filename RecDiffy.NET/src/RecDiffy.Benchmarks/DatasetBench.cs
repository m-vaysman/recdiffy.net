using BenchmarkDotNet.Attributes;

namespace RecDiffy.Benchmarks;

// Issue #64. Every dataset shape through the four-pass cascade of CascadeTests, matcher only.
// Intake, normalization and result building are not timed. Sizes and the short job live in BenchScale.
[MemoryDiagnoser]
[SimpleJob(launchCount: BenchScale.LaunchCount, warmupCount: BenchScale.WarmupCount, iterationCount: BenchScale.IterationCount)]
public class DatasetBench
{
    [Params(
        BenchShape.ExactPairs,
        BenchShape.Unmatched,
        BenchShape.SmallAllocations,
        BenchShape.SplittableBlocks,
        BenchShape.OversizedComponents,
        BenchShape.DisjointTargets,
        BenchShape.WildcardHeavy)]
    public BenchShape Dataset { get; set; }

    [Params(BenchScale.Small, BenchScale.Large)]
    public int Rows { get; set; }

    private ConsolidatedRow[] _left = [];
    private ConsolidatedRow[] _right = [];

    [GlobalSetup]
    public void Setup()
    {
        var dataset = BenchDatasets.Create(Dataset, Rows);
        _left = BenchCascade.Rows(dataset.Left);
        _right = BenchCascade.Rows(dataset.Right);
        BenchCascade.Verify(dataset, BenchCascade.Run(_left, _right, BenchCascade.Passes));
    }

    [Benchmark]
    public int Match() => BenchCascade.Run(_left, _right, BenchCascade.Passes).Committed.Count;
}
