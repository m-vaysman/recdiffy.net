using System.Globalization;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// #424: reading a text number must not copy anything out of the text, so the per-call allocation is bounded.
public class CultureNumbersAllocationTests
{
    private const int Runs = 1_000;

    [Fact]
    public void A_plain_number_reads_with_under_eight_bytes_per_call()
    {
        var format = CultureInfo.GetCultureInfo("en-US").NumberFormat;
        // Warm up, so the JIT finishes its work on this path before bytes are counted.
        for (var warm = 0; warm < 200; warm++)
            _field = "1234.56".ReadNumber(format);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < Runs; index++)
            _field = "1234.56".ReadNumber(format);
        var perCall = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)Runs;

        Assert.True(perCall < 8, $"A plain number allocated {perCall.ToInvariant("0.0")} bytes per call.");
    }

    [Fact]
    public void A_grouped_number_reads_with_under_sixty_four_bytes_per_call()
    {
        var format = CultureInfo.GetCultureInfo("en-US").NumberFormat;
        // Warm up, so the JIT finishes its work on this path before bytes are counted.
        for (var warm = 0; warm < 200; warm++)
            _field = "1,234,567.89".ReadNumber(format);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < Runs; index++)
            _field = "1,234,567.89".ReadNumber(format);
        var perCall = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)Runs;

        Assert.True(perCall < 64, $"A grouped number allocated {perCall.ToInvariant("0.0")} bytes per call.");
    }

    // Written to so a call is never optimized away.
    private static NumberRead _field;
}
