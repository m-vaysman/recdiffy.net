#if NET10_0_OR_GREATER
using RecDiffy.Benchmarks;

namespace RecDiffy.Tests;

// Issue #64: every benchmark dataset is reproducible from its seed and gives the outcome it was built for.
// The benchmark project is referenced on net10.0 only.
public class BenchDatasetTests
{
    private const int Rows = 150;

    public static TheoryData<BenchShape> Shapes => [.. Enum.GetValues<BenchShape>()];

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Builds_the_same_rows_from_the_seed(BenchShape shape)
    {
        var first = BenchDatasets.Create(shape, Rows);
        var second = BenchDatasets.Create(shape, Rows);

        Assert.Equal(first.Left, second.Left);
        Assert.Equal(first.Right, second.Right);
        Assert.Equal(first.Expected, second.Expected);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Holds_twice_the_row_parameter_with_unique_ids(BenchShape shape)
    {
        var dataset = BenchDatasets.Create(shape, Rows);

        Assert.Equal(2 * Rows, dataset.Left.Count + dataset.Right.Count);
        Assert.Equal(dataset.Left.Count, dataset.Left.Select(trade => trade.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(dataset.Right.Count, dataset.Right.Select(trade => trade.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(dataset.Left, trade => Assert.Equal(Side.Left, trade.Side));
        Assert.All(dataset.Right, trade => Assert.Equal(Side.Right, trade.Side));
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Expects_the_outcome_each_shape_is_named_for(BenchShape shape)
    {
        var expected = BenchDatasets.Create(shape, Rows).Expected;
        var all = 2 * Rows;

        var named = shape switch
        {
            BenchShape.ExactPairs => BenchOutcome.Empty with { Matches = Rows, MatchedRows = all },
            BenchShape.Unmatched => BenchOutcome.Empty with { Matches = Rows / 2, MatchedRows = 2 * (Rows / 2), LeftOnlyRows = Rows - Rows / 2, RightOnlyRows = Rows - Rows / 2 },
            BenchShape.OversizedComponents => BenchOutcome.Empty with { BlockedRows = all },
            _ => expected with { MatchedRows = all },
        };

        Assert.Equal(named, expected);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Gives_the_expected_outcome_through_the_four_pass_cascade(BenchShape shape)
    {
        var bench = new DatasetBench { Dataset = shape, Rows = Rows };

        bench.Setup();

        Assert.Equal(BenchDatasets.Create(shape, Rows).Expected.Matches, bench.Match());
    }

    [Theory]
    [InlineData(BenchShape.DisjointTargets, 8)]
    [InlineData(BenchShape.DisjointTargets, 64)]
    [InlineData(BenchShape.WildcardHeavy, 8)]
    [InlineData(BenchShape.WildcardHeavy, 64)]
    [InlineData(BenchShape.RepeatedIdentity, 8)]
    [InlineData(BenchShape.RepeatedIdentity, 64)]
    public void Gives_the_expected_outcome_through_the_group_pass_alone(BenchShape shape, int width)
    {
        var bench = new GroupConstructionBench { Shape = shape, Width = width, Rows = Rows };

        bench.Setup();

        Assert.Equal(BenchDatasets.Create(shape, Rows, width).Expected.Matches, bench.Match());
        Assert.True(bench.Build() > 0);
    }

    [Fact]
    public void Blocks_a_glued_block_wider_than_the_target_limit()
    {
        var wide = BenchDatasets.Create(BenchShape.WildcardHeavy, Rows, 64).Expected;
        var narrow = BenchDatasets.Create(BenchShape.WildcardHeavy, Rows, 8).Expected;

        Assert.Equal(0, wide.MatchedRows);
        Assert.Equal(2 * Rows, wide.BlockedRows);
        Assert.Equal(2 * Rows, narrow.MatchedRows);
    }

    [Fact]
    public void Rejects_a_size_too_small_for_one_component()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchDatasets.Create(BenchShape.OversizedComponents, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchDatasets.Create(BenchShape.ExactPairs, Rows, 2));
    }
}
#endif
