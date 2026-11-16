using System.Data;
using System.Globalization;

namespace RecDiffy.Extensions.Tests;

public sealed class DataGridTests
{
    private static DataTable Sample()
    {
        var table = new DataTable("Sample");
        table.Columns.AddRange(
        [
            new DataColumn("Status", typeof(string)),
            new DataColumn("Name", typeof(string)),
            new DataColumn("Qty", typeof(int)),
            new DataColumn("Big", typeof(long)),
            new DataColumn("Price", typeof(decimal)),
            new DataColumn("Ratio", typeof(double)),
            new DataColumn("Day", typeof(DateOnly)),
        ]);
        table.Rows.Add("Exact", "AAPL", 10_000, 1_234_567_890L, 1234.5m, 1234.5, new DateOnly(2026, 11, 13));
        table.Rows.Add("Break", "<MSFT>", 5, 7L, 411.2501m, 0.1, new DateOnly(2026, 1, 2));
        table.Rows.Add("Pending", DBNull.Value, DBNull.Value, DBNull.Value, 5m, DBNull.Value, DBNull.Value);
        return table;
    }

    [Fact]
    public void Number_columns_are_right_aligned_in_the_header_and_in_every_cell()
    {
        using var table = Sample();

        var html = table.ToDataGridHtml();

        Assert.Contains(
            "<thead><tr><th>Status</th><th>Name</th><th class=\"num\">Qty</th><th class=\"num\">Big</th><th class=\"num\">Price</th><th class=\"num\">Ratio</th><th>Day</th></tr></thead>",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "<tr><td><span class=\"tag ok\">Exact</span></td><td>AAPL</td><td class=\"num\">10,000</td><td class=\"num\">1,234,567,890</td><td class=\"num\">1,234.5000</td><td class=\"num\">1,234.50</td><td>2026-11-13</td></tr>",
            html,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Decimals_keep_the_largest_scale_in_their_column_with_thousands_separators()
    {
        using var table = Sample();

        var html = table.ToDataGridHtml();

        Assert.Contains("<td class=\"num\">1,234.5000</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td class=\"num\">411.2501</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td class=\"num\">5.0000</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td class=\"num\">0.10</td>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_status_is_a_tag_with_its_class_and_no_emoji()
    {
        using var table = new DataTable("Statuses");
        table.Columns.AddRange([new DataColumn("Status", typeof(string))]);
        foreach (var status in new[] { "Exact", "WithinTolerance", "Break", "Ambiguous", "LeftOnly", "RightOnly", "Pending" })
            table.Rows.Add(status);

        var html = table.ToDataGridHtml();

        Assert.Contains("<span class=\"tag ok\">Exact</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"tag warn\">WithinTolerance</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"tag bad\">Break</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"tag amb\">Ambiguous</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"tag orphan\">LeftOnly</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"tag orphan\">RightOnly</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"tag\">Pending</span>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("✅", html, StringComparison.Ordinal);
        Assert.DoesNotContain("←", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Null_cells_are_empty_and_keep_their_column_alignment()
    {
        using var table = Sample();

        var html = table.ToDataGridHtml();

        Assert.Contains(
            "<tr><td><span class=\"tag\">Pending</span></td><td></td><td class=\"num\"></td><td class=\"num\"></td><td class=\"num\">5.0000</td><td class=\"num\"></td><td></td></tr>",
            html,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_grid_is_the_same_under_another_culture()
    {
        using var table = Sample();
        var invariant = table.ToDataGridHtml();
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.Equal(invariant, table.ToDataGridHtml());
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void The_caption_defaults_to_the_table_name_and_text_is_encoded()
    {
        using var table = Sample();

        Assert.StartsWith("<table class=\"grid\"><caption>Sample</caption><thead>", table.ToDataGridHtml(), StringComparison.Ordinal);
        Assert.StartsWith("<table class=\"grid\"><thead>", table.ToDataGridHtml(""), StringComparison.Ordinal);
        Assert.Contains("<caption>A &amp; B</caption>", table.ToDataGridHtml("A & B"), StringComparison.Ordinal);
        Assert.Contains("<td>&lt;MSFT&gt;</td>", table.ToDataGridHtml(), StringComparison.Ordinal);
        Assert.Contains("<td>2026-01-02</td>", table.ToDataGridHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_style_sheet_aligns_on_the_baseline_and_right_aligns_numbers()
    {
        var css = DataGrid.Css;

        Assert.Equal(2, css.Split("vertical-align: baseline;", StringSplitOptions.None).Length - 1);
        Assert.Contains("table.grid .num { text-align: right; }", css, StringComparison.Ordinal);
        Assert.Contains("table.grid .tag.ok { color: #15803d; background: #dcfce7; }", css, StringComparison.Ordinal);
        Assert.Contains("table.grid .tag.orphan { color: #c2410c; background: #ffedd5; }", css, StringComparison.Ordinal);
    }

    [Fact]
    public void A_null_table_is_rejected()
    {
        DataTable table = null!;

        Assert.Throws<ArgumentNullException>(() => table.ToDataGridHtml());
    }
}
