using System.Data;

namespace RecDiffy.Extensions.Tests;

public sealed class SummaryAndCountsTableTests
{
    [Fact]
    public void Summary_has_one_row_per_group_with_its_shape()
    {
        using var table = Samples.OmsBroker().ToSummaryTable();

        Assert.Equal(["Group", "Status", "Shape", "Left", "Right", "Diffs"], TableRead.ColumnNames(table));
        Assert.Equal(
            ["1 ↔ 1", "3 ↔ 1", "3 ↔ 1", "1 ↔ 1", "1 ↔ 0", "1 ↔ 0", "1 ↔ 0", "0 ↔ 1", "0 ↔ 1", "0 ↔ 1"],
            TableRead.Strings(table, "Shape"));
        Assert.Equal(
            ["Exact", "Exact", "Break", "Break", "LeftOnly", "LeftOnly", "LeftOnly", "RightOnly", "RightOnly", "RightOnly"],
            TableRead.Strings(table, "Status"));
        Assert.Equal(["", "", "Cash 1128560.00 → 1128534.70", "Cash 716340.00 → 716355.00"], TableRead.Strings(table, "Diffs").Take(4).ToArray());
    }

    [Fact]
    public void Summary_shows_the_items_the_caller_describes_joined_with_a_dot_and_null_for_an_empty_side()
    {
        using var table = Samples.OmsBroker().ToSummaryTable(
            fill => $"{fill.ExecId} {fill.Qty}",
            confirm => $"{confirm.ConfirmRef} {confirm.Qty}");

        Assert.Equal("EX-45102 1000 · EX-45107 500 · EX-45113 500", table.Rows[1]["Left"]);
        Assert.Equal("CNF-0002 2000", table.Rows[1]["Right"]);
        Assert.IsType<DBNull>(table.Rows[4]["Right"]);
        Assert.IsType<DBNull>(table.Rows[7]["Left"]);
    }

    [Fact]
    public void Summary_shows_each_item_with_its_own_text_when_the_caller_gives_none()
    {
        using var table = Samples.IborAbor().ToSummaryTable();

        var left = (string)table.Rows[0]["Left"];
        Assert.Contains("US0378331005", left, StringComparison.Ordinal);
        Assert.Contains("5000", left, StringComparison.Ordinal);
    }

    [Fact]
    public void Status_counts_list_each_status_that_has_groups_from_exact_to_right_only()
    {
        using var oms = Samples.OmsBroker().ToStatusCountsTable();
        using var ibor = Samples.IborAbor().ToStatusCountsTable();

        Assert.Equal(["Status", "Groups"], TableRead.ColumnNames(oms));
        Assert.Equal(["Exact", "Break", "LeftOnly", "RightOnly"], TableRead.Strings(oms, "Status"));
        Assert.Equal([2, 2, 3, 3], TableRead.Column(oms, "Groups").Cast<int>());
        Assert.Equal(["Exact", "WithinTolerance", "Break", "Ambiguous", "LeftOnly", "RightOnly"], TableRead.Strings(ibor, "Status"));
        Assert.Equal([2, 1, 2, 1, 1, 1], TableRead.Column(ibor, "Groups").Cast<int>());
    }

    [Fact]
    public void Table_names_default_and_can_be_set()
    {
        var result = Samples.OmsBroker();

        using var summary = result.ToSummaryTable();
        using var counts = result.ToStatusCountsTable("Counts");

        Assert.Equal("Summary", summary.TableName);
        Assert.Equal("Counts", counts.TableName);
    }
}
