using BenchmarkDotNet.Attributes;
using RecDiffy.Rules;

namespace RecDiffy.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(launchCount: BenchScale.LaunchCount, warmupCount: BenchScale.WarmupCount, iterationCount: BenchScale.IterationCount)]
public class CascadeScaleBench
{
    [Params(BenchScale.Small, BenchScale.Large)]
    public int Rows { get; set; }

    private ConsolidatedRow[] _left = null!;
    private ConsolidatedRow[] _right = null!;
    private readonly PassDeclaration[] _passes =
    [
        new("P2", "match.naturalKey", ["Counterparty", "Instrument", "Quantity"], [], [], null, null),
    ];

    private readonly FieldDeclaration[] _fields =
    [
        new("Counterparty", FieldType.Symbol, FieldRole.Key, 0),
        new("Instrument", FieldType.Symbol, FieldRole.Key, 0),
        new("Quantity", FieldType.Decimal, FieldRole.Economic, 2),
    ];

    [GlobalSetup]
    public void Setup()
    {
        _left = new ConsolidatedRow[Rows];
        _right = new ConsolidatedRow[Rows];
        for (var index = 0; index < Rows; index++)
        {
            _left[index] = Row($"L{index}", Side.Left, index);
            _right[index] = Row($"R{index}", Side.Right, index);
        }
    }

    [Benchmark]
    public int Match() => CascadeMatcher.Run(_left, _right, _passes, _fields, SearchLimits.Default).Committed.Count;

    private static ConsolidatedRow Row(string id, Side side, int index)
    {
        var row = RowContext.Create(id, side, new Dictionary<string, object?>(StringComparer.Ordinal));
        var (partyPool, party) = row.Pool.InternSymbol("C");
        row = row.WithPool(partyPool).Write("Counterparty", CanonicalCell.FromInterned(FieldType.Symbol, party), "bench", null, "C");
        var (instrumentPool, instrument) = row.Pool.InternSymbol("I" + index);
        row = row.WithPool(instrumentPool).Write("Instrument", CanonicalCell.FromInterned(FieldType.Symbol, instrument), "bench", null, "I" + index);
        row = row.Write("Quantity", CanonicalCell.FromDecimal(10m), "bench", null, null);
        return new ConsolidatedRow([new SourceRef(side == Side.Left ? "left" : "right", id, index)], row, false, null, null);
    }
}
