using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using RecDiffy.Engine;

var summary = BenchmarkRunner.Run<ExactDiffBench>();
return summary.Reports.All(report => report.Success) ? 0 : 1;

[MemoryDiagnoser]
public class ExactDiffBench
{
    private decimal[] _left = [];
    private decimal[] _right = [];

    [GlobalSetup]
    public void Setup()
    {
        _left = Enumerable.Repeat(96.078125m, 32).ToArray();
        _right = _left.ToArray();
    }

    [Benchmark]
    public int ExactPair() => HotPath.CompareExact(_left, _right);
}
