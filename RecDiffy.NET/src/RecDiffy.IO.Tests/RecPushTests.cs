using RecDiffy.Results;

namespace RecDiffy.IO.Tests;

public class RecPushTests
{
    private const int Patterns = 100;

    public static TheoryData<string> Names => [.. RecPushFixtures.Names];

    [Theory]
    [MemberData(nameof(Names))]
    public void One_push_of_every_row_equals_a_batch_run(string name)
    {
        var (view, _) = Pushed(name).Snapshot();

        Assert.Equal(RecPushFixtures.Fingerprint(RecPushFixtures.Batch(name)), RecPushFixtures.Fingerprint(view.Result));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Pushes_in_random_chunks_with_snapshots_between_equal_a_batch_run(string name)
    {
        var expected = RecPushFixtures.Fingerprint(RecPushFixtures.Batch(name));
        var profile = RecPushFixtures.Profile(name);
        var left = RecPushFixtures.Rows(name, "left");
        var right = RecPushFixtures.Rows(name, "right");

        Assert.All(Enumerable.Range(0, Patterns), seed =>
        {
            var random = new Random(seed);
            var pushes = Pairs(Chunks(left, random), Chunks(right, random));
            var final = pushes.Aggregate(
                profile.Push([], []),
                (batch, push) => random.Next(2) is 0 ? batch.Push(push.Left, push.Right) : batch.Push(push.Left, push.Right).Snapshot().Batch);

            Assert.Equal(expected, RecPushFixtures.Fingerprint(final.Snapshot().View.Result));
        });
    }

    [Fact]
    public void A_snapshot_with_nothing_pushed_since_returns_the_same_view()
    {
        var (first, batch) = Pushed("orders").Snapshot();
        var (second, again) = batch.Snapshot();

        Assert.Same(first, second);
        Assert.Same(batch, again);
        Assert.True(batch.IsCurrent);
    }

    [Fact]
    public void A_push_returns_a_new_batch_and_leaves_the_old_one_as_it_was()
    {
        var (_, current) = Pushed("orders").Snapshot();
        var pushed = current.Push([], []);

        Assert.Equal((1, true), (current.Version, current.IsCurrent));
        Assert.Equal((2, false), (pushed.Version, pushed.IsCurrent));
    }

    [Fact]
    public void The_view_carries_the_breaks_of_its_result()
    {
        var (view, _) = Pushed("orders").Snapshot();

        Assert.Equal(["J2-1", "N2-0", "S2-1"], view.Breaks.SelectMany(entry => entry.Left).Select(row => row.RowKey).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal(1, view.Version);
    }

    [Fact]
    public void Leftovers_carry_only_open_rows_and_open_chains_whole()
    {
        var profile = RecPushFixtures.Profile("orders");
        var right = RecPushFixtures.Rows("orders", "right").Where(row => row["Root"] is not "R1").ToArray();
        var (day1, _) = profile.Push(RecPushFixtures.Rows("orders", "left"), right).Snapshot();

        var carried = profile.Push(day1.Result);

        Assert.Equal(["L1-0", "R1-0", "R1-1", "X1-0"], carried.Left.Select(row => row.Cells["ExecID"]).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal(["X1-0"], carried.Right.Select(row => row.Cells["ExecID"]), StringComparer.Ordinal);
    }

    [Fact]
    public void A_carried_chain_matches_when_the_other_side_arrives()
    {
        var profile = RecPushFixtures.Profile("orders");
        var right = RecPushFixtures.Rows("orders", "right");
        var (day1, _) = profile.Push(RecPushFixtures.Rows("orders", "left"), right.Where(row => row["Root"] is not "R1")).Snapshot();

        var (day2, _) = profile.Push(day1.Result).Push([], right.Where(row => row["Root"] is "R1")).Snapshot();

        var entry = Assert.Single(day2.Result.Entries, entry => entry.Left.Any(row => row.RowKey is "R1-1"));
        Assert.Equal((Alignment.Matched, FieldOutcome.Exact), (entry.Alignment, entry.Outcome));
        Assert.DoesNotContain(day2.Result.Rows, row => row.Disposition is Disposition.Quarantined);
    }

    [Fact]
    public void Carried_rows_keep_the_cells_the_profile_reads()
    {
        var (day1, _) = Pushed("orders").Snapshot();
        var original = RecPushFixtures.Rows("orders", "left").Single(row => row["ExecID"] is "L1-0");

        var carried = Assert.Single(RecPushFixtures.Profile("orders").Push(day1.Result).Left, row => row.Cells["ExecID"] is "L1-0");

        Assert.All(carried.Cells, cell => Assert.Equal(original[cell.Key], cell.Value));
        Assert.Equal(["Account", "Action", "ClOrdID", "Counterparty", "ExecID", "ExecType", "OrdStatus", "OrderQty", "Price", "Root", "SessionDay", "Side", "Symbol"], carried.Cells.Keys, StringComparer.Ordinal);
    }

    private static RecBatch Pushed(string name) =>
        RecPushFixtures.Profile(name).Push(RecPushFixtures.Rows(name, "left"), RecPushFixtures.Rows(name, "right"));

    private static IReadOnlyList<IReadOnlyList<IReadOnlyDictionary<string, string?>>> Chunks(IReadOnlyList<IReadOnlyDictionary<string, string?>> rows, Random random)
    {
        var cuts = Enumerable.Range(1, Math.Max(0, rows.Count - 1)).Where(_ => random.Next(3) is 0).ToArray();
        var bounds = (int[])[0, .. cuts, rows.Count];
        return [.. bounds.Zip(bounds.Skip(1)).Select(span => rows.Skip(span.First).Take(span.Second - span.First).ToArray())];
    }

    private static IReadOnlyList<(IReadOnlyList<IReadOnlyDictionary<string, string?>> Left, IReadOnlyList<IReadOnlyDictionary<string, string?>> Right)> Pairs(
        IReadOnlyList<IReadOnlyList<IReadOnlyDictionary<string, string?>>> left,
        IReadOnlyList<IReadOnlyList<IReadOnlyDictionary<string, string?>>> right) =>
        [
            .. Enumerable.Range(0, Math.Max(left.Count, right.Count)).Select(index => (
                index < left.Count ? left[index] : (IReadOnlyList<IReadOnlyDictionary<string, string?>>)[],
                index < right.Count ? right[index] : (IReadOnlyList<IReadOnlyDictionary<string, string?>>)[])),
        ];
}
