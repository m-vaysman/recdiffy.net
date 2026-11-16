using System.Data;
using System.Globalization;
using RecDiffy.Results;

namespace RecDiffy.Extensions.Tests;

public sealed record StatusLine(string Status, string Note);

public sealed record Line(string Id, decimal Amount, string Note, DateOnly Day, DateTime At);

public sealed class HtmlTableTests
{
    private static int Count(string text, string needle) => text.Split(needle, StringSplitOptions.None).Length - 1;

    [Fact]
    public void Summary_html_is_one_table_with_the_caption_the_headers_and_a_row_per_group()
    {
        var html = Samples.OmsBroker().ToSummaryHtml(caption: "OMS vs broker");

        Assert.StartsWith(
            "<table class=\"rec\"><caption>OMS vs broker</caption><thead><tr><th>Group</th><th>Status</th><th>Shape</th><th>Left</th><th>Right</th><th>Diffs</th></tr></thead><tbody>",
            html,
            StringComparison.Ordinal);
        Assert.EndsWith("</tbody></table>", html, StringComparison.Ordinal);
        Assert.Equal(10, Count(html, "<tr class="));
    }

    [Fact]
    public void Each_status_gets_its_row_class_and_a_pill_with_its_badge()
    {
        var html = Samples.OmsBroker().ToSummaryHtml();

        Assert.Equal(2, Count(html, "<tr class=\"ok\">"));
        Assert.Equal(2, Count(html, "<tr class=\"bad\">"));
        Assert.Equal(6, Count(html, "<tr class=\"orphan\">"));
        Assert.Contains("<span class=\"pill\">✅ Exact</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"pill\">🔴 Break</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"pill\">⬅️ LeftOnly</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"pill\">➡️ RightOnly</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Within_tolerance_and_ambiguous_rows_get_their_own_classes()
    {
        var html = Samples.IborAbor().ToReconciliationHtml();

        Assert.Contains("<tr class=\"warn\">", html, StringComparison.Ordinal);
        Assert.Contains("<tr class=\"amb\">", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"pill\">🟡 WithinTolerance</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"pill\">⚠️ Ambiguous</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_is_html_encoded()
    {
        StatusLine[] rows = [new("Exact", "<script>alert(1)</script> & co")];

        var html = rows.ToHtmlTable("<i>x</i>");

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt; &amp; co", html, StringComparison.Ordinal);
        Assert.Contains("<caption>&lt;i&gt;x&lt;/i&gt;</caption>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sequence_with_no_caption_has_none_and_a_status_badge_colours_its_row()
    {
        StatusLine[] rows = [new("✅", "a"), new("🔴", "b"), new("pending", "c")];

        var html = rows.ToHtmlTable();

        Assert.DoesNotContain("<caption>", html, StringComparison.Ordinal);
        Assert.Contains("<tr class=\"ok\"><td class=\"status\"><span class=\"pill\">✅</span></td>", html, StringComparison.Ordinal);
        Assert.Contains("<tr class=\"bad\">", html, StringComparison.Ordinal);
        Assert.Contains("<tr class=\"\"><td class=\"status\"><span class=\"pill\">pending</span></td>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Numbers_are_right_aligned_and_long_identifiers_are_monospace()
    {
        Line[] rows = [new("PM1-20261113-0001", 1234.5m, "1,200.50", new DateOnly(2026, 11, 13), new DateTime(2026, 11, 13, 9, 30, 0, DateTimeKind.Unspecified))];

        var html = rows.ToHtmlTable();

        Assert.Contains("<td class=\"id\">PM1-20261113-0001</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td class=\"num\">1234.5</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td class=\"num\">1,200.50</td>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Numbers_and_dates_do_not_change_with_the_current_culture()
    {
        Line[] rows = [new("x", 1234.5m, "", new DateOnly(2026, 11, 13), new DateTime(2026, 11, 13, 9, 30, 0, DateTimeKind.Unspecified))];
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var html = rows.ToHtmlTable();

            Assert.Contains("<td class=\"num\">1234.5</td>", html, StringComparison.Ordinal);
            Assert.Contains("<td class=\"\">2026-11-13</td>", html, StringComparison.Ordinal);
            Assert.Contains("<td class=\"\">2026-11-13 09:30:00</td>", html, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void A_data_table_shows_null_cells_empty_and_takes_its_caption_from_its_name_unless_given_one()
    {
        using var table = new DataTable("Named");
        table.Columns.AddRange([new DataColumn("A", typeof(string)), new DataColumn("B", typeof(int))]);
        table.Rows.Add("x", DBNull.Value);

        var named = table.ToHtmlTable();
        var given = table.ToHtmlTable("Given");
        var none = table.ToHtmlTable("");

        Assert.Contains("<caption>Named</caption>", named, StringComparison.Ordinal);
        Assert.Contains("<caption>Given</caption>", given, StringComparison.Ordinal);
        Assert.DoesNotContain("<caption>", none, StringComparison.Ordinal);
        Assert.Contains("<tr class=\"\"><td class=\"\">x</td><td class=\"\"></td></tr>", named, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_html_method_renders_its_table_under_its_default_caption()
    {
        var result = Samples.OmsBroker();
        var spec = new SideBySideSpec(["Side"], [], [new MeasureSpec("Qty")], null);

        using var reconciliation = result.ToReconciliationTable();
        using var summary = result.ToSummaryTable();
        using var counts = result.ToStatusCountsTable();
        using var sideBySide = result.ToSideBySideTable(spec);

        Assert.Equal(reconciliation.ToHtmlTable("Reconciliation"), result.ToReconciliationHtml());
        Assert.Equal(summary.ToHtmlTable("Summary"), result.ToSummaryHtml());
        Assert.Equal(counts.ToHtmlTable("StatusCounts"), result.ToStatusCountsHtml());
        Assert.Equal(sideBySide.ToHtmlTable("SideBySide"), result.ToSideBySideHtml(spec));
        Assert.Equal(sideBySide.ToHtmlTable("SideBySide"), result.Result.BuildSideBySide(spec).ToSideBySideHtml());
    }

    [Fact]
    public void The_style_sheet_covers_the_classes_the_tables_use()
    {
        Assert.Contains("table.rec", HtmlTable.Css, StringComparison.Ordinal);
        Assert.All(["ok", "warn", "bad", "amb", "orphan", "num", "id", "status", "pill"], name => Assert.Contains(name, HtmlTable.Css, StringComparison.Ordinal));
    }
}
