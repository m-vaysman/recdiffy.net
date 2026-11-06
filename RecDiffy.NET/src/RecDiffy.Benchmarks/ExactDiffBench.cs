using BenchmarkDotNet.Attributes;
using RecDiffy;
using RecDiffy.Rules;

namespace RecDiffy.Benchmarks;

[MemoryDiagnoser]
public class ExactDiffBench
{
    private RowContext _left = null!;
    private RowContext _right = null!;
    private DiffPlan _plan = null!;

    [GlobalSetup]
    public void Setup()
    {
        _plan = DiffPlan.From(
            new PathDeclaration("trade", new RouteDeclaration("route.all", null, null), [], null),
            [
                new FieldDeclaration("Quantity", FieldType.Decimal, FieldRole.Economic, 2),
                new FieldDeclaration("Price", FieldType.Decimal, FieldRole.Economic, 6),
                new FieldDeclaration("NetCash", FieldType.Decimal, FieldRole.Economic, 2),
            ],
            MidpointMode.ToEven);
        _left = Row("L");
        _right = Row("R");
    }

    [Benchmark]
    public int ExactPair() => FieldDiffer.Compare(_left, _right, _plan).Length;

    private static RowContext Row(string id)
    {
        var row = RowContext.Create(id, id == "L" ? Side.Left : Side.Right, new Dictionary<string, object?>(StringComparer.Ordinal));
        return row
            .Write("Quantity", CanonicalCell.FromDecimal(1_000_000m), "bench", null, null)
            .Write("Price", CanonicalCell.FromDecimal(102.234375m), "bench", null, null)
            .Write("NetCash", CanonicalCell.FromDecimal(1_022_343.75m), "bench", null, null);
    }
}
