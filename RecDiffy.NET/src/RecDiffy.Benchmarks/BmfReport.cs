using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RecDiffy.Benchmarks;

// BenchmarkDotNet's JSON exporter already includes Statistics and, with
// [MemoryDiagnoser], Memory. Bencher's c_sharp_dot_net adapter only publishes
// latency, so this writes Bencher Metric Format for one combined report.
public static class BmfReport
{
    public static void Write(string resultsDirectory, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(resultsDirectory);
        ArgumentNullException.ThrowIfNull(outputPath);
        var directory = Path.GetFullPath(resultsDirectory);
        Console.WriteLine($"BenchmarkDotNet results: {directory}");
        if (!Directory.Exists(directory))
        {
            var listing = Listing(Path.GetDirectoryName(directory));
            Console.WriteLine(listing);
            throw new InvalidOperationException($"BenchmarkDotNet results directory does not exist: {directory}{Environment.NewLine}{listing}");
        }

        var reports = ChooseReports(directory);
        if (reports.Count == 0)
        {
            var listing = Listing(directory);
            Console.WriteLine(listing);
            throw new InvalidOperationException($"No BenchmarkDotNet JSON reports in {directory}{Environment.NewLine}{listing}");
        }

        foreach (var report in reports)
            Console.WriteLine($"Reading {report}");

        File.WriteAllText(outputPath, FromDocuments(reports.Select(File.ReadAllText)));
        Console.WriteLine($"Wrote {Path.GetFullPath(outputPath)}");
    }

    // BenchmarkDotNet 0.15 writes --exporters json as *-report-full-compressed.json.
    // Older builds use *-report.json. One file per benchmark type, preferring the 0.15 name.
    private static IReadOnlyList<string> ChooseReports(string directory)
    {
        var chosen = new Dictionary<string, (int Rank, string Path)>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            if (Match(Path.GetFileName(path)) is not (var stem, var rank))
                continue;
            if (!chosen.TryGetValue(stem, out var existing) || rank < existing.Rank)
                chosen[stem] = (rank, path);
        }

        return [.. chosen.Values.Select(item => item.Path).OrderBy(path => path, StringComparer.Ordinal)];
    }

    private static (string Stem, int Rank)? Match(string fileName) => fileName switch
    {
        _ when Cut(fileName, "-report-full-compressed.json") is { } stem => (stem, 0),
        _ when Cut(fileName, "-report-full.json") is { } stem => (stem, 1),
        _ when Cut(fileName, "-report-brief-compressed.json") is { } stem => (stem, 3),
        _ when Cut(fileName, "-report-brief.json") is { } stem => (stem, 4),
        _ when Cut(fileName, "-report.json") is { } stem => (stem, 2),
        _ => null,
    };

    private static string? Cut(string fileName, string suffix) =>
        fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? fileName[..^suffix.Length] : null;

    private static string Listing(string? directory) =>
        directory is null || !Directory.Exists(directory)
            ? "(directory does not exist)"
            : Directory.EnumerateFileSystemEntries(directory).OrderBy(path => path, StringComparer.Ordinal).ToArray() switch
            {
                [] => "(empty directory)",
                var entries => string.Join(Environment.NewLine, entries),
            };

    public static string FromDocuments(IEnumerable<string> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var root = new JsonObject();
        foreach (var document in documents)
        {
            using var parsed = JsonDocument.Parse(document);
            if (!parsed.RootElement.TryGetProperty("Benchmarks", out var benchmarks) || benchmarks.ValueKind != JsonValueKind.Array)
                continue;
            using (var items = benchmarks.EnumerateArray())
            {
                while (items.MoveNext())
                    Add(root, items.Current);
            }
        }

        return root.Count == 0
            ? throw new InvalidOperationException("BenchmarkDotNet reports contained no benchmarks.")
            : root.ToJsonString();
    }

    private static void Add(JsonObject root, JsonElement benchmark)
    {
        var name = BenchmarkName(benchmark);
        if (root.ContainsKey(name))
            throw new InvalidOperationException($"Duplicate benchmark name '{name}'.");

        var statistics = benchmark.GetProperty("Statistics");
        var mean = statistics.GetProperty("Mean").GetDouble();
        var spread = statistics.GetProperty("StandardDeviation").GetDouble();
        var measures = new JsonObject
        {
            ["latency"] = Latency(mean, spread),
        };
        if (benchmark.TryGetProperty("Memory", out var memory) && memory.ValueKind == JsonValueKind.Object)
        {
            if (Number(memory, "BytesAllocatedPerOperation") is { } allocated)
                measures["allocated-bytes"] = Point(allocated);
            if (Number(memory, "Gen0Collections") is { } gen0)
                measures["gen0-collections"] = Point(gen0);
            if (Number(memory, "Gen1Collections") is { } gen1)
                measures["gen1-collections"] = Point(gen1);
            if (Number(memory, "Gen2Collections") is { } gen2)
                measures["gen2-collections"] = Point(gen2);
        }

        if (RowsPerSide(benchmark) is int rows && mean > 0)
            measures["throughput"] = Throughput(mean, spread, checked(rows * 2));

        root[name] = measures;
    }

    // Namespace.Type.Method, plus BenchmarkDotNet's Parameters text so Rows=10000
    // and Rows=100000 stay distinct. The C# adapter drops Parameters.
    private static string BenchmarkName(JsonElement benchmark)
    {
        var ns = Text(benchmark, "Namespace");
        var type = Text(benchmark, "Type");
        var method = Text(benchmark, "Method");
        var parameters = Text(benchmark, "Parameters");
        var qualified = ns.Length == 0 ? $"{type}.{method}" : $"{ns}.{type}.{method}";
        return parameters.Length == 0 ? qualified : $"{qualified}({parameters})";
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? ""
            : "";

    private static int? RowsPerSide(JsonElement benchmark)
    {
        var parameters = Text(benchmark, "Parameters");
        foreach (var part in parameters.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Split('=', 2) is ["Rows", var value]
                && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var rows))
                return rows;
        }

        return null;
    }

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number
            ? property.GetDouble()
            : null;

    // Mean is nanoseconds. Both sides have `Rows` rows, so throughput is rows processed per second.
    private static JsonObject Throughput(double meanNanoseconds, double spreadNanoseconds, int rows)
    {
        var value = Rate(meanNanoseconds, rows);
        var lower = Rate(meanNanoseconds + spreadNanoseconds, rows);
        double? upper = meanNanoseconds > spreadNanoseconds ? Rate(meanNanoseconds - spreadNanoseconds, rows) : null;
        return Band(value, lower, upper);
    }

    private static double Rate(double nanoseconds, int rows) => rows * 1_000_000_000d / nanoseconds;

    private static JsonObject Latency(double mean, double spread) =>
        Band(mean, Math.Max(0, mean - spread), mean + spread);

    private static JsonObject Point(double value) => new() { ["value"] = JsonValue.Create(value) };

    private static JsonObject Band(double value, double? lower, double? upper)
    {
        var metric = Point(value);
        if (lower is { } low && double.IsFinite(low))
            metric["lower_value"] = JsonValue.Create(low);
        if (upper is { } high && double.IsFinite(high))
            metric["upper_value"] = JsonValue.Create(high);
        return metric;
    }
}
