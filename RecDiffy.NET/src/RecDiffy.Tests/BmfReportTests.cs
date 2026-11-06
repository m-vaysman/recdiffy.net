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
    public void Reads_the_full_compressed_json_from_benchmarkdotnet_0_15()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(
            Path.Combine(directory.Path, "RecDiffy.Benchmarks.CascadeScaleBench-report-full-compressed.json"),
            Report("CascadeScaleBench", "Match", "Rows=1000", mean: 1_000_000d, spread: 0, allocated: 32, gen0: 1, gen1: 0, gen2: 0));
        File.WriteAllText(Path.Combine(directory.Path, "RecDiffy.Benchmarks.CascadeScaleBench-report.html"), "<html></html>");
        var output = Path.Combine(directory.Path, "bencher.bmf.json");

        BmfReport.Write(directory.Path, output);

        using var document = JsonDocument.Parse(File.ReadAllText(output));
        Assert.Equal(32, document.RootElement.GetProperty("RecDiffy.Benchmarks.CascadeScaleBench.Match(Rows=1000)").GetProperty("allocated-bytes").GetProperty("value").GetDouble());
    }

    [Fact]
    public void Uses_one_report_when_brief_and_full_json_both_exist()
    {
        using var directory = new TempDirectory();
        var json = Report("CascadeScaleBench", "Match", "Rows=10", mean: 1, spread: 0, allocated: 8, gen0: 0, gen1: 0, gen2: 0);
        File.WriteAllText(Path.Combine(directory.Path, "RecDiffy.Benchmarks.CascadeScaleBench-report.json"), json);
        File.WriteAllText(Path.Combine(directory.Path, "RecDiffy.Benchmarks.CascadeScaleBench-report-full-compressed.json"), json);
        var output = Path.Combine(directory.Path, "bencher.bmf.json");

        BmfReport.Write(directory.Path, output);

        using var document = JsonDocument.Parse(File.ReadAllText(output));
        Assert.Equal(8, document.RootElement.GetProperty("RecDiffy.Benchmarks.CascadeScaleBench.Match(Rows=10)").GetProperty("allocated-bytes").GetProperty("value").GetDouble());
    }

    [Fact]
    public void Names_the_files_present_when_no_json_report_matches()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "notes.txt"), "not a report");
        var output = Path.Combine(directory.Path, "bencher.bmf.json");

        var error = Assert.Throws<InvalidOperationException>(() => BmfReport.Write(directory.Path, output));

        Assert.Contains("notes.txt", error.Message, StringComparison.Ordinal);
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

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory() => Path = Directory.CreateTempSubdirectory("bmf-report-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
