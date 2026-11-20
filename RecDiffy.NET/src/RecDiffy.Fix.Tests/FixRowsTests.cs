using System.Collections.Frozen;
using RecDiffy.Results;

namespace RecDiffy.Fix.Tests;

public class FixRowsTests
{
    [Fact]
    public void A_replace_chain_points_each_row_at_the_previous_accepted_exec_id()
    {
        var rows = Read(
            Wire.Ack("0", "E1", "M1", null, "100"),
            Wire.Ack("5", "E2", "M2", "M1", "150"),
            Wire.Ack("5", "E3", "M3", "M2", "120"));

        Assert.Equal(["NEW|-|M1", "CORR|E1|M1", "CORR|E2|M1"], rows.Orders.Select(row => $"{row["Action"]}|{row["Target"] ?? "-"}|{row["Root"]}"), StringComparer.Ordinal);
        Assert.All(rows.Orders, row => Assert.Equal(("GS", "BUY"), (row["Counterparty"], row["Side"])));
    }

    [Fact]
    public void An_unsolicited_cancel_and_a_restatement_chain_on_the_same_cl_ord_id()
    {
        var rows = Read(
            Wire.Ack("0", "E1", "U1", null, "1000"),
            Wire.Ack("D", "E2", "U1", null, "500"),
            Wire.Ack("4", "E3", "U1", null, "500", "4"));

        Assert.Equal(["NEW|-", "CORR|E1", "CANC|E2"], rows.Orders.Select(row => $"{row["Action"]}|{row["Target"] ?? "-"}"), StringComparer.Ordinal);
    }

