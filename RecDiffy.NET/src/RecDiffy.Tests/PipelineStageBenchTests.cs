#if NET10_0_OR_GREATER
using RecDiffy.Benchmarks;

namespace RecDiffy.Tests;

// #377 P2: every stage of PipelineStageBench runs on the dataset it was built for, so a drift fails in CI instead of
// being timed as something else.
public class PipelineStageBenchTests
{
    private const int Rows = 150;

    [Fact]
    public void Every_stage_returns_the_count_it_was_built_for()
    {
        var bench = new PipelineStageBench { Rows = Rows };

        bench.Setup();

        Assert.Equal(Rows, Run(bench, PipelineStage.Normalize));
        Assert.Equal(Rows, Run(bench, PipelineStage.Consolidate));
        Assert.Equal(Rows / 2, Run(bench, PipelineStage.Packages));
        Assert.Equal(Rows, Run(bench, PipelineStage.Match));
        Assert.True(Run(bench, PipelineStage.Diff) > 0);
        Assert.Equal(Rows, Run(bench, PipelineStage.Build));
    }

    private static int Run(PipelineStageBench bench, PipelineStage stage)
    {
        bench.Stage = stage;
        return bench.Run();
    }
}
#endif
