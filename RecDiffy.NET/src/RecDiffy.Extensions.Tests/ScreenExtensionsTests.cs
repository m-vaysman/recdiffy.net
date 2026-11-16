using System.Data;

namespace RecDiffy.Extensions.Tests;

public sealed record PipeRow(string A, string B, int Qty, decimal Value);

public sealed class ScreenExtensionsTests
{
    private static RecResult<PipeRow, PipeRow> PipeRun()
    {
        PipeRow[] left = [new("x|y", "b", 5, 10m), new("z", "b", 2, 4m)];
        PipeRow[] right = [new("x|y", "b", 5, 11m), new("z", "b", 2, 4m)];

        return RecBuilder.Between(left, right)
            .MatchOn(l => l.A, r => r.A)
                .And(l => l.B, r => r.B)
            .Sum(l => l.Qty, r => r.Qty)
            .Compare(l => l.Value, r => r.Value)
            .Run();
    }

    [Fact]
    public void Match_is_every_row_without_the_group_and_with_diff()
    {
        var result = Samples.OmsBroker();

        using var reconciliation = result.ToReconciliationTable();
        using var match = result.ToMatchTable();

        Assert.Equal("Match", match.TableName);
        Assert.Equal(
            [.. TableRead.ColumnNames(reconciliation).Where(name => name != "Group").Select(name => name == "Diffs" ? "Diff" : name)],
            TableRead.ColumnNames(match));
        Assert.Equal(reconciliation.Rows.Count, match.Rows.Count);
        Assert.Equal("Cash 1128560.00 → 1128534.70", match.Rows[4]["Diff"]);
    }

    [Fact]
    public void Breaks_by_side_show_quantity_on_each_side_and_the_value_diff()
    {
        using var table = Samples.OmsBroker().ToBreakTable(["Side"], "Qty", "Cash");

        Assert.Equal("Breaks", table.TableName);
        Assert.Equal(["Side", "Qty left", "Qty right", "Qty diff", "Cash diff"], TableRead.ColumnNames(table));
        Assert.Equal(["BUY", "SELL"], TableRead.Strings(table, "Side"));
        Assert.Equal([15400m, 14000m], TableRead.Column(table, "Qty left").Cast<decimal>());
        Assert.Equal([13600m, 14000m], TableRead.Column(table, "Qty right").Cast<decimal>());
        Assert.Equal([-1800m, 0m], TableRead.Column(table, "Qty diff").Cast<decimal>());
        Assert.Equal([263385.00m, 274.70m], TableRead.Column(table, "Cash diff").Cast<decimal>());
    }

    [Fact]
    public void Breaks_leave_ambiguous_rows_out()
    {
        using var table = Samples.IborAbor().ToBreakTable(["Fund"], "Qty", "MarketValue");

        // INCOME holds an ambiguous AAPL position of 1,000 against two lots of 600. Without it both sides hold 10,000.
        Assert.Equal(["GROWTH", "INCOME"], TableRead.Strings(table, "Fund"));
        Assert.Equal([16200m, 10000m], TableRead.Column(table, "Qty left").Cast<decimal>());
        Assert.Equal([15500m, 10000m], TableRead.Column(table, "Qty right").Cast<decimal>());
    }

    [Fact]
    public void Breaks_by_two_fields_put_each_key_in_its_own_column()
    {
        using var table = Samples.IborAbor().ToBreakTable(["Fund", "Isin"], "Qty", "MarketValue");

        Assert.Equal(["Fund", "Isin", "Qty left", "Qty right", "Qty diff", "MarketValue diff"], TableRead.ColumnNames(table));
        var aapl = table.Rows.Cast<DataRow>().Single(row => (string)row["Fund"] == "GROWTH" && (string)row["Isin"] == "US0378331005");
        Assert.Equal(5000m, aapl["Qty left"]);
        Assert.Equal(5000m, aapl["Qty right"]);
        Assert.DoesNotContain(table.Rows.Cast<DataRow>(), row => (string)row["Fund"] == "INCOME" && (string)row["Isin"] == "US0378331005");
    }

