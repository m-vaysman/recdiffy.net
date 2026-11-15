using System.Globalization;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// cash.toPerspective (spec v3.1 §9.2, fixture 2.0): net cash reported in the dealer's perspective is negated to the internal view.
public class CashToPerspectiveRuleTests
{
    private static readonly RuleBench Perspective = new RuleBench("cash.toPerspective")
        .With("to", "NetCash")
        .With("perspective", "Persp")
        .With("invertWhen", "DEALER_BILATERAL_VIEW");

    [Theory]
    [InlineData("1087625.00", "-1087625.00")]
    [InlineData("-19089000.00", "19089000.00")]
    public void A_dealer_view_amount_is_negated(string amount, string expected)
    {
        var row = Perspective.Apply(Row("DEALER_BILATERAL_VIEW", decimal.Parse(amount, CultureInfo.InvariantCulture)));

        Assert.Equal(expected, Text(row));
        Assert.Equal(new TraceStep("cash.toPerspective", "NetCash", amount, expected), row.Traces[^1]);
    }

    [Theory]
    [InlineData("CLIENT_VIEW")]
    [InlineData("")]
    public void Any_other_perspective_is_left_as_read(string perspective)
    {
        var row = Row(perspective, 1087625.00m);

        Assert.Same(row, Perspective.Apply(row));
    }

    [Fact]
    public void A_row_with_no_perspective_column_is_left_as_read()
    {
        var row = RuleBench.Row().Canonical(("NetCash", 5m));

        Assert.Same(row, Perspective.Apply(row));
    }

    [Fact]
    public void A_null_amount_stays_null()
    {
        var row = RuleBench.Row(("Persp", "DEALER_BILATERAL_VIEW"));

        Assert.Same(row, Perspective.Apply(row));
    }

    // Negating zero must not leave a negative zero that keys and displays differently.
    [Fact]
    public void Zero_stays_a_positive_zero() =>
        Assert.Equal("0.00", Text(Perspective.Apply(Row("DEALER_BILATERAL_VIEW", 0.00m))));

    [Fact]
    public void Only_net_cash_is_negated()
    {
        var row = Perspective.Apply(Row("DEALER_BILATERAL_VIEW", 100m).Canonical(("Accrued", 12.34m)));

        Assert.Equal("12.34", row.Values["Accrued"].Number.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void The_perspective_column_defaults_to_the_one_side_resolve_reads()
    {
        var bench = new RuleBench("cash.toPerspective").With("to", "NetCash").With("invertWhen", "DEALER_BILATERAL_VIEW");

        var row = bench.Apply(RuleBench.Row(("Reporting_Perspective", "DEALER_BILATERAL_VIEW")).Canonical(("NetCash", 7m)));

        Assert.Equal("-7", Text(row));
    }

    private static RowContext Row(string perspective, decimal net) =>
        RuleBench.Row(("Persp", perspective)).Canonical(("NetCash", net));

    private static string Text(RowContext row) => row.Values["NetCash"].Number.ToString(CultureInfo.InvariantCulture);
}
