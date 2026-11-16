using System.Globalization;
using System.Text.RegularExpressions;

namespace RecDiffy.Extensions.Tests;

public sealed record AlignRow(string Status, string Ref, int Qty, decimal Price, double Ratio, string Note);

public sealed record Amounts(string Amount);

public sealed class HtmlAlignmentTests
{
    private static readonly Regex Th = new("<th(?: class=\"(?<class>[^\"]*)\")?>", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(5));
    private static readonly Regex Tr = new("<tr class=\"[^\"]*\">(?<cells>.*?)</tr>", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.Singleline, TimeSpan.FromSeconds(5));
    private static readonly Regex Td = new("<td class=\"(?<class>[^\"]*)\">(?<text>.*?)</td>", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.Singleline, TimeSpan.FromSeconds(5));

    private static readonly AlignRow[] Rows =
    [
        new("Exact", "OMS-1042", 10_000, 189.21m, 1.5, "10"),
        new("Break", "OMS-1043", 5_000, 411.2501m, 22.25, "n/a"),
        new("LeftOnly", "OMS-1044", 800, 5m, double.NaN, ""),
        new("RightOnly", "X9917", 300, 1234.5m, 1e20, "7"),
    ];

    private static (string[] Heads, (string Class, string Text)[][] Cells) Parse(string html) =>
        (
            [.. Th.Matches(html).Select(match => match.Groups["class"].Value)],
            [.. Tr.Matches(html).Select(row => Td.Matches(row.Groups["cells"].Value)
                .Select(cell => (cell.Groups["class"].Value, cell.Groups["text"].Value))
                .ToArray())]);

    private static void AssertEachColumnAlignsAlike(string html)
    {
        var (heads, cells) = Parse(html);

        Assert.NotEmpty(cells);
        Assert.All(cells, row => Assert.Equal(heads.Length, row.Length));
        Assert.All(
            Enumerable.Range(0, heads.Length),
            column =>
            {
                var classes = cells.Select(row => row[column].Class).Distinct(StringComparer.Ordinal).ToArray();
                Assert.Single(classes);
                Assert.Equal(classes[0] == "num", heads[column] == "num");
            });
    }

    [Fact]
    public void Every_column_of_every_table_has_one_class_in_all_its_cells_and_a_numeric_one_has_a_numeric_header()
    {
        var oms = Samples.OmsBroker();
        var ibor = Samples.IborAbor();

        AssertEachColumnAlignsAlike(oms.ToReconciliationHtml());
        AssertEachColumnAlignsAlike(oms.ToSummaryHtml());
        AssertEachColumnAlignsAlike(oms.ToStatusCountsHtml());
        AssertEachColumnAlignsAlike(oms.ToSideBySideHtml(new RecDiffy.Results.SideBySideSpec(["Side"], [], [new RecDiffy.Results.MeasureSpec("Qty"), new RecDiffy.Results.MeasureSpec("Cash")], null)));
        AssertEachColumnAlignsAlike(ibor.ToReconciliationHtml());
        AssertEachColumnAlignsAlike(ibor.ToSummaryHtml());
        AssertEachColumnAlignsAlike(Rows.ToHtmlTable());
    }

    [Fact]
    public void Numeric_columns_get_a_numeric_header_and_numeric_cells_and_others_do_not()
    {
        var (heads, cells) = Parse(Rows.ToHtmlTable());

        Assert.Equal(["", "", "num", "num", "num", ""], heads);
        Assert.Equal(["status", "", "num", "num", "num", ""], cells[0].Select(cell => cell.Class).ToArray());
    }

    [Fact]
    public void One_odd_cell_makes_the_whole_column_plain()
    {
        var (heads, cells) = Parse(new Amounts[] { new("12"), new("n/a"), new("1,200.50") }.ToHtmlTable());

        Assert.Equal([""], heads);
        Assert.Equal(["", "", ""], cells.Select(row => row[0].Class).ToArray());
    }

    [Fact]
    public void Numeric_text_with_a_blank_cell_is_one_numeric_column()
    {
        var (heads, cells) = Parse(new Amounts[] { new("12"), new(""), new("1,200.50") }.ToHtmlTable());

        Assert.Equal(["num"], heads);
        Assert.Equal(["num", "num", "num"], cells.Select(row => row[0].Class).ToArray());
        Assert.Equal(["12", "", "1,200.50"], cells.Select(row => row[0].Text).ToArray());
    }

    [Fact]
    public void Decimals_in_a_column_are_padded_to_one_scale_without_changing_their_value()
    {
        var (_, cells) = Parse(Rows.ToHtmlTable());
        var prices = cells.Select(row => row[3].Text).ToArray();

        Assert.Equal(["189.2100", "411.2501", "5.0000", "1234.5000"], prices);
        Assert.Equal(Rows.Select(row => row.Price), prices.Select(price => decimal.Parse(price, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Doubles_are_padded_too_and_integers_special_values_and_exponents_are_left_alone()
    {
        var (_, cells) = Parse(Rows.ToHtmlTable());

        Assert.Equal(["1.50", "22.25", "NaN", "1E+20"], cells.Select(row => row[4].Text).ToArray());
        Assert.Equal(["10000", "5000", "800", "300"], cells.Select(row => row[2].Text).ToArray());
    }

    [Fact]
    public void The_decimal_scale_is_the_same_under_another_culture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var (_, cells) = Parse(Rows.ToHtmlTable());

            Assert.Equal(["189.2100", "411.2501", "5.0000", "1234.5000"], cells.Select(row => row[3].Text).ToArray());
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void The_status_header_and_its_pills_are_both_left_aligned()
    {
        var (heads, cells) = Parse(Rows.ToHtmlTable());
        var css = HtmlTable.Css;

        Assert.Equal("", heads[0]);
        Assert.Equal("status", cells[0][0].Class);
        Assert.Contains("table.rec td.status { text-align: left; }", css, StringComparison.Ordinal);
        Assert.DoesNotContain("text-align: center", css, StringComparison.Ordinal);
    }

    [Fact]
    public void The_style_sheet_aligns_headers_and_cells_on_the_baseline_and_right_aligns_numeric_headers()
    {
        var css = HtmlTable.Css;

        Assert.Contains("padding: 9px 14px; white-space: nowrap; vertical-align: baseline;", css, StringComparison.Ordinal);
        Assert.Contains("padding: 8px 14px; border-top: 1px solid #eaeef2; vertical-align: baseline;", css, StringComparison.Ordinal);
        Assert.Contains("table.rec th.num { text-align: right; }", css, StringComparison.Ordinal);
        Assert.Contains("font-size: 13px; white-space: nowrap; }", css, StringComparison.Ordinal);
        Assert.Contains("table.rec td.num { text-align: right;", css, StringComparison.Ordinal);
        Assert.DoesNotContain("vertical-align: top", css, StringComparison.Ordinal);
    }
}
