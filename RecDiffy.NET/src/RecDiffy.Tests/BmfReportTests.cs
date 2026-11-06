using System.Text.Json;
using RecDiffy.Benchmarks;

namespace RecDiffy.Tests;

public class BmfReportTests
{
    [Fact]
    public void Publishes_latency_allocations_and_row_throughput()
    {
        var json = BmfReport.FromDocuments([Report("CascadeScaleBench", "Match", "Rows=10000", mean: 2_000_000_000d, spread: 100_000_000d, allocated: 4096, gen0: 3, gen1: 1, gen2: 0)]);
        using var document = JsonDocument.Parse(json);
        var benchmark = document.RootElement.GetProperty("RecDiffy.Benchmarks.CascadeScaleBench.Match(Rows=10000)");

        Assert.Equal(2_000_000_000d, benchmark.GetProperty("latency").GetProperty("value").GetDouble());
        Assert.Equal(1_900_000_000d, benchmark.GetProperty("latency").GetProperty("lower_value").GetDouble());
        Assert.Equal(2_100_000_000d, benchmark.GetProperty("latency").GetProperty("upper_value").GetDouble());
        Assert.Equal(4096, benchmark.GetProperty("allocated-bytes").GetProperty("value").GetDouble());
        Assert.Equal(3, benchmark.GetProperty("gen0-collections").GetProperty("value").GetDouble());
        Assert.Equal(1, benchmark.GetProperty("gen1-collections").GetProperty("value").GetDouble());
        Assert.Equal(0, benchmark.GetProperty("gen2-collections").GetProperty("value").GetDouble());
        // 20,000 rows (both sides) in 2 seconds.
        Assert.Equal(10_000d, benchmark.GetProperty("throughput").GetProperty("value").GetDouble());
    }

    [Fact]
    public void Omits_throughput_when_the_benchmark_has_no_row_parameter()
    {
        var json = BmfReport.FromDocuments([Report("ExactDiffBench", "ExactPair", "", mean: 50, spread: 5, allocated: 16, gen0: 0, gen1: 0, gen2: 0)]);
        using var document = JsonDocument.Parse(json);
        var benchmark = document.RootElement.GetProperty("RecDiffy.Benchmarks.ExactDiffBench.ExactPair");

        Assert.False(benchmark.TryGetProperty("throughput", out _));
        Assert.Equal(16, benchmark.GetProperty("allocated-bytes").GetProperty("value").GetDouble());
    }

    [Fact]
    public void Rejects_two_reports_that_collapse_to_one_name()
    {
        var report = Report("CascadeScaleBench", "Match", "Rows=10", mean: 1, spread: 0, allocated: 1, gen0: 0, gen1: 0, gen2: 0);
        var error = Assert.Throws<InvalidOperationException>(() => BmfReport.FromDocuments([report, report]));
        Assert.Contains("Duplicate benchmark name", error.Message, StringComparison.Ordinal);
    }

    private static string Report(string type, string method, string parameters, double mean, double spread, int allocated, int gen0, int gen1, int gen2) =>
        $$"""
        {
          "Benchmarks": [
            {
              "Namespace": "RecDiffy.Benchmarks",
              "Type": "{{type}}",
              "Method": "{{method}}",
              "Parameters": "{{parameters}}",
              "Statistics": { "Mean": {{mean}}, "StandardDeviation": {{spread}} },
              "Memory": {
                "BytesAllocatedPerOperation": {{allocated}},
                "Gen0Collections": {{gen0}},
                "Gen1Collections": {{gen1}},
                "Gen2Collections": {{gen2}}
              }
            }
          ]
        }
        """;
}
