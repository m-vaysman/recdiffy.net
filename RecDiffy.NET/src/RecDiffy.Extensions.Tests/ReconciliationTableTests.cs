using System.Data;
using System.Globalization;

namespace RecDiffy.Extensions.Tests;

public sealed class Indexed(string id)
{
    public string Id { get; } = id;

    public int this[int index] => index;
}

public sealed class ReconciliationTableTests
{
    [Fact]
    public void Oms_sample_has_one_group_per_entry_and_one_row_per_item_on_the_longer_side()
    {
        using var table = Samples.OmsBroker().ToReconciliationTable();

        Assert.Equal(14, table.Rows.Count);
        Assert.Equal(
            ["Exact", "Exact", "Break", "Break", "LeftOnly", "LeftOnly", "LeftOnly", "RightOnly", "RightOnly", "RightOnly"],
            TableRead.StatusPerGroup(table));
        Assert.Equal([1, 3, 3, 1, 1, 1, 1, 1, 1, 1], TableRead.RowsPerGroup(table));
    }

    [Fact]
    public void Columns_are_the_group_the_status_the_properties_of_each_side_and_the_diffs()
    {
        using var table = Samples.OmsBroker().ToReconciliationTable();

        Assert.Equal(
            [
                "Group", "Status",
                "Left.ClOrdId", "Left.ExecId", "Left.Ticker", "Left.Isin", "Left.TradeDate", "Left.Side", "Left.Qty", "Left.Price", "Left.Fees", "Left.Cash",
                "Right.ConfirmRef", "Right.Ticker", "Right.Isin", "Right.TradeDate", "Right.Side", "Right.Qty", "Right.Price", "Right.Fees", "Right.Cash",
                "Diffs",
            ],
            TableRead.ColumnNames(table));
        Assert.Equal(typeof(DateOnly), table.Columns["Left.TradeDate"]!.DataType);
        Assert.Equal(typeof(decimal), table.Columns["Right.Cash"]!.DataType);
        Assert.Equal(typeof(int), table.Columns["Left.Qty"]!.DataType);
    }

    [Fact]
    public void A_sum_match_lists_every_left_item_and_puts_the_right_item_on_the_first_row_only()
    {
        using var table = Samples.OmsBroker().ToReconciliationTable();
        var msft = table.Rows.Cast<DataRow>().Where(row => (int)row["Group"] == 2).ToArray();

        Assert.Equal(["EX-45102", "EX-45107", "EX-45113"], msft.Select(row => (string)row["Left.ExecId"]).ToArray());
        Assert.Equal("CNF-0002", msft[0]["Right.ConfirmRef"]);
        Assert.All(msft.Skip(1), row => Assert.IsType<DBNull>(row["Right.ConfirmRef"]));
    }

    [Fact]
    public void An_unmatched_item_leaves_the_other_side_null()
    {
        using var table = Samples.OmsBroker().ToReconciliationTable();
        var leftOnly = table.Rows.Cast<DataRow>().First(row => (string)row["Status"] == "LeftOnly");
        var rightOnly = table.Rows.Cast<DataRow>().First(row => (string)row["Status"] == "RightOnly");

        Assert.Equal("EX-45141", leftOnly["Left.ExecId"]);
        Assert.IsType<DBNull>(leftOnly["Right.ConfirmRef"]);
        Assert.IsType<DBNull>(rightOnly["Left.ExecId"]);
        Assert.Equal("CNF-0005", rightOnly["Right.ConfirmRef"]);
    }

    [Fact]
    public void Diffs_describe_the_compared_fields_that_differ_on_the_first_row_of_a_group_only()
    {
        using var table = Samples.OmsBroker().ToReconciliationTable();
        var nvda = table.Rows.Cast<DataRow>().Where(row => (int)row["Group"] == 3).ToArray();

        Assert.Equal("Cash 1128560.00 → 1128534.70", nvda[0]["Diffs"]);
        Assert.Equal(["", ""], nvda.Skip(1).Select(row => (string)row["Diffs"]).ToArray());
    }

    [Fact]
    public void Ibor_sample_shows_all_six_statuses()
    {
        using var table = Samples.IborAbor().ToReconciliationTable();

        Assert.Equal(
            ["Exact", "WithinTolerance", "Break", "Exact", "Break", "Ambiguous", "LeftOnly", "RightOnly"],
            TableRead.StatusPerGroup(table));
        Assert.Equal([2, 1, 1, 2, 1, 2, 1, 1], TableRead.RowsPerGroup(table));
        Assert.Equal(
            ["", "MarketValue 830400.00 → 830420.00", "Qty 8000 → 7000 · MarketValue 1128800.00 → 987700.00", "", "MarketValue 674400.00 → 677700.00", "", "", ""],
            TableRead.FirstRowPerGroup(table, "Diffs"));
    }

    [Fact]
    public void Table_name_and_locale_are_set()
    {
        var result = Samples.OmsBroker();

        using var named = result.ToReconciliationTable("OmsBroker");
        using var unnamed = result.ToReconciliationTable();

        Assert.Equal("OmsBroker", named.TableName);
        Assert.Equal("Reconciliation", unnamed.TableName);
        Assert.Equal(CultureInfo.InvariantCulture, named.Locale);
    }

    [Fact]
    public void A_match_with_nothing_to_compare_is_exact_and_an_indexer_is_not_a_column()
    {
        var result = RecBuilder.Between([new Indexed("a")], [new Indexed("a")])
            .MatchOn(left => left.Id, right => right.Id)
            .Run();

        using var table = result.ToReconciliationTable();

        Assert.Equal(["Group", "Status", "Left.Id", "Right.Id", "Diffs"], TableRead.ColumnNames(table));
        Assert.Equal(["Exact"], TableRead.Strings(table, "Status"));
    }

    [Fact]
    public void An_empty_run_makes_a_table_with_columns_and_no_rows()
    {
        var result = RecBuilder.Between(Array.Empty<Indexed>(), Array.Empty<Indexed>())
            .MatchOn(left => left.Id, right => right.Id)
            .Run();

        using var table = result.ToReconciliationTable();

        Assert.Equal(["Group", "Status", "Left.Id", "Right.Id", "Diffs"], TableRead.ColumnNames(table));
        Assert.Empty(table.Rows);
    }
}
