namespace RecDiffy.Tests;

// Issue #407. ComponentPartition.Split is the one pass that replaced the fold building a block's components.
// Each case builds the same four shapes of claim against one block, at 128 and at 1,024 targets, and holds the
// allocated bytes to under 16x for 8x the targets. The fold this replaced allocated 31x for a repeated identity,
// 55x for distinct values, and 35x for a wildcard block at the same scale; a single pass stays near 8x.
// It counts the calling thread's allocations, not time, so machine load does not move it, and it runs on both
// target frameworks because the partition does not need the benchmark project.
public class ComponentPartitionCostTests
{
    [Theory]
    [InlineData(ClaimShape.Repeated)]
    [InlineData(ClaimShape.Distinct)]
    [InlineData(ClaimShape.Wildcard)]
    [InlineData(ClaimShape.Absent)]
    public void Eight_times_the_targets_allocates_less_than_sixteen_times_as_much(ClaimShape shape)
    {
        var small = Allocated(shape, 128);
        var large = Allocated(shape, 1_024);
        var ratio = (double)large / small;

        Assert.True(small > 0);
        Assert.True(ratio < 16, $"{shape} at 128 targets allocated {small:N0} bytes and at 1,024 allocated {large:N0} ({ratio:0.0}x). Linear growth is about 8x.");
    }

    public enum ClaimShape
    {
        // Every target claims one value the many side holds, so the whole block is one component.
        Repeated,

        // Every target claims a value of its own, so the block is 1,024 singleton components.
        Distinct,

        // Every target is a wildcard against a many side with no wildcard, so again one component.
        Wildcard,

        // Every target claims one value the many side does not hold, so no two targets are glued.
        Absent,
    }

    private static long Allocated(ClaimShape shape, int targets)
    {
        var claims = Enumerable.Range(0, targets).Select(index => Claim(shape, index)).ToArray();

        // The many side holds one row claiming "A" and has no wildcard, so Absent is the only shape whose targets
        // draw nothing and the other three all reach the grouping path.
        MatchKeys.SharedClaim[] many = [Claim(ClaimShape.Repeated, 0)];
        var census = Census.Collect([0], index => many[index], CancellationToken.None);

        _ = ComponentPartition.Split(claims, census, CancellationToken.None);
        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = ComponentPartition.Split(claims, census, CancellationToken.None);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static MatchKeys.SharedClaim Claim(ClaimShape shape, int index) => shape switch
    {
        ClaimShape.Repeated => new(false, "A"),
        ClaimShape.Distinct => new(false, "V" + index.ToInvariant()),
        ClaimShape.Wildcard => new(true, null),
        ClaimShape.Absent => new(false, "absent"),
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown claim shape."),
    };
}
