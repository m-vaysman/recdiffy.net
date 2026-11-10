#if NET10_0_OR_GREATER
using System.Text.Json;
using RecDiffy.Benchmarks;

namespace RecDiffy.Tests;

// BenchmarkDotNet 0.15 joins parameters with '&' ("Rows=1000&degree=1"), and BmfReport looks for Rows between commas.
// The benchmark project is referenced on net10.0 only.
public class BmfReportParameterTests
{
    [Fact(Skip = "🔴 #265: BmfReport splits parameters on commas, so a benchmark with two parameters gets no throughput.")]
    public void Publishes_throughput_when_benchmarkdotnet_joins_two_parameters_with_an_ampersand()
    {
        const string report = """
            {
              "Benchmarks": [
                {
                  "Namespace": "RecDiffy.Benchmarks",
                  "Type": "ExecutionModeBench",
                  "Method": "Parallel",
                  "Parameters": "Rows=1000&degree=1",
                  "Statistics": { "Mean": 1000000000, "StandardDeviation": 0 }
                }
              ]
            }
            """;

        using var document = JsonDocument.Parse(BmfReport.FromDocuments([report]));
        var benchmark = document.RootElement.GetProperty("RecDiffy.Benchmarks.ExecutionModeBench.Parallel(Rows=1000&degree=1)");

        // 2,000 rows (both sides) in one second.
        Assert.True(benchmark.TryGetProperty("throughput", out var throughput), "No throughput for a benchmark with two parameters.");
        Assert.Equal(2_000d, throughput.GetProperty("value").GetDouble());
    }
}
#endif
