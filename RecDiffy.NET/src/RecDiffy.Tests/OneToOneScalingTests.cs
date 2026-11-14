using System.Diagnostics;
using System.Globalization;

namespace RecDiffy.Tests;

// Issue #99: building row dispositions must stay linear. One-to-one matching on a unique instrument, 4,000 rows per side against 16,000,
// after a warmup, median of several runs. Linear growth is about 4x and quadratic about 16x; the test fails at 8x or more.
// The minimum is the wrong summary. A 4,000-row run sometimes finishes before a GC pause, and that best time against a steady 16,000-row run crosses 8x.
// Moved from #98 (seeded fuzz) so the engine change and its proof ship together.
// Runs in its own non-parallel collection: other tests competing for the CPU would distort the timings.
[Collection(nameof(TimingCollection))]
public class OneToOneScalingTests
{
    private static readonly CompiledProfile ScaleProfile = Reconciler.Compile(ProfileDocument.Parse(Scale), RuleRegistry.CreateCore());

    private static readonly SourceMap<Dictionary<string, string>> ScaleMap = new SourceMap<Dictionary<string, string>>()
        .Text("Id", row => row["Id"])
        .Text("Instrument", row => row["Instrument"])
        .Text("Quantity", row => row["Quantity"]);

    [Fact]
    public void One_to_one_matching_scales_linearly()
    {
        const int smallRows = 4_000;
        const int largeRows = 16_000;
        const int warmup = 2;
        const int samples = 21;

        for (var attempt = 0; attempt < warmup; attempt++)
        {
            _ = Time(smallRows);
            _ = Time(largeRows);
        }

        var measured = Enumerable.Range(0, samples).Select(_ => (Small: Time(smallRows), Large: Time(largeRows))).ToArray();
        var small = Median(measured.Select(sample => sample.Small));
        var large = Median(measured.Select(sample => sample.Large));
        var ratio = large / small;

        Assert.True(
            ratio < 8,
            $"16,000 rows took {large:0} ms vs {small:0} ms for 4,000 ({ratio:0.00}x). Linear growth is about 4x.");
    }

    private static double Median(IEnumerable<double> samples)
    {
        var ordered = samples.Order().ToArray();
        return ordered[ordered.Length / 2];
    }

    private static double Time(int rows)
    {
        var left = ScaleRows(rows, "L");
        var right = ScaleRows(rows, "R");
        var watch = Stopwatch.StartNew();
        var result = Reconciler.Run(
            ScaleProfile,
            RecordSources.FromObjects("book", left, ScaleMap),
            RecordSources.FromObjects("book", right, ScaleMap),
            ReferenceDataSet.Empty);
        watch.Stop();
        Assert.Equal(rows, result.Summary.Matched);
        Assert.Equal(rows * 2, result.Rows.Length);
        return watch.Elapsed.TotalMilliseconds;
    }

    private static Dictionary<string, string>[] ScaleRows(int rows, string prefix) =>
    [
        .. Enumerable.Range(0, rows).Select(index => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Id"] = prefix + index.ToString(CultureInfo.InvariantCulture),
            ["Instrument"] = "I" + index.ToString(CultureInfo.InvariantCulture),
            ["Quantity"] = "1",
        }),
    ];

    private const string Scale = """
        {
          "formatVersion": 1,
          "profileId": "fuzz-scale",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Instrument", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
            "right": { "identify": [ { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
          },
          "paths": [ { "id": "trade", "route": { "rule": "route.all" }, "passes": [
            { "id": "P1", "pass": "match.naturalKey", "key": ["Instrument"] }
          ] } ]
        }
        """;
}

[CollectionDefinition(nameof(TimingCollection), DisableParallelization = true)]
public sealed class TimingCollection;
