using RecDiffy.Results;

namespace RecDiffy.Sessions.Tests;

public class RecSessionTests
{
    private const int Patterns = 100;

    public static TheoryData<string> Names => [.. Fixtures.Names];

    [Theory]
    [MemberData(nameof(Names))]
    public void One_push_of_every_row_equals_a_batch_run(string name)
    {
        var (view, _) = RecSession.Start(Fixtures.Profile(name)).Push(Fixtures.Rows(name, "left"), Fixtures.Rows(name, "right")).Snapshot();

        Assert.Equal(Fixtures.Fingerprint(Fixtures.Batch(name)), Fixtures.Fingerprint(view.Result));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Pushes_in_random_chunks_with_snapshots_between_equal_a_batch_run(string name)
    {
        var expected = Fixtures.Fingerprint(Fixtures.Batch(name));
        var left = Fixtures.Rows(name, "left");
        var right = Fixtures.Rows(name, "right");

        Assert.All(Enumerable.Range(0, Patterns), seed =>
        {
            var random = new Random(seed);
            var pushes = Chunks(left, random).ZipLongest(Chunks(right, random));
            var final = pushes.Aggregate(
                RecSession.Start(Fixtures.Profile(name)),
                (session, push) => random.Next(2) is 0 ? session.Push(push.Left, push.Right) : session.Push(push.Left, push.Right).Snapshot().Session);

            Assert.Equal(expected, Fixtures.Fingerprint(final.Snapshot().View.Result));
        });
    }

    [Fact]
    public void A_snapshot_with_nothing_pushed_since_returns_the_same_view()
    {
        var (first, session) = Started("orders").Snapshot();
        var (second, again) = session.Snapshot();

        Assert.Same(first, second);
        Assert.Same(session, again);
        Assert.True(session.IsCurrent);
    }

    [Fact]
    public void A_push_returns_a_new_session_and_leaves_the_old_one_as_it_was()
    {
        var (_, current) = Started("orders").Snapshot();
        var pushed = current.Push([], []);

        Assert.Equal((1, true), (current.Version, current.IsCurrent));
        Assert.Equal((2, false), (pushed.Version, pushed.IsCurrent));
    }

    [Fact]
    public void The_view_carries_the_breaks_of_its_result()
    {
        var (view, _) = Started("orders").Snapshot();

        Assert.Equal(["J2-1", "N2-0", "S2-1"], view.Breaks.SelectMany(entry => entry.Left).Select(row => row.RowKey).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal(1, view.Version);
    }

    [Fact]
    public void Leftovers_carry_only_open_rows_and_open_chains_whole()
    {
        var left = Fixtures.Rows("orders", "left");
        var right = Fixtures.Rows("orders", "right").Where(row => row["Root"] is not "R1").ToArray();
        var (day1, _) = RecSession.Start(Fixtures.Profile("orders")).Push(left, right).Snapshot();

        var carried = RecSession.Start(Fixtures.Profile("orders")).Push(day1.Result);

        Assert.Equal(["L1-0", "R1-0", "R1-1", "X1-0"], carried.Left.Select(row => row.Cells["ExecID"]).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal(["X1-0"], carried.Right.Select(row => row.Cells["ExecID"]), StringComparer.Ordinal);
    }

    [Fact]
    public void A_carried_chain_matches_when_the_other_side_arrives()
    {
        var left = Fixtures.Rows("orders", "left");
        var right = Fixtures.Rows("orders", "right");
        var (day1, _) = RecSession.Start(Fixtures.Profile("orders")).Push(left, right.Where(row => row["Root"] is not "R1")).Snapshot();

        var (day2, _) = RecSession.Start(Fixtures.Profile("orders")).Push(day1.Result).Push([], right.Where(row => row["Root"] is "R1")).Snapshot();

        var entry = Assert.Single(day2.Result.Entries, entry => entry.Left.Any(row => row.RowKey is "R1-1"));
        Assert.Equal((Alignment.Matched, FieldOutcome.Exact), (entry.Alignment, entry.Outcome));
        Assert.DoesNotContain(day2.Result.Rows, row => row.Disposition is Disposition.Quarantined);
    }

    [Fact]
    public void Carried_rows_keep_the_cells_the_profile_reads()
    {
        var (day1, _) = Started("orders").Snapshot();
        var original = Fixtures.Rows("orders", "left").Single(row => row["ExecID"] is "L1-0");

        var carried = Assert.Single(RecSession.Start(Fixtures.Profile("orders")).Push(day1.Result).Left, row => row.Cells["ExecID"] is "L1-0");

        Assert.All(carried.Cells, cell => Assert.Equal(original[cell.Key], cell.Value));
        Assert.Equal(["Account", "Action", "ClOrdID", "Counterparty", "ExecID", "ExecType", "OrdStatus", "OrderQty", "Price", "Root", "SessionDay", "Side", "Symbol"], carried.Cells.Keys, StringComparer.Ordinal);
    }

    private static RecSession Started(string name) =>
        RecSession.Start(Fixtures.Profile(name)).Push(Fixtures.Rows(name, "left"), Fixtures.Rows(name, "right"));

    private static IReadOnlyList<IReadOnlyList<IReadOnlyDictionary<string, string?>>> Chunks(IReadOnlyList<IReadOnlyDictionary<string, string?>> rows, Random random)
    {
        var cuts = Enumerable.Range(1, Math.Max(0, rows.Count - 1)).Where(_ => random.Next(3) is 0).ToArray();
        var bounds = (int[])[0, .. cuts, rows.Count];
        return [.. bounds.Zip(bounds.Skip(1)).Select(span => rows.Skip(span.First).Take(span.Second - span.First).ToArray())];
    }
}

internal static class ChunkPairs
{
    public static IReadOnlyList<(IReadOnlyList<IReadOnlyDictionary<string, string?>> Left, IReadOnlyList<IReadOnlyDictionary<string, string?>> Right)> ZipLongest(
        this IReadOnlyList<IReadOnlyList<IReadOnlyDictionary<string, string?>>> left,
        IReadOnlyList<IReadOnlyList<IReadOnlyDictionary<string, string?>>> right) =>
        [
            .. Enumerable.Range(0, Math.Max(left.Count, right.Count)).Select(index => (
                index < left.Count ? left[index] : (IReadOnlyList<IReadOnlyDictionary<string, string?>>)[],
                index < right.Count ? right[index] : (IReadOnlyList<IReadOnlyDictionary<string, string?>>)[])),
        ];
}
