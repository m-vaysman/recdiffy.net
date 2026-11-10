using System.Collections.Frozen;
using System.Collections.Immutable;
using BenchmarkDotNet.Attributes;

namespace RecDiffy.Benchmarks;

// Issue #41. The group-sum pass alone, on the block shapes that stress component construction.
// Width is the number of targets that share one block key.
// Build times GroupIndex.Create: block placement, components, and the first search of each component.
// Match times the whole pass, where every commit evaluates its block again.
[MemoryDiagnoser]
[SimpleJob(launchCount: BenchScale.LaunchCount, warmupCount: BenchScale.WarmupCount, iterationCount: BenchScale.IterationCount)]
public class GroupConstructionBench
{
    private static readonly PassDeclaration[] GroupOnly = [BenchCascade.Group];

    [Params(BenchShape.DisjointTargets, BenchShape.WildcardHeavy, BenchShape.RepeatedIdentity)]
    public BenchShape Shape { get; set; }

    [Params(8, 64)]
    public int Width { get; set; }

    [Params(BenchScale.Large)]
    public int Rows { get; set; }

    private ConsolidatedRow[] _left = [];
    private ConsolidatedRow[] _right = [];
    private MatchWorkspace _workspace = null!;

    [GlobalSetup]
    public void Setup()
    {
        var dataset = BenchDatasets.Create(Shape, Rows, Width);
        _left = BenchCascade.Rows(dataset.Left);
        _right = BenchCascade.Rows(dataset.Right);
        _workspace = Workspace(_left, _right);
        BenchCascade.Verify(dataset, BenchCascade.Run(_left, _right, GroupOnly));
    }

    [Benchmark]
    public int Build()
    {
        var pass = BenchCascade.Group;
        var limits = SearchLimits.Default;
        return GroupIndex.Create(pass.Id, pass.Block, pass.Conserve, Side.Left, limits.MaxGroupSize, _workspace, new MatchCounters()).Counters.Components;
    }

    [Benchmark]
    public int Match() => BenchCascade.Run(_left, _right, GroupOnly).Committed.Count;

    // The workspace CascadeMatcher.Run builds for its first pass. Every bench row is active.
    private static MatchWorkspace Workspace(ConsolidatedRow[] left, ConsolidatedRow[] right)
    {
        ActiveItem[] items = [.. left.Concat(right).Select((row, index) => new ActiveItem(index, row.Context.Side, row))];
        var schema = BenchCascade.Fields.ToFrozenDictionary(field => field.Name, field => field, StringComparer.Ordinal);
        var free = items.Select(item => item.Index).ToImmutableHashSet();
        var limits = SearchLimits.Default;
        return new MatchWorkspace(BenchCascade.Group.Id, items, free, schema, limits, "TradeRef", limits.MaxGroupSize, CancellationToken.None);
    }
}
