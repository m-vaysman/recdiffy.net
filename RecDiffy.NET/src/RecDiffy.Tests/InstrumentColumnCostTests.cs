using System.Globalization;

namespace RecDiffy.Tests;

public class InstrumentColumnCostTests
{
    // A left column named ISIN, CUSIP, SEDOL or Ticker is read into an instrument master on every run, though no rule
    // reads that master. Its cost is what the same run allocates with the column, less what it allocates without.
    // Linear growth doubles that cost when the rows double. The intake runs on the calling thread, so the count is
    // that thread's allocations, which other tests running in parallel do not touch.
    [Fact(Skip = "🔴 #267: InstrumentMaster.Add copies the whole map for every entry, so the cost grows with the square of the left rows.")]
    public void An_isin_column_adds_a_cost_that_grows_linearly_with_the_left_rows()
    {
        _ = Allocated(500, isin: true);
        _ = Allocated(500, isin: false);
        var small = Allocated(2_000, isin: true) - Allocated(2_000, isin: false);
        var large = Allocated(4_000, isin: true) - Allocated(4_000, isin: false);

        Assert.True(
            large < 3 * small,
            $"The ISIN column cost {small.ToString("N0", CultureInfo.InvariantCulture)} bytes at 2,000 rows and {large.ToString("N0", CultureInfo.InvariantCulture)} at 4,000.");
    }

    private static long Allocated(int rows, bool isin)
    {
        var map = new SourceMap<int>()
            .Text("Id", index => "T" + index.ToString(CultureInfo.InvariantCulture))
            .Text("Qty", _ => "1");
        var withIsin = isin ? map.Text("ISIN", index => "US" + index.ToString("D10", CultureInfo.InvariantCulture)) : map;
        var profile = MidStream.Profile();
        var left = RecordSources.FromObjects("left", Enumerable.Range(0, rows).ToArray(), withIsin);
        var right = RecordSources.FromObjects("right", Enumerable.Range(0, rows).ToArray(), map);

        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = Reconciler.Run(profile, left, right, ReferenceDataSet.Empty, new RunOptions(MaxDegreeOfParallelism: 1));
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
