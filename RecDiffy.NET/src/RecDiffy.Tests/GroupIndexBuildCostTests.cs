#if NET10_0_OR_GREATER
using RecDiffy.Benchmarks;

namespace RecDiffy.Tests;

// GroupIndex.Evaluate calls Drop for each block it evaluates, and Drop walks every ready proposal of every block,
// copying the index record once per key. Building the index for small allocation blocks, one target each, is then quadratic.
// This counts the calling thread's allocations, not time, so machine load does not move it.
// The benchmark project is referenced on net10.0 only.
public class GroupIndexBuildCostTests
{
    [Fact(Skip = "🔴 #273: GroupIndex.Drop walks every ready proposal on each block evaluation, so the group index builds in quadratic time and memory.")]
    public void Building_the_group_index_for_eight_times_the_rows_allocates_about_eight_times_as_much()
    {
        var small = Allocated(500);
        var large = Allocated(4_000);
        var ratio = (double)large / small;

        Assert.True(ratio < 16, $"500 rows per side allocated {small:N0} bytes and 4,000 allocated {large:N0} ({ratio:0.0}x). Linear growth is about 8x.");
    }

    private static long Allocated(int rows)
    {
        var bench = new GroupConstructionBench { Shape = BenchShape.SmallAllocations, Width = 1, Rows = rows };
        bench.Setup();
        _ = bench.Build();
        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = bench.Build();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
#endif
