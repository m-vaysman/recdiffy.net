#if NET10_0_OR_GREATER
using RecDiffy.Benchmarks;

namespace RecDiffy.Tests;

// Issue #407. One wide block is the shape that exposes component construction: a block of T one-side targets and
// E many-side rows used to be built by a fold that scanned and copied the components so far once per target.
// This measures the group pass on one block at 128 and at 1,024 targets, an 8x ratio, and holds the allocated
// bytes to under 16x. Copies that grow with the target count land near 64x; a linear build lands near 8x.
// It counts the calling thread's allocations, not time, so machine load does not move it, and the benchmark
// project is referenced on net10.0 only.
public class GroupComponentWidthCostTests
{
    // 128 -> 1,024 targets is 8x. Every shape is well under 16x once the partition is one pass, and the fold it
    // replaces measured 29.9x for DisjointTargets, 14.2x for RepeatedIdentity and 14.6x for WildcardHeavy.
    [Theory]
    [InlineData(BenchShape.DisjointTargets)]
    [InlineData(BenchShape.RepeatedIdentity)]
    [InlineData(BenchShape.WildcardHeavy)]
    public void Eight_times_the_targets_in_one_block_allocates_less_than_sixteen_times_as_much(BenchShape shape)
    {
        var small = Allocated(shape, 128);
        var large = Allocated(shape, 1_024);
        var ratio = (double)large / small;

        Assert.True(small > 0);
        Assert.True(ratio < 16, $"{shape} at 128 targets allocated {small:N0} bytes and at 1,024 allocated {large:N0} ({ratio:0.0}x). Linear growth is about 8x.");
    }

    // The widths the benchmark job runs at still commit the same matches, so the budget is measured on the shapes
    // the rest of the suite already accepts. Membership itself is checked in ComponentPartitionTests.
    [Theory]
    [InlineData(BenchShape.DisjointTargets, 8)]
    [InlineData(BenchShape.DisjointTargets, 64)]
    [InlineData(BenchShape.RepeatedIdentity, 8)]
    [InlineData(BenchShape.RepeatedIdentity, 64)]
    [InlineData(BenchShape.WildcardHeavy, 8)]
    [InlineData(BenchShape.WildcardHeavy, 64)]
    public void The_benchmark_shapes_commit_the_matches_they_were_built_for(BenchShape shape, int width)
    {
        var bench = new GroupConstructionBench { Shape = shape, Width = width, Rows = 150 };

        bench.Setup();

        Assert.Equal(BenchDatasets.Create(shape, 150, width).Expected.Matches, bench.Match());
    }

    // A width far past the row count puts every target in one block, so the measurement is of one block's build.
    private static long Allocated(BenchShape shape, int targets)
    {
        var bench = new GroupConstructionBench { Shape = shape, Width = 1_000_000, Rows = targets };
        bench.Setup();
        _ = bench.Build();
        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = bench.Build();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
#endif
