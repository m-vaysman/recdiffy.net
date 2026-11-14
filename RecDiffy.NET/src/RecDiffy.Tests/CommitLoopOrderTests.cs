using System.Globalization;
using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// #413: the commit loop in CascadeMatcher now fills its matches and its free rows through builders. These tests pin
// what the loop returns, so they pass before and after that change: the commits in order (the earliest pass first,
// then the smallest key), and the rows no pass took.
public class CommitLoopOrderTests
{
    private static readonly FieldDeclaration[] Fields =
    [
        new("Counterparty", FieldType.Symbol, FieldRole.Key, 0),
        new("Instrument", FieldType.Symbol, FieldRole.Key, 0),
        new("Quantity", FieldType.Decimal, FieldRole.Economic, 2),
    ];

    private static readonly PassDeclaration[] Passes =
    [
        new("exact", "match.naturalKey", ["Counterparty", "Instrument", "Quantity"], [], [], null, null),
        new("loose", "match.reducedKey", ["Counterparty", "Instrument"], [], [], null, null),
    ];

    [Fact]
    public void Commits_come_in_pass_order_then_key_order_and_untaken_rows_stay_unmatched()
    {
        var report = CascadeMatcher.Run(
            [Trade("L-c", Side.Left, "US3", 1m), Trade("L-only", Side.Left, "US9", 1m), Trade("L-a", Side.Left, "US1", 1m), Trade("L-d", Side.Left, "US4", 5m), Trade("L-b", Side.Left, "US2", 1m)],
            [Trade("R-b", Side.Right, "US2", 1m), Trade("R-d", Side.Right, "US4", 7m), Trade("R-only", Side.Right, "US8", 1m), Trade("R-c", Side.Right, "US3", 1m), Trade("R-a", Side.Right, "US1", 1m)],
            Passes,
            Fields,
            SearchLimits.Default);

        Assert.Equal(["exact", "exact", "exact", "loose"], report.Committed.Select(match => match.PassId), StringComparer.Ordinal);
        Assert.Equal(["L-a", "L-b", "L-c", "L-d"], report.Committed.Select(match => Assert.Single(match.Left).Context.RowKey), StringComparer.Ordinal);
        Assert.Equal(["R-a", "R-b", "R-c", "R-d"], report.Committed.Select(match => Assert.Single(match.Right).Context.RowKey), StringComparer.Ordinal);
        Assert.Equal(
            ["LeftOnly L-only", "RightOnly R-only"],
            report.Residue.Select(cluster => cluster.Alignment + " " + Assert.Single(cluster.Rows).Context.RowKey).Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    // Enough commits that the matches and the free rows are deep trees, with the input in a shuffled order.
    [Fact]
    public void Two_thousand_commits_keep_key_order_and_leave_only_the_unpaired_rows_free()
    {
        const int pairs = 2_000;
        var random = new Random(413);
        var order = Enumerable.Range(0, pairs).OrderBy(_ => random.Next()).ToArray();
        ConsolidatedRow[] left = [.. order.Select(index => Trade("L" + Name(index), Side.Left, "I" + Name(index), 1m)), Trade("L-only", Side.Left, "ZZ-left", 1m)];
        ConsolidatedRow[] right = [.. order.Reverse().Select(index => Trade("R" + Name(index), Side.Right, "I" + Name(index), 1m)), Trade("R-only", Side.Right, "ZZ-right", 1m)];

        var report = CascadeMatcher.Run(left, right, Passes, Fields, SearchLimits.Default);

        Assert.Equal(Enumerable.Range(0, pairs).Select(index => "L" + Name(index)), report.Committed.Select(match => Assert.Single(match.Left).Context.RowKey), StringComparer.Ordinal);
        Assert.Equal(Enumerable.Range(0, pairs).Select(index => "R" + Name(index)), report.Committed.Select(match => Assert.Single(match.Right).Context.RowKey), StringComparer.Ordinal);
        Assert.Equal(["L-only", "R-only"], report.Residue.Select(cluster => Assert.Single(cluster.Rows).Context.RowKey).Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    private static string Name(int index) => index.ToString("D5", CultureInfo.InvariantCulture);

    private static ConsolidatedRow Trade(string id, Side side, string instrument, decimal quantity)
    {
        var start = RowContext.Create(id, side, new Dictionary<string, object?>(StringComparer.Ordinal));
        var withParty = Symbol(start, "Counterparty", "JPM");
        var withInstrument = Symbol(withParty, "Instrument", instrument);
        var row = withInstrument.Write("Quantity", CanonicalCell.FromDecimal(quantity), "test", null, null);
        return new ConsolidatedRow([new SourceRef(side == Side.Left ? "left" : "right", id, 0)], row, false, null, null);
    }

    private static RowContext Symbol(RowContext row, string field, string value)
    {
        var (pool, id) = row.Pool.InternSymbol(value);
        return row.WithPool(pool).Write(field, CanonicalCell.FromInterned(FieldType.Symbol, id), "test", null, value);
    }
}
