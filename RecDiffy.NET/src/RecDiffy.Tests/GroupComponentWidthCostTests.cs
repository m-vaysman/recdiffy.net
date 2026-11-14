#if NET10_0_OR_GREATER
using RecDiffy.Benchmarks;

namespace RecDiffy.Tests;

// #407, for #41. One block, not a row sweep. Eight times the targets must stay under sixteen times the bytes.
// A quadratic copy of the growing target list is about sixty-four times and fails. No stopwatch: load must not move it.
public class GroupComponentWidthCostTests
{
    public static TheoryData<BenchShape> Shapes =>
    [
        BenchShape.DisjointTargets,
        BenchShape.RepeatedIdentity,
        BenchShape.WildcardHeavy,
    ];

    [Theory]
    [MemberData(nameof(Shapes))]
    public void One_block_at_eight_times_the_targets_allocates_under_sixteen_times(BenchShape shape)
    {
        var small = Allocated(shape, 32);
        var large = Allocated(shape, 256);
        var ratio = (double)large / small;

        Assert.True(
            ratio < 16,
            $"{shape} at 32 targets allocated {small.ToInvariant("N0")} bytes and 256 allocated {large.ToInvariant("N0")} ({ratio.ToInvariant("0.0")}x). Linear growth is about 8x.");
    }

    [Theory]
    [InlineData(BenchShape.DisjointTargets, 32, 32)]
    [InlineData(BenchShape.DisjointTargets, 256, 256)]
    [InlineData(BenchShape.RepeatedIdentity, 32, 1)]
    [InlineData(BenchShape.RepeatedIdentity, 256, 1)]
    [InlineData(BenchShape.WildcardHeavy, 32, 1)]
    [InlineData(BenchShape.WildcardHeavy, 256, 1)]
    public void The_sized_dataset_is_one_block_with_the_shape_s_components(BenchShape shape, int width, int components)
    {
        var bench = Prepare(shape, width);

        Assert.Equal(components, bench.Build());
    }

    private static long Allocated(BenchShape shape, int width)
    {
        var bench = Prepare(shape, width);
        _ = bench.Build();
        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = bench.Build();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static GroupConstructionBench Prepare(BenchShape shape, int width)
    {
        var bench = new GroupConstructionBench { Shape = shape, Width = width, Rows = RowsForOneBlock(shape, width) };
        bench.Setup();
        return bench;
    }

    // Partition's component count is round(2 * total / (min + max)). These row counts land on Width, so Chunk yields one block.
    private static int RowsForOneBlock(BenchShape shape, int width) => shape switch
    {
        BenchShape.DisjointTargets => width * 2,
        BenchShape.RepeatedIdentity or BenchShape.WildcardHeavy => width * 7 / 4,
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "This gate covers the three wide shapes."),
    };
}
#endif
