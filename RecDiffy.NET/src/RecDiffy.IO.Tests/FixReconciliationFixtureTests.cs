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
        var entry = Entry(Orders.Value, "N1-0");

        Assert.Equal((Alignment.Matched, Composition.OneToOne, FieldOutcome.Exact), (entry.Alignment, entry.Composition, entry.Outcome));
    }

    [Theory]
    [InlineData("orders", 26, 23)]
    [InlineData("fills", 25, 27)]
    [InlineData("executions", 4, 7)]
    [InlineData("allocations", 12, 10)]
    [InlineData("trades", 8, 7)]
    public void Every_fixture_row_and_no_other_reaches_the_result(string name, int left, int right)
    {
        var rows = Run(name).Rows;

        Assert.Equal(left, rows.Count(row => string.Equals(row.Row.Source, "left", StringComparison.Ordinal)));
        Assert.Equal(right, rows.Count(row => string.Equals(row.Row.Source, "right", StringComparison.Ordinal)));
    }

    [Fact]
    public void An_order_status_difference_is_a_break()
    {
        var diff = Diff(Entry(Orders.Value, "N2-0"), "OrdStatus");

        Assert.Equal(FieldOutcome.Break, diff.Outcome);
    }

    [Fact]
    public void A_rejected_replace_wrongly_written_as_a_row_shows_as_a_break()
    {
        var entry = Entry(Orders.Value, "J2-1");

        Assert.Equal(["J2-0"], Keys(entry.Right), StringComparer.Ordinal);
        Assert.Equal((FieldOutcome.Break, FieldOutcome.Break), (Diff(entry, "Quantity").Outcome, Diff(entry, "OrdStatus").Outcome));
    }

    [Fact]
    public void A_partially_filled_order_matches_at_order_level()
    {
        Assert.Equal(FieldOutcome.Exact, Entry(Orders.Value, "P1-0").Outcome);
    }

    [Fact]
    public void A_replace_mid_fill_matches_on_the_replacement_and_supersedes_the_original()
    {
        var entry = Entry(Orders.Value, "R1-1");

        Assert.Equal(["R1-1"], Keys(entry.Right), StringComparer.Ordinal);
        Assert.True(entry.Composition.HasFlag(Composition.LifecycleCollapsed));
        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
        Assert.Equal(SuppressionReason.Superseded, Row(Orders.Value, "left", "R1-0").Suppression);
        Assert.Equal(SuppressionReason.Superseded, Row(Orders.Value, "right", "R1-0").Suppression);
    }

    [Fact]
    public void A_multi_step_replace_follows_the_chain_to_its_last_version()
    {
        var entry = Entry(Orders.Value, "M1-2");

        Assert.Equal(["M1-2"], Keys(entry.Right), StringComparer.Ordinal);
        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
        Assert.All(["M1-0", "M1-1"], key => Assert.Equal(SuppressionReason.Superseded, Row(Orders.Value, "left", key).Suppression));
    }

    [Theory]
    [InlineData("J1-0")]
    [InlineData("Z1-0")]
    public void A_rejected_replace_or_cancel_never_enters_the_chain_so_the_original_matches(string execId)
    {
        var entry = Entry(Orders.Value, execId);

        Assert.Equal([execId], Keys(entry.Right), StringComparer.Ordinal);
        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
    }

    [Theory]
    [InlineData("C1-0", "C1-1")]
    [InlineData("U1-0", "U1-1")]
    [InlineData("E1-0", "E1-1")]
    public void A_cancel_unsolicited_cancel_or_expiry_removes_the_order_on_both_sides(string order, string ending)
    {
        Assert.All(
            [("left", order), ("left", ending), ("right", order), ("right", ending)],
            row => Assert.Equal(SuppressionReason.FullyCancelled, Row(Orders.Value, row.Item1, row.Item2).Suppression));
    }

    [Fact]
    public void A_cancelled_orders_fill_still_matches()
    {
        Assert.Equal(FieldOutcome.Exact, Entry(Fills.Value, "C1-F1").Outcome);
    }

    [Fact]
    public void A_restatement_matches_on_the_restated_quantity()
    {
        var entry = Entry(Orders.Value, "S1-1");

        Assert.Equal(["S1-1"], Keys(entry.Right), StringComparer.Ordinal);
        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
        Assert.Equal((500m, 500m), (Quantity(Orders.Value, entry.Left), Quantity(Orders.Value, entry.Right)));
        Assert.Equal(SuppressionReason.Superseded, Row(Orders.Value, "left", "S1-0").Suppression);
    }

    [Fact]
    public void A_restatement_one_side_missed_is_a_quantity_break()
    {
        var entry = Entry(Orders.Value, "S2-1");

        Assert.Equal(["S2-0"], Keys(entry.Right), StringComparer.Ordinal);
        Assert.Equal((FieldOutcome.Break, 500m), (Diff(entry, "Quantity").Outcome, Diff(entry, "Quantity").Delta.Number));
    }

    [Fact]
    public void An_order_the_broker_has_not_acked_yet_is_left_only()
    {
        Assert.Equal(Alignment.LeftOnly, Entry(Orders.Value, "L1-0").Alignment);
    }

    [Fact]
    public void A_replace_on_a_later_day_still_collapses_because_scope_is_the_counterparty()
    {
        var entry = Entry(Orders.Value, "20261119-G1-1");

        Assert.Equal(["20261119-G1-1"], Keys(entry.Right), StringComparer.Ordinal);
        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
    }

    [Theory]
    [InlineData("SessionDay")]
    [InlineData("Account")]
    public void A_replace_on_a_later_day_into_another_account_breaks_its_chain_under_a_narrower_scope(string scope)
    {
        var result = Run("orders", profile => profile.Replace("\"scope\": \"Counterparty\"", $"\"scope\": \"{scope}\"", StringComparison.Ordinal));
        var row = Row(result, "left", "20261119-G1-1");

        Assert.Equal(Disposition.Quarantined, row.Disposition);
        Assert.Contains(row.Diagnostics, diagnostic => string.Equals(diagnostic.Code, "MissingLifecycleTarget", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("orders", "X1-0", "X1-0")]
    [InlineData("fills", "XF-F1", "XF-F1")]
    [InlineData("executions", "XE-E", "XE-F1")]
    [InlineData("allocations", "AL8/FUND-A", "AL8/FUND-A")]
    [InlineData("trades", "TRX", "TRX")]
    public void Rows_of_different_counterparties_never_match(string name, string leftKey, string rightKey)
    {
        var result = Run(name);

        Assert.Equal(Alignment.LeftOnly, Entry(result, leftKey).Alignment);
        Assert.Equal(Alignment.RightOnly, RightEntry(result, rightKey).Alignment);
    }

    [Fact]
    public void Each_partial_fill_matches_its_drop_copy()
    {
        Assert.All(["P1-F1", "P1-F2", "P1-F3"], key => Assert.Equal(FieldOutcome.Exact, Entry(Fills.Value, key).Outcome));
    }

    [Fact]
    public void Fills_under_both_order_ids_of_a_replace_each_match()
    {
        Assert.All(["R1-F1", "R1-F2"], key => Assert.Equal(FieldOutcome.Exact, Entry(Fills.Value, key).Outcome));
        Assert.Equal(
            ["R1", "R1b"],
            new[] { "R1-F1", "R1-F2" }.Select(key => Fills.Value.Canonical(Row(Fills.Value, "left", key).Row).Text("ClOrdId")),
            StringComparer.Ordinal);
    }

    [Fact]
    public void A_busted_fill_is_removed_on_both_sides()
    {
        Assert.All(
            [("left", "B1-F1"), ("left", "B1-F1x"), ("right", "B1-F1"), ("right", "B1-F1x")],
            row => Assert.Equal(SuppressionReason.FullyCancelled, Row(Fills.Value, row.Item1, row.Item2).Suppression));
    }

    [Fact]
    public void A_fill_after_a_bust_matches_on_its_own()
    {
        Assert.Equal(SuppressionReason.FullyCancelled, Row(Fills.Value, "left", "B2-F1").Suppression);
        Assert.Equal(FieldOutcome.Exact, Entry(Fills.Value, "B2-F2").Outcome);
    }

    [Theory]
    [InlineData("K1-F1c", "K1-F1")]
    [InlineData("K2-F1cc", "K2-F1c")]
    public void A_corrected_fill_matches_on_the_last_correction(string survivor, string superseded)
    {
        var entry = Entry(Fills.Value, survivor);

        Assert.Equal([survivor], Keys(entry.Right), StringComparer.Ordinal);
        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
        Assert.Equal(SuppressionReason.Superseded, Row(Fills.Value, "left", superseded).Suppression);
    }

    [Fact]
    public void An_identical_resend_collapses_into_one_match()
    {
        var entry = Entry(Fills.Value, "D1-F1");

        Assert.Equal(["D1-F1", "D1-F1"], Keys(entry.Right), StringComparer.Ordinal);
        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
        Assert.All(
            Rows(Fills.Value, "right", "D1-F1"),
            row => Assert.Contains(row.Diagnostics, diagnostic => string.Equals(diagnostic.Code, "DuplicateRowCollapsed", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_resend_with_a_different_quantity_is_a_conflict_not_a_match()
    {
        Assert.All(
            Rows(Fills.Value, "right", "D2-F1"),
            row =>
            {
                Assert.Equal(Disposition.Quarantined, row.Disposition);
                Assert.Contains(row.Diagnostics, diagnostic => string.Equals(diagnostic.Code, "DuplicateConflict", StringComparison.Ordinal));
            });
        Assert.Equal(Alignment.LeftOnly, Entry(Fills.Value, "D2-F1").Alignment);
    }

    [Theory]
    [InlineData("T3-F1", FieldOutcome.WithinTolerance, 500_000_000)]
    [InlineData("T4-F1", FieldOutcome.Break, 500_000_001)]
    [InlineData("T5-F1", FieldOutcome.WithinTolerance, 1)]
    public void The_tolerance_is_inclusive_and_exact_to_the_nanosecond(string execId, FieldOutcome outcome, long delta)
    {
        var diff = Diff(Entry(Fills.Value, execId), "TransactNanos");

        Assert.Equal((outcome, (decimal)delta), (diff.Outcome, diff.Delta.Number));
    }

    [Fact]
    public void A_quantity_with_the_opposite_sign_is_a_break()
    {
        var diff = Diff(Entry(Fills.Value, "SG1-F1"), "Quantity");

        Assert.Equal((FieldOutcome.Break, -200m), (diff.Outcome, diff.Delta.Number));
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
        Assert.Equal(["S1-F1"], Keys(entry.Right), StringComparer.Ordinal);
    }

    [Fact]
    public void A_booked_execution_that_does_not_sum_to_its_fills_is_ambiguous()
    {
        var entry = Entry(Executions.Value, "Q1-E");

        Assert.Equal(Alignment.Ambiguous, entry.Alignment);
        Assert.Equal(["Q1-F1", "Q1-F2"], Keys(entry.Right), StringComparer.Ordinal);
        Assert.Equal((250m, 300m), (Quantity(Executions.Value, entry.Left), Quantity(Executions.Value, entry.Right)));
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
    public void A_cancelled_allocation_is_removed_on_both_sides()
    {
        Assert.All(
            [("left", "AL4/FUND-B"), ("left", "AL4x/FUND-B"), ("right", "AL4/FUND-B"), ("right", "AL4x/FUND-B")],
            row => Assert.Equal(SuppressionReason.FullyCancelled, Row(Allocations.Value, row.Item1, row.Item2).Suppression));
    }

    [Fact]
    public void An_allocation_quantity_difference_is_a_break()
    {
        var diff = Diff(Entry(Allocations.Value, "AL5/FUND-C"), "Quantity");

        Assert.Equal((FieldOutcome.Break, -10m), (diff.Outcome, diff.Delta.Number));
    }

    [Fact]
    public void An_allocation_the_broker_only_received_is_still_open()
    {
        Assert.Equal(Alignment.LeftOnly, Entry(Allocations.Value, "AL6/FUND-A").Alignment);
    }

    [Fact]
    public void An_account_level_reject_leaves_only_the_rejected_account_open()
    {
        Assert.Equal(FieldOutcome.Exact, Entry(Allocations.Value, "AL7/FUND-A").Outcome);
        Assert.Equal(Alignment.LeftOnly, Entry(Allocations.Value, "AL7/FUND-B").Alignment);
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

    [Fact]
    public void A_reversed_trade_report_is_removed_on_both_sides()
    {
        Assert.All(
            [("left", "TR4"), ("left", "TR4x"), ("right", "TR4"), ("right", "TR4x")],
            row => Assert.Equal(SuppressionReason.FullyCancelled, Row(Trades.Value, row.Item1, row.Item2).Suppression));
    }

    [Fact]
    public void A_trade_report_the_counterparty_declined_is_left_only()
    {
        Assert.Equal(Alignment.LeftOnly, Entry(Trades.Value, "TR5").Alignment);
    }

    private static IReconciliationResult Run(string name) => Run(name, profile => profile);

    private static IReconciliationResult Run(string name, Func<string, string> editProfile)
    {
        var folder = Path.Combine(TestFiles.TestData, "fix-reconciliation");
        var json = editProfile(File.ReadAllText(Path.Combine(folder, $"{name}.profile.json")));
        var profile = Reconciler.Compile(ProfileDocument.Parse(json), RuleRegistry.CreateCore());
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

    private static IEnumerable<RowResult> Rows(IReconciliationResult result, string side, string key) =>
        result.Rows.Where(row => string.Equals(row.Row.Source, side, StringComparison.Ordinal) && string.Equals(row.Row.RowKey, key, StringComparison.Ordinal));

    private static decimal Quantity(IReconciliationResult result, IEnumerable<SourceRef> rows) =>
        rows.Sum(row => result.Canonical(row)["Quantity"].Number);

    private static FieldDiff Diff(ResultEntry entry, string field) =>
        Assert.Single(entry.Diffs, diff => string.Equals(diff.Field.Name, field, StringComparison.Ordinal));

    private static IEnumerable<string> Keys(IEnumerable<SourceRef> rows) => rows.Select(row => row.RowKey).Order(StringComparer.Ordinal);
}