    [Fact]
    public void Control_totals_by_fund_end_with_a_total_row()
    {
        using var table = Samples.IborAbor().ToControlTotalsTable(["Fund"], "Qty");

        Assert.Equal("ControlTotals", table.TableName);
        Assert.Equal(["Fund", "Left", "Right", "Diff", "Left only", "Right only", "Matched diff"], TableRead.ColumnNames(table));
        Assert.Equal(["GROWTH", "INCOME", "Total"], TableRead.Strings(table, "Fund"));
        Assert.Equal([16200m, 10000m, 26200m], TableRead.Column(table, "Left").Cast<decimal>());
        Assert.Equal([15500m, 10000m, 25500m], TableRead.Column(table, "Right").Cast<decimal>());
        Assert.Equal([-700m, 0m, -700m], TableRead.Column(table, "Diff").Cast<decimal>());
        Assert.Equal([-1200m, 0m, -1200m], TableRead.Column(table, "Left only").Cast<decimal>());
        Assert.Equal([1500m, 0m, 1500m], TableRead.Column(table, "Right only").Cast<decimal>());
        Assert.Equal([-1000m, 0m, -1000m], TableRead.Column(table, "Matched diff").Cast<decimal>());
    }

    [Fact]
    public void The_total_row_fills_only_the_first_key()
    {
        using var table = Samples.IborAbor().ToControlTotalsTable(["Fund", "Isin"], "Qty");
        var total = table.Rows[table.Rows.Count - 1];

        Assert.Equal("Total", total["Fund"]);
        Assert.Equal("", total["Isin"]);
        Assert.Equal(26200m, total["Left"]);
    }

    [Fact]
    public void One_key_that_contains_a_pipe_stays_whole()
    {
        using var table = PipeRun().ToBreakTable(["A"], "Qty", "Value");

        Assert.Equal(["x|y", "z"], TableRead.Strings(table, "A"));
        Assert.Equal([1m, 0m], TableRead.Column(table, "Value diff").Cast<decimal>());
    }

    [Fact]
    public void Several_keys_one_of_which_contains_a_pipe_are_refused_rather_than_mislabelled()
    {
        var result = PipeRun();

        Assert.Throws<InvalidOperationException>(() => result.ToBreakTable(["A", "B"], "Qty", "Value").Dispose());
        Assert.Throws<InvalidOperationException>(() => result.ToControlTotalsTable(["A", "B"], "Qty").Dispose());
    }

    [Fact]
    public void Each_html_screen_is_a_grid_of_its_table()
    {
        var result = Samples.IborAbor();

        using var match = result.ToMatchTable();
        using var breaks = result.ToBreakTable(["Fund"], "Qty", "MarketValue");
        using var totals = result.ToControlTotalsTable(["Fund"], "Qty");

        Assert.Equal(match.ToDataGridHtml("Match"), result.ToMatchHtml());
        Assert.Equal(breaks.ToDataGridHtml("Breaks"), result.ToBreakHtml(["Fund"], "Qty", "MarketValue"));
        Assert.Equal(totals.ToDataGridHtml("ControlTotals"), result.ToControlTotalsHtml(["Fund"], "Qty"));
        Assert.StartsWith("<table class=\"grid\"><caption>Match</caption>", result.ToMatchHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void Bad_arguments_are_rejected()
    {
        var result = Samples.IborAbor();
        RecResult<IborPosition, AborLot> none = null!;

        Assert.Throws<ArgumentNullException>(() => none.ToMatchTable().Dispose());
        Assert.Throws<ArgumentNullException>(() => none.ToBreakTable(["Fund"], "Qty", "MarketValue").Dispose());
        Assert.Throws<ArgumentNullException>(() => none.ToControlTotalsTable(["Fund"], "Qty").Dispose());
        Assert.Throws<ArgumentNullException>(() => result.ToBreakTable(null!, "Qty", "MarketValue").Dispose());
        Assert.Throws<ArgumentException>(() => result.ToBreakTable(["Fund"], "Qty", "Qty").Dispose());
        Assert.Throws<ArgumentException>(() => result.ToBreakTable(["Fund"], " ", "MarketValue").Dispose());
        Assert.Throws<ArgumentException>(() => result.ToControlTotalsTable([], "Qty").Dispose());
    }
}
