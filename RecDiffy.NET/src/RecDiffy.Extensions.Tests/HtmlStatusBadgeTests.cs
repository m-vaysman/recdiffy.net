namespace RecDiffy.Extensions.Tests;

public sealed class HtmlStatusBadgeTests
{
    [Fact]
    public void Left_only_and_right_only_use_plain_text_arrows()
    {
        var html = Samples.OmsBroker().ToSummaryHtml();

        Assert.Contains("<span class=\"pill\">← LeftOnly</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"pill\">→ RightOnly</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void No_table_contains_the_emoji_arrows_that_the_system_draws_as_boxes()
    {
        var oms = Samples.OmsBroker();
        var ibor = Samples.IborAbor();

        Assert.All(
            new[] { oms.ToReconciliationHtml(), oms.ToSummaryHtml(), oms.ToStatusCountsHtml(), ibor.ToReconciliationHtml(), ibor.ToStatusCountsHtml() },
            html =>
            {
                Assert.DoesNotContain("⬅", html, StringComparison.Ordinal);
                Assert.DoesNotContain("➡", html, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void A_status_that_is_an_arrow_or_the_old_emoji_still_colours_its_row()
    {
        StatusLine[] rows =
        [
            new("←", "a"),
            new("→", "b"),
            new("⬅️", "c"),
            new("➡️", "d"),
            new("LeftOnly", "e"),
        ];

        var html = rows.ToHtmlTable();

        Assert.Equal(5, html.Split("<tr class=\"orphan\">", StringSplitOptions.None).Length - 1);
    }
}
