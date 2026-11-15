#if NET10_0_OR_GREATER
using RecDiffy.Benchmarks;

namespace RecDiffy.Tests;

// #41, #407. The budget for building a block's components: a block with eight times the targets allocates less than
// sixteen times as much. Building them one target at a time against every component so far allocated about sixty-four
// times as much, because each target rescanned and recopied what the earlier targets had built.
// GroupIndexBuildCostTests grows the rows at a width of one, so it cannot see this: there the blocks multiply and each
// stays one target wide. Here the number of blocks stays the same and only their width grows.
// This counts the calling thread's allocations, not time, so machine load does not move it.
// The benchmark project is referenced on net10.0 only.
public class GroupComponentWidthCostTests
{
    private const int Narrow = 32;
    private const int Wide = 256;

    // The three block shapes #41 names: every target its own component, one component glued by rows with no
    // reference, and one component glued by a reference every row repeats.
    [Theory]
    [InlineData(BenchShape.DisjointTargets)]
    [InlineData(BenchShape.WildcardHeavy)]
    [InlineData(BenchShape.RepeatedIdentity)]
    public void A_block_with_eight_times_the_targets_allocates_about_eight_times_as_much(BenchShape shape)
    {
        var narrow = Allocated(shape, Narrow);
        var wide = Allocated(shape, Wide);
        var ratio = (double)wide / narrow;

        Assert.True(ratio < 16, $"{shape}: blocks of {Narrow} targets allocated {narrow:N0} bytes and blocks of {Wide} allocated {wide:N0} ({ratio:0.0}x). Linear growth is about 8x.");
    }

    // The rows grow with the width, so both sizes hold about the same number of blocks.
    private static long Allocated(BenchShape shape, int width)
    {
        var bench = new GroupConstructionBench { Shape = shape, Width = width, Rows = width * 16 };
        bench.Setup();
        _ = bench.Build();
        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = bench.Build();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
#endif
