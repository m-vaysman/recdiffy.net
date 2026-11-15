using RecDiffy.Rules;

namespace RecDiffy.Tests;

// price.dirtyToClean (spec v3.1 §3.3): clean = dirty - accrued / quantity * 100, in decimal, to the price field's scale.
public class DirtyToCleanRuleTests
{
    private static readonly RuleBench Clean = new RuleBench("price.dirtyToClean").With("to", "Price") with { Scale = 6 };

    // §3.3's own row, CG-51174451: dirty 100.213333, accrued 54,687.50, quantity 3,750,000 gives 98.755000.
    [Fact]
    public void The_spec_example_gives_98_755()
    {
        var row = Clean.Apply(Parsed(100.213333m, 54687.50m, 3750000m));

        Assert.Equal("98.755000", row.Values["Price"].Number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Empty(row.Diagnostics);
    }

    [Fact]
    public void The_trace_names_the_rule_with_the_dirty_price_as_its_input()
    {
        var row = Clean.Apply(Parsed(100.213333m, 54687.50m, 3750000m));

        Assert.Equal(new TraceStep("price.dirtyToClean", "Price", "100.213333", "98.755000"), row.Traces[^1]);
    }

    [Fact]
    public void A_negative_quantity_is_read_as_its_magnitude() =>
        Assert.Equal(98.755m, Clean.Apply(Parsed(100.213333m, 54687.50m, -3750000m)).Values["Price"].Number);

    // 100.0000005 sits exactly on a midpoint at scale 6.
    [Theory]
    [InlineData(MidpointMode.ToEven, "100.000000")]
    [InlineData(MidpointMode.AwayFromZero, "100.000001")]
    public void The_profile_midpoint_rounds_to_the_field_scale(MidpointMode midpoint, string expected) =>
        Assert.Equal(expected, (Clean with { Midpoint = midpoint }).Apply(Parsed(100.0000005m, 0m, 1000m)).Values["Price"].Number.ToString(System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void The_field_scale_is_the_declared_one() =>
        Assert.Equal("98.7550", (Clean with { Scale = 4 }).Apply(Parsed(100.213333m, 54687.50m, 3750000m)).Values["Price"].Number.ToString(System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void A_null_price_is_left_as_it_is_with_no_diagnostic()
    {
        var row = RuleBench.Row().Canonical(("Accrued", 10m), ("Quantity", 100m));

        var applied = Clean.Apply(row);

        Assert.Same(row, applied);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void A_dirty_price_with_no_accrued_or_no_quantity_is_PriceParse(bool hasAccrued, bool hasQuantity)
    {
        var row = RuleBench.Row().Canonical(("Price", 100.5m), ("Accrued", hasAccrued ? 10m : null), ("Quantity", hasQuantity ? 100m : null));

        var applied = Clean.Apply(row);

        Assert.True(applied.Values["Price"].IsNull);
        var diagnostic = Assert.Single(applied.Diagnostics);
        Assert.Equal(("PriceParse", DiagnosticSeverity.Invalid, "price.dirtyToClean", "Price", "100.5"), (diagnostic.Code, diagnostic.Severity, diagnostic.RuleId, diagnostic.Field, diagnostic.RawText));
    }

    // Accrued over quantity first, then times 100: equal accrued and face is exactly 100, so 100.5 clean is 0.5 and nothing overflows.
    [Fact]
    public void An_accrued_the_size_of_the_quantity_does_not_overflow_before_it_is_divided() =>
        Assert.Equal(0.5m, Clean.Apply(Parsed(100.5m, decimal.MaxValue, decimal.MaxValue)).Values["Price"].Number);

    // A text cell has no number: a text price is left as it is, and a text accrued or quantity makes the quote unreadable.
    [Fact]
    public void A_price_that_holds_text_is_left_as_it_is()
    {
        var row = RuleBench.Row().Canonical(("Price", "N/A"), ("Accrued", 10m), ("Quantity", 100m));

        Assert.Same(row, Clean.Apply(row));
    }

    [Theory]
    [InlineData("Accrued")]
    [InlineData("Quantity")]
    public void An_accrued_or_quantity_that_holds_text_is_PriceParse_not_zero(string field)
    {
        var applied = Clean.Apply(Parsed(100.5m, 10m, 100m).Canonical((field, "N/A")));

        Assert.True(applied.Values["Price"].IsNull);
        Assert.Equal("PriceParse", Assert.Single(applied.Diagnostics).Code);
    }

    [Fact]
    public void A_zero_quantity_is_PriceParse_not_a_division_by_zero()
    {
        var applied = Clean.Apply(Parsed(100.5m, 10m, 0m));

        Assert.True(applied.Values["Price"].IsNull);
        Assert.Equal("PriceParse", Assert.Single(applied.Diagnostics).Code);
    }

    // Accrued over half a unit of face leaves the decimal range, so the rule reports an unreadable price and does not throw.
    [Fact]
    public void A_step_past_the_decimal_range_is_PriceParse()
    {
        var applied = Clean.Apply(Parsed(100.5m, decimal.MaxValue, 0.5m));

        Assert.True(applied.Values["Price"].IsNull);
        Assert.Equal("PriceParse", Assert.Single(applied.Diagnostics).Code);
    }

    private static RowContext Parsed(decimal dirty, decimal accrued, decimal quantity) =>
        RuleBench.Row().Canonical(("Price", dirty), ("Accrued", accrued), ("Quantity", quantity));
}
