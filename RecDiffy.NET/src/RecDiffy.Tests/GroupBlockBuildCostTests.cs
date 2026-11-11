using RecDiffy.Rules;

namespace RecDiffy.Tests;

// #40, for #19. Before the fix, the group pass checked every target against every many-side row of a block and kept a
// candidate list per target, then found the component too large to search. The membership it produced was the same as
// today's, so the defect shows only in the work a block costs. These tests bound that work by the bytes the run
// allocates on the test's thread: one block of 1,000 targets and 20,000 candidates, all wildcards, so it is one
// component and far over the item limit. Today the run allocates about 40 MB, and a cancelled run about 25 MB. With the
// cross product put back, a list per target is 1,000 × 20,000 indexes and the merge builds a set of 20,000 candidates
// for each target: both runs allocate about 470 MB.
public class GroupBlockBuildCostTests
{
    private const int Targets = 1_000;
    private const int Candidates = 20_000;

    // About three times today's cost, and about a quarter of the cross product's.
    private const long Budget = 128L * 1024 * 1024;

    private static readonly FieldDeclaration[] Fields =
    [
        new("Counterparty", FieldType.Symbol, FieldRole.Key, 0),
        new("Quantity", FieldType.Decimal, FieldRole.Economic, 2),
        new("TradeRef", FieldType.Identifier, FieldRole.Identity, 0),
    ];

    private static readonly PassDeclaration[] Passes =
    [
        new("G", "match.groupSum", [], ["Counterparty"], ["Quantity"], "left", null),
    ];

    [Fact]
    public void A_block_too_large_to_search_is_left_unresolved_without_a_candidate_list_per_target()
    {
        var (left, right) = Block(Targets, Candidates);
        Run(Block(2, 2), CancellationToken.None);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var report = Run((left, right), CancellationToken.None);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        var blocked = Assert.Single(report.Residue);
        Assert.Equal(("BlockTooLarge", Targets + Candidates), (blocked.Code, blocked.Rows.Count));
        Assert.Empty(report.Committed);
        Assert.True(allocated < Budget, $"One blocked component of {Targets} targets and {Candidates} candidates allocated {allocated:N0} bytes.");
    }

    // FoldResidue polls the token too, so a cancelled run threw before the fix as well, but only after the block was
    // built. The fix polls while the block is classified and merged, so a cancelled run stops before that work.
    [Fact]
    public void A_cancelled_run_stops_before_it_builds_a_large_block()
    {
        var (left, right) = Block(Targets, Candidates);
        Run(Block(2, 2), CancellationToken.None);
        using var source = new CancellationTokenSource();
        source.Cancel();

        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.ThrowsAny<OperationCanceledException>(() => Run((left, right), source.Token));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < Budget, $"A cancelled run over {Targets} targets and {Candidates} candidates allocated {allocated:N0} bytes before it threw.");
    }

    private static MatchReport Run((ConsolidatedRow[] Left, ConsolidatedRow[] Right) rows, CancellationToken cancellation) =>
        CascadeMatcher.Run(rows.Left, rows.Right, Passes, Fields, SearchLimits.Default, cancellation);

    // No row carries a TradeRef, so every row is a wildcard and the block is one component.
    private static (ConsolidatedRow[] Left, ConsolidatedRow[] Right) Block(int targets, int candidates) =>
        ([.. Enumerable.Range(0, candidates).Select(index => Row($"L{index}", Side.Left, 1m))],
         [.. Enumerable.Range(0, targets).Select(index => Row($"R{index}", Side.Right, 2m))]);

    private static ConsolidatedRow Row(string id, Side side, decimal quantity)
    {
        var row = RowContext.Create(id, side, new Dictionary<string, object?>(StringComparer.Ordinal));
        var (pool, symbol) = row.Pool.InternSymbol("JPM");
        row = row.WithPool(pool).Write("Counterparty", CanonicalCell.FromInterned(FieldType.Symbol, symbol), "test", null, "JPM");
        row = row.Write("Quantity", CanonicalCell.FromDecimal(quantity), "test", null, null);
        return new ConsolidatedRow([new SourceRef(side == Side.Left ? "left" : "right", id, 0)], row, false, null, null);
    }
}
