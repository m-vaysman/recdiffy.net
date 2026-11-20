using RecDiffy.Results;

namespace RecDiffy.IO.Tests;

public class FixReconciliationFixtureTests
{
    private static readonly CsvOptions Csv = new() { HasHeader = true, Delimiter = CsvDelimiter.Comma };

    private static readonly Lazy<IReconciliationResult> Orders = new(() => Run("orders"));

    private static readonly Lazy<IReconciliationResult> Fills = new(() => Run("fills"));

    private static readonly Lazy<IReconciliationResult> Executions = new(() => Run("executions"));

    private static readonly Lazy<IReconciliationResult> Allocations = new(() => Run("allocations"));

    private static readonly Lazy<IReconciliationResult> Trades = new(() => Run("trades"));

    [Fact]
    public void A_new_order_matches_its_ack()
    {
        var entry = Entry(Orders.Value, "N1");

        Assert.Equal((Alignment.Matched, Composition.OneToOne, FieldOutcome.Exact), (entry.Alignment, entry.Composition, entry.Outcome));
    }

    [Fact]
    public void A_partially_filled_order_matches_at_order_level()
    {
        Assert.Equal(FieldOutcome.Exact, Entry(Orders.Value, "P1").Outcome);
    }

    [Fact]
    public void A_replace_mid_fill_matches_on_the_replacement_and_supersedes_the_original()
    {
        var entry = Entry(Orders.Value, "R1b");

        Assert.Equal(["R1b"], Keys(entry.Right), StringComparer.Ordinal);
        Assert.True(entry.Composition.HasFlag(Composition.LifecycleCollapsed));
        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
        Assert.Equal(SuppressionReason.Superseded, Row(Orders.Value, "left", "R1").Suppression);
        Assert.Equal(SuppressionReason.Superseded, Row(Orders.Value, "right", "R1").Suppression);
    }

    [Fact]
    public void A_multi_step_replace_follows_the_chain_to_its_last_version()
    {
        var entry = Entry(Orders.Value, "M3");

        Assert.Equal(["M3"], Keys(entry.Right), StringComparer.Ordinal);
        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
        Assert.All(["M1", "M2"], key => Assert.Equal(SuppressionReason.Superseded, Row(Orders.Value, "left", key).Suppression));
    }

    [Fact]
    public void A_rejected_replace_never_enters_the_chain_so_the_original_matches()
    {
        var entry = Entry(Orders.Value, "J1");

        Assert.Equal(["J1"], Keys(entry.Right), StringComparer.Ordinal);
        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
    }

    [Fact]
    public void A_cancel_after_a_partial_fill_removes_the_order_but_its_fill_still_matches()
    {
        Assert.All(["C1", "C1x"], key => Assert.Equal(SuppressionReason.FullyCancelled, Row(Orders.Value, "left", key).Suppression));
        Assert.Equal(FieldOutcome.Exact, Entry(Fills.Value, "C1-F1").Outcome);
    }

    [Fact]
    public void An_order_the_broker_has_not_acked_yet_is_left_only()
    {
        Assert.Equal(Alignment.LeftOnly, Entry(Orders.Value, "L1").Alignment);
    }

    [Fact]
    public void A_replace_on_a_later_day_still_collapses_because_scope_is_the_counterparty()
    {
        var entry = Entry(Orders.Value, "G1b");

        Assert.Equal(["G1b"], Keys(entry.Right), StringComparer.Ordinal);
        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
    }

    [Fact]
    public void Rows_of_different_counterparties_never_match()
    {
        Assert.Equal(Alignment.LeftOnly, Entry(Orders.Value, "X1").Alignment);
        Assert.Equal(Alignment.RightOnly, RightEntry(Orders.Value, "X1").Alignment);
    }

    [Fact]
    public void Each_partial_fill_matches_its_drop_copy()
    {
        Assert.All(["P1-F1", "P1-F2", "P1-F3"], key => Assert.Equal(FieldOutcome.Exact, Entry(Fills.Value, key).Outcome));
    }

    [Fact]
    public void One_booked_execution_matches_the_brokers_partial_fills_by_quantity()
    {
        var entry = Entry(Executions.Value, "P1-E");

        Assert.Equal((Alignment.Matched, FieldOutcome.Exact), (entry.Alignment, entry.Outcome));
        Assert.True(entry.Composition.HasFlag(Composition.OneToMany));
        Assert.Equal(["P1-F1", "P1-F2", "P1-F3"], Keys(entry.Right), StringComparer.Ordinal);
    }

    [Fact]
    public void A_booked_execution_with_a_single_fill_matches_one_to_one()
    {
        var entry = Entry(Executions.Value, "S1-E");

        Assert.Equal((Composition.OneToOne, FieldOutcome.Exact), (entry.Composition, entry.Outcome));
    }

    [Fact]
    public void A_booked_execution_that_does_not_sum_to_its_fills_is_ambiguous()
    {
        var entry = Entry(Executions.Value, "Q1-E");

        Assert.Equal(Alignment.Ambiguous, entry.Alignment);
        Assert.Equal(["Q1-F1", "Q1-F2"], Keys(entry.Right), StringComparer.Ordinal);
    }

    [Fact]
    public void Fills_before_and_after_a_replace_each_match()
    {
        Assert.All(["R1-F1", "R1-F2"], key => Assert.Equal(FieldOutcome.Exact, Entry(Fills.Value, key).Outcome));
    }