    [Fact]
    public void A_replace_of_an_unknown_order_is_rejected_with_its_reason()
    {
        var rows = Read(Wire.Ack("5", "E2", "M2", "M1", "150"));

        Assert.Empty(rows.Orders);
        Assert.Contains("not seen before: M1", Assert.Single(rows.Rejected).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Requests_rejects_pending_states_status_replies_and_session_messages_make_no_rows()
    {
        var rows = Read(
            Wire.Message("0"),
            Wire.Message("G", (11, "M2"), (41, "M1")),
            Wire.Message("9", (11, "M2"), (41, "M1"), (434, "2")),
            Wire.Ack("E", "E9", "M2", "M1", "150"),
            Wire.Ack("I", "E8", "M1", null, "100"),
            Wire.Ack("3", "E7", "M1", null, "100"));

        Assert.Empty(rows.Orders);
        Assert.Equal(6, rows.Skipped.Count);
        Assert.Empty(rows.Rejected);
    }

    [Fact]
    public void Fill_corrections_and_busts_point_at_the_execution_they_change()
    {
        var rows = Read(
            Wire.Fill("F", "F1", "C1", "100", "50.00"),
            Wire.Fill("G", "F1c", "C1", "100", "50.10", "F1"),
            Wire.Fill("H", "F1x", "C1", "100", "50.10", "F1c"));

        Assert.Equal(["NEW|-", "CORR|F1", "CANC|F1c"], rows.Fills.Select(row => $"{row["Action"]}|{row["Target"] ?? "-"}"), StringComparer.Ordinal);
    }

    [Fact]
    public void Fix_4_2_fills_use_exec_trans_type()
    {
        var fill = Wire.Message("8", "FIX.4.2", FixOptions.Soh, Wire.Broker, Wire.Us, (11, "C1"), (17, "F1"), (20, "0"), (150, "2"), (32, "100"), (31, "50"));
        var bust = Wire.Message("8", "FIX.4.2", FixOptions.Soh, Wire.Broker, Wire.Us, (11, "C1"), (17, "F2"), (20, "1"), (19, "F1"), (150, "2"), (32, "100"), (31, "50"));
        var status = Wire.Message("8", "FIX.4.2", FixOptions.Soh, Wire.Broker, Wire.Us, (11, "C1"), (17, "F3"), (20, "3"), (150, "2"));

        var rows = Read(fill, bust, status);

        Assert.Equal(["NEW|-", "CANC|F1"], rows.Fills.Select(row => $"{row["Action"]}|{row["Target"] ?? "-"}"), StringComparer.Ordinal);
        Assert.Equal("order status reply", Assert.Single(rows.Skipped).Reason);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    public void A_fill_sent_with_the_old_exec_type_in_fix_4_4_is_still_a_fill(string execType)
    {
        var rows = Read(Wire.Fill(execType, "F1", "C1", "100", "50.00"));

        Assert.Equal("NEW", Assert.Single(rows.Fills)["Action"]);
        Assert.Empty(rows.Orders);
    }

    [Theory]
    [InlineData("20261119-15:00:01","1795100401000000000")]
    [InlineData("20261119-15:00:01.123", "1795100401123000000")]
    [InlineData("20261119-15:00:01.123456789", "1795100401123456789")]
    public void Transact_time_becomes_exact_nanoseconds_since_the_epoch(string timestamp, string nanos)
    {
        var rows = Read(Wire.Message("8", (11, "C1"), (17, "F1"), (150, "F"), (32, "1"), (31, "1"), (60, timestamp)));

        Assert.Equal(nanos, Assert.Single(rows.Fills)["TransactNanos"]);
    }

    [Fact]
    public void A_malformed_transact_time_is_rejected()
    {
        var rows = Read(Wire.Message("8", (11, "C1"), (17, "F1"), (150, "F"), (60, "2026-11-19 15:00")));

        Assert.Contains("TransactTime (60)", Assert.Single(rows.Rejected).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void An_allocation_makes_one_row_per_account_and_a_replace_follows_it()
    {
        var rows = Read(
            Wire.Message("J", (70, "AL1"), (71, "0"), (6, "410.10"), (78, "2"), (79, "FUND-A"), (80, "100"), (79, "FUND-B"), (80, "50")),
            Wire.Message("J", (70, "AL2"), (71, "1"), (72, "AL1"), (6, "410.10"), (78, "1"), (79, "FUND-A"), (80, "90")));

        Assert.Equal(
            ["AL1/FUND-A|NEW|-|AL1", "AL1/FUND-B|NEW|-|AL1", "AL2/FUND-A|CORR|AL1/FUND-A|AL1"],
            rows.Allocations.Select(row => $"{row["AllocRow"]}|{row["Action"]}|{row["Target"] ?? "-"}|{row["AllocRoot"]}"), StringComparer.Ordinal);
    }

    [Fact]
    public void A_declined_trade_report_makes_no_row_and_a_reversal_cancels()
    {
        var rows = Read(
            Wire.Message("AE", (571, "TR1"), (487, "0"), (32, "20"), (31, "900")),
            Wire.Message("AE", (571, "TR1x"), (487, "4"), (572, "TR1"), (32, "20"), (31, "900")),
            Wire.Message("AE", (571, "TR2"), (487, "0"), (856, "3")));

        Assert.Equal(["NEW|-", "CANC|TR1"], rows.Trades.Select(row => $"{row["Action"]}|{row["Target"] ?? "-"}"), StringComparer.Ordinal);
        Assert.Equal("trade report declined", Assert.Single(rows.Skipped).Reason);
    }

    [Fact]
    public void A_message_between_two_other_parties_is_rejected()
    {
        var text = Wire.Message("8", "FIX.4.4", FixOptions.Soh, "MS", "JPM", (11, "C1"), (17, "E1"), (150, "0"));

        Assert.Contains("is ours", Assert.Single(Read(text).Rejected).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Flattened_orders_reconcile_with_the_sample_profile()
    {
        var ours = Read(Wire.Ack("0", "E1", "M1", null, "100"), Wire.Ack("5", "E2", "M2", "M1", "150"), Wire.Ack("0", "E5", "L1", null, "75"));
        var broker = Read(Wire.Ack("0", "E1", "M1", null, "100"), Wire.Ack("5", "E2", "M2", "M1", "150"));
        var profile = Reconciler.Compile(ProfileDocument.Parse(File.ReadAllText(Sample("orders.profile.json"))), RuleRegistry.CreateCore());

        var result = Reconciler.Run(profile, Source("left", ours.Orders), Source("right", broker.Orders), ReferenceDataSet.Empty);

        var replaced = Assert.Single(result.Entries, entry => entry.Left.Any(row => row.RowKey is "E2"));
        Assert.Equal((Alignment.Matched, FieldOutcome.Exact), (replaced.Alignment, replaced.Outcome));
        Assert.Equal(Alignment.LeftOnly, Assert.Single(result.Entries, entry => entry.Left.Any(row => row.RowKey is "E5")).Alignment);
    }

    private static FixRows Read(params string[] messages) => FixRows.Read(string.Concat(messages), Wire.Options);

    private static RecordSource Source(string side, IReadOnlyList<FrozenDictionary<string, string?>> rows) =>
        RecordSources.FromObjects(
            side,
            rows,
            rows.SelectMany(row => row.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                .Aggregate(new SourceMap<FrozenDictionary<string, string?>>(), (map, column) => map.Text(column, row => row.GetValueOrDefault(column))));

    private static string Sample(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && File.Exists(Path.Combine(dir.FullName, "RecDiffy.NET", "RecDiffy.slnx")) is false)
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("repo root"), "RecDiffy.NET", "test-data", "fix-reconciliation", file);
    }
}
