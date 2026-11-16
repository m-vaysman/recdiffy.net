using System.Collections.Immutable;
using System.Data;
using RecDiffy.Results;

namespace RecDiffy.Extensions.Tests;

public sealed class SideBySideTableTests
{
    private static readonly SideBySideSpec QtyAndCashBySide = new(["Side"], [], [new MeasureSpec("Qty"), new MeasureSpec("Cash")], null);

    [Fact]
    public void Columns_are_the_row_the_column_and_six_decimals_per_measure_in_name_order()
    {
        using var table = Samples.OmsBroker().ToSideBySideTable(QtyAndCashBySide);

        Assert.Equal(
            [
                "Row", "Column",
                "Cash.Left", "Cash.Right", "Cash.Delta", "Cash.LeftOnly", "Cash.RightOnly", "Cash.MatchedDelta",
                "Qty.Left", "Qty.Right", "Qty.Delta", "Qty.LeftOnly", "Qty.RightOnly", "Qty.MatchedDelta",
            ],
            TableRead.ColumnNames(table));
        Assert.Equal(typeof(decimal), table.Columns["Qty.Delta"]!.DataType);
    }

    [Fact]
    public void Oms_sample_totals_by_side()
    {
        using var table = Samples.OmsBroker().ToSideBySideTable(QtyAndCashBySide);

        Assert.Equal(["BUY", "SELL"], TableRead.Strings(table, "Row"));
        Assert.Equal(["All", "All"], TableRead.Strings(table, "Column"));
        Assert.Equal([15400m, 14000m], TableRead.Column(table, "Qty.Left").Cast<decimal>());
        Assert.Equal([13600m, 14000m], TableRead.Column(table, "Qty.Right").Cast<decimal>());
        Assert.Equal([-1800m, 0m], TableRead.Column(table, "Qty.Delta").Cast<decimal>());
        Assert.Equal([-5400m, -6000m], TableRead.Column(table, "Qty.LeftOnly").Cast<decimal>());
        Assert.Equal([3600m, 6000m], TableRead.Column(table, "Qty.RightOnly").Cast<decimal>());
        Assert.Equal([15m, -25.30m], TableRead.Column(table, "Cash.MatchedDelta").Cast<decimal>());
    }

    [Fact]
    public void Delta_is_left_only_plus_right_only_plus_matched_delta_on_every_line()
    {
        using var table = Samples.OmsBroker().ToSideBySideTable(QtyAndCashBySide);

        Assert.All(
            table.Rows.Cast<DataRow>(),
            row =>
            {
                Assert.Equal((decimal)row["Qty.Delta"], (decimal)row["Qty.LeftOnly"] + (decimal)row["Qty.RightOnly"] + (decimal)row["Qty.MatchedDelta"]);
                Assert.Equal((decimal)row["Cash.Delta"], (decimal)row["Cash.LeftOnly"] + (decimal)row["Cash.RightOnly"] + (decimal)row["Cash.MatchedDelta"]);
            });
    }

    [Fact]
    public void Ibor_sample_totals_by_fund()
    {
        using var table = Samples.IborAbor().ToSideBySideTable(new SideBySideSpec(["Fund"], [], [new MeasureSpec("Qty")], null));

        Assert.Equal(["GROWTH", "INCOME"], TableRead.Strings(table, "Row"));
        Assert.Equal([16200m, 10000m], TableRead.Column(table, "Qty.Left").Cast<decimal>());
        Assert.Equal([15500m, 10000m], TableRead.Column(table, "Qty.Right").Cast<decimal>());
        Assert.Equal([-700m, 0m], TableRead.Column(table, "Qty.Delta").Cast<decimal>());
    }

    [Fact]
    public void The_result_and_its_view_make_the_same_table()
    {
        var result = Samples.OmsBroker();

        using var fromResult = result.ToSideBySideTable(QtyAndCashBySide);
        using var fromView = result.Result.BuildSideBySide(QtyAndCashBySide).ToSideBySideTable();

        Assert.Equal(TableRead.ColumnNames(fromView), TableRead.ColumnNames(fromResult));
        Assert.Equal(fromView.Rows.Count, fromResult.Rows.Count);
        Assert.Equal(TableRead.Column(fromView, "Cash.Delta"), TableRead.Column(fromResult, "Cash.Delta"));
    }

    [Fact]
    public void A_measure_missing_from_a_line_is_null()
    {
        var cell = new SideBySideCell(1m, 2m, 1m, 0m, 0m, 1m);
        var view = new FixedView(
        [
            new SideBySideLine("A", "All", ImmutableDictionary<string, SideBySideCell>.Empty.Add("Qty", cell).Add("Cash", cell)),
            new SideBySideLine("B", "All", ImmutableDictionary<string, SideBySideCell>.Empty.Add("Qty", cell)),
        ]);

        using var table = view.ToSideBySideTable("Fixed");

        Assert.Equal("Fixed", table.TableName);
        Assert.Equal(1m, table.Rows[0]["Cash.Left"]);
        Assert.IsType<DBNull>(table.Rows[1]["Cash.Left"]);
        Assert.Equal(2m, table.Rows[1]["Qty.Right"]);
    }

    private sealed class FixedView(ImmutableArray<SideBySideLine> lines) : ISideBySideView
    {
        public ImmutableArray<SideBySideLine> Lines { get; } = lines;
    }
}
