using System.Globalization;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;

namespace RecDiffy.Benchmarks;

// Same one-to-one natural-key workload through the public entry points.
// MaxDegreeOfParallelism parallelizes result building (field comparison), not cascade matching.
// Sizes and the short job live in BenchScale.
[MemoryDiagnoser]
[SimpleJob(launchCount: BenchScale.LaunchCount, warmupCount: BenchScale.WarmupCount, iterationCount: BenchScale.IterationCount)]
public class ExecutionModeBench
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "bench-execution",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Instrument", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Price", "type": "Decimal", "scale": 6, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" } ],
              "parse": [
                { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
                { "rule": "decimal.parse", "from": "Price", "to": "Price" }
              ]
            },
            "right": {
              "identify": [ { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" } ],
              "parse": [
                { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
                { "rule": "decimal.parse", "from": "Price", "to": "Price" }
              ]
            }
          },
          "paths": [ { "id": "trade", "route": { "rule": "route.all" }, "passes": [
            { "id": "P1", "pass": "match.naturalKey", "key": ["Instrument"] }
          ] } ]
        }
        """;

    private static readonly CompiledProfile Compiled = Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore());

    private static readonly SourceMap<TradeRow> Map = new SourceMap<TradeRow>()
        .Text("Id", row => row.Id)
        .Text("Instrument", row => row.Instrument)
        .Text("Quantity", row => row.Quantity)
        .Text("Price", row => row.Price);

    [Params(BenchScale.Small, BenchScale.Large)]
    public int Rows { get; set; }

    private TradeRow[] _left = null!;
    private TradeRow[] _right = null!;

    [GlobalSetup]
    public void Setup()
    {
        _left = Side("L");
        _right = Side("R");
    }

    [Benchmark]
    public int Sync() => Match(new RunOptions());

    [Benchmark]
    public async Task<int> Async()
    {
        var result = await Reconciler.RunAsync(
            Compiled,
            RecordSources.FromObjects("book", Stream(_left), Map),
            RecordSources.FromObjects("book", Stream(_right), Map),
            ReferenceDataSet.Empty,
            new RunOptions()).ConfigureAwait(false);
        return result.Summary.Matched;
    }

    [Benchmark]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(4)]
    [Arguments(8)]
    public int Parallel(int degree) => Match(new RunOptions(degree));

    private int Match(RunOptions options) =>
        Reconciler.Run(
            Compiled,
            RecordSources.FromObjects("book", _left, Map),
            RecordSources.FromObjects("book", _right, Map),
            ReferenceDataSet.Empty,
            options).Summary.Matched;

    private TradeRow[] Side(string prefix) =>
    [
        .. Enumerable.Range(0, Rows).Select(index => new TradeRow(
            prefix + index.ToString(CultureInfo.InvariantCulture),
            "I" + index.ToString(CultureInfo.InvariantCulture),
            "1.00",
            "10.000000")),
    ];

    private static async IAsyncEnumerable<TradeRow> Stream(TradeRow[] rows)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        foreach (var row in rows)
            yield return row;
    }

    private readonly record struct TradeRow(string Id, string Instrument, string Quantity, string Price);
}