    [Fact]
    public void A_busted_fill_is_removed_on_both_sides()
    {
        Assert.All(
            [("left", "B1-F1"), ("left", "B1-F1x"), ("right", "B1-F1"), ("right", "B1-F1x")],
            row => Assert.Equal(SuppressionReason.FullyCancelled, Row(Fills.Value, row.Item1, row.Item2).Suppression));
    }

    [Fact]
    public void A_corrected_fill_matches_on_the_correction()
    {
        var entry = Entry(Fills.Value, "K1-F1c");

        Assert.Equal(["K1-F1c"], Keys(entry.Right), StringComparer.Ordinal);
        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
        Assert.Equal(SuppressionReason.Superseded, Row(Fills.Value, "left", "K1-F1").Suppression);
    }

    [Fact]
    public void An_identical_resend_collapses_into_one_match()
    {
        var entry = Entry(Fills.Value, "D1-F1");

        Assert.Equal(["D1-F1", "D1-F1"], Keys(entry.Right), StringComparer.Ordinal);
        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
    }

    [Fact]
    public void Timestamps_as_nanoseconds_within_half_a_second_are_within_tolerance()
    {
        var diff = Diff(Entry(Fills.Value, "T1-F1"), "TransactNanos");

        Assert.Equal((FieldOutcome.WithinTolerance, 300_000_000m), (diff.Outcome, diff.Delta.Number));
    }

    [Fact]
    public void Timestamps_as_nanoseconds_two_seconds_apart_are_a_break()
    {
        var diff = Diff(Entry(Fills.Value, "T2-F1"), "TransactNanos");

        Assert.Equal((FieldOutcome.Break, 2_000_000_000m), (diff.Outcome, diff.Delta.Number));
    }

    [Fact]
    public void An_allocation_split_across_accounts_matches_account_by_account()
    {
        Assert.All(["AL1/FUND-A", "AL1/FUND-B", "AL1/FUND-C"], key => Assert.Equal(FieldOutcome.Exact, Entry(Allocations.Value, key).Outcome));
    }

    [Fact]
    public void A_replaced_allocation_matches_on_the_replacement()
    {
        var entry = Entry(Allocations.Value, "AL3/FUND-A");

        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
        Assert.Equal(SuppressionReason.Superseded, Row(Allocations.Value, "left", "AL2/FUND-A").Suppression);
    }

    [Fact]
    public void A_cancelled_allocation_is_removed()
    {
        Assert.All(["AL4/FUND-B", "AL4x/FUND-B"], key => Assert.Equal(SuppressionReason.FullyCancelled, Row(Allocations.Value, "right", key).Suppression));
    }

    [Fact]
    public void An_allocation_quantity_difference_is_a_break()
    {
        var diff = Diff(Entry(Allocations.Value, "AL5/FUND-C"), "Quantity");

        Assert.Equal((FieldOutcome.Break, -10m), (diff.Outcome, diff.Delta.Number));
    }

    [Fact]
    public void A_trade_report_matches_and_a_replaced_one_matches_on_the_replacement()
    {
        Assert.Equal(FieldOutcome.Exact, Entry(Trades.Value, "TR1").Outcome);
        Assert.Equal(FieldOutcome.Exact, Entry(Trades.Value, "TR2r").Outcome);
        Assert.Equal(SuppressionReason.Superseded, Row(Trades.Value, "right", "TR2").Suppression);
    }

    [Fact]
    public void A_trade_report_price_difference_is_a_break()
    {
        var diff = Diff(Entry(Trades.Value, "TR3"), "Price");

        Assert.Equal((FieldOutcome.Break, 0.05m), (diff.Outcome, diff.Delta.Number));
    }

    private static IReconciliationResult Run(string name)
    {
        var folder = Path.Combine(TestFiles.TestData, "fix-reconciliation");
        var profile = Reconciler.Compile(ProfileDocument.Parse(File.ReadAllText(Path.Combine(folder, $"{name}.profile.json"))), RuleRegistry.CreateCore());
        return Reconciler.Run(
            profile,
            FileSources.FromCsv("left", Path.Combine(folder, $"{name}-left.csv"), Csv),
            FileSources.FromCsv("right", Path.Combine(folder, $"{name}-right.csv"), Csv),
            ReferenceDataSet.Empty);
    }

    private static ResultEntry Entry(IReconciliationResult result, string leftKey) =>
        Assert.Single(result.Entries, entry => entry.Left.Any(row => string.Equals(row.RowKey, leftKey, StringComparison.Ordinal)));

    private static ResultEntry RightEntry(IReconciliationResult result, string rightKey) =>
        Assert.Single(result.Entries, entry => entry.Right.Any(row => string.Equals(row.RowKey, rightKey, StringComparison.Ordinal)));

    private static RowResult Row(IReconciliationResult result, string side, string key) =>
        Assert.Single(result.Rows, row => string.Equals(row.Row.Source, side, StringComparison.Ordinal) && string.Equals(row.Row.RowKey, key, StringComparison.Ordinal));

    private static FieldDiff Diff(ResultEntry entry, string field) =>
        Assert.Single(entry.Diffs, diff => string.Equals(diff.Field.Name, field, StringComparison.Ordinal));

    private static IEnumerable<string> Keys(IEnumerable<SourceRef> rows) => rows.Select(row => row.RowKey).Order(StringComparer.Ordinal);
}
