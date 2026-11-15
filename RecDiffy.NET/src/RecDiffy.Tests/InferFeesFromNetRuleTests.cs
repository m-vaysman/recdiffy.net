using System.Globalization;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

// fees.inferFromNet (spec v3.1 §3.3): BUY and PAY_FIXED give net - principal - accrued, SELL and REC_FIXED give
// principal + accrued - net, where the profile defines principal: bond quantity * price / 100, equity and forward quantity * price.
public class InferFeesFromNetRuleTests
{
    private static readonly RuleBench Infer = new RuleBench("fees.inferFromNet").With("to", "Fees");

    // 1,000,000 face at 98.75 is 987,500.00 principal, plus 1,375.00 accrued, so 988,875.00 before fees.
    [Theory]
    [InlineData("BUY", 988925.00, "50.00")]
    [InlineData("PAY_FIXED", 988925.00, "50.00")]
    [InlineData("SELL", 988825.00, "50.00")]
    [InlineData("REC_FIXED", 988825.00, "50.00")]
    public void A_bond_fee_is_what_net_cash_adds_to_or_takes_from_principal_and_accrued(string side, double net, string fees)
    {
        var row = Infer.Apply(Bond(side, (decimal)net));

        Assert.Equal(fees, Fees(row));
        Assert.Empty(row.Diagnostics);
    }

    // Fixture 2.0 signs cash: a BUY pays out. The magnitude gives the same fee, so one rule serves signed and unsigned cash (§9.2).
    [Theory]
    [InlineData("BUY", -988925.00, "50.00")]
    [InlineData("SELL", 988825.00, "50.00")]
    public void Signed_cash_gives_the_same_fee_as_unsigned_cash(string side, double net, string fees) =>
        Assert.Equal(fees, Fees(Infer.Apply(Bond(side, (decimal)net))));

    [Fact]
    public void An_equity_has_no_divisor_and_a_missing_accrued_counts_as_zero()
    {
        var row = Infer.Apply(RuleBench.Row().Canonical(("Side", "BUY"), ("AssetClass", "Equity"), ("Quantity", 1000m), ("Price", 45.50m), ("NetCash", 45512.00m)));

        Assert.Equal("12.00", Fees(row));
    }

    [Fact]
    public void A_forward_is_quantity_times_the_rate()
    {
        var row = Infer.Apply(RuleBench.Row().Canonical(("Side", "SELL"), ("AssetClass", "Forward"), ("Quantity", 5000000m), ("Price", 1.083456m), ("Accrued", 0m), ("NetCash", 5417200.00m)));

        Assert.Equal("80.00", Fees(row));
    }

    // §3.3: a swap's principal is not a fixed function of notional and rate, so its street fee stays null.
    [Theory]
    [InlineData("Swap")]
    [InlineData("Other")]
    public void An_asset_class_with_no_principal_formula_has_no_fee(string asset)
    {
        var row = Infer.Apply(RuleBench.Row().Canonical(("Side", "PAY_FIXED"), ("AssetClass", asset), ("Quantity", 1000000m), ("Price", 0.04m), ("Accrued", 10m), ("NetCash", 123.00m)));

        Assert.True(row.Values["Fees"].IsNull);
        Assert.Empty(row.Diagnostics);
    }

    [Theory]
    [InlineData("Side")]
    [InlineData("AssetClass")]
    [InlineData("Quantity")]
    [InlineData("Price")]
    [InlineData("NetCash")]
    public void A_missing_input_leaves_the_fee_null_and_does_not_quarantine(string missing)
    {
        var cells = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Side"] = "BUY",
            ["AssetClass"] = "Bond",
            ["Quantity"] = 1000000m,
            ["Price"] = 98.75m,
            ["Accrued"] = 1375.00m,
            ["NetCash"] = 988925.00m,
        };
        cells.Remove(missing);

        var row = Infer.Apply(RuleBench.Row().Canonical([.. cells.Select(cell => (cell.Key, cell.Value))]));

        Assert.True(row.Values["Fees"].IsNull);
        Assert.Empty(row.Diagnostics);
    }

    // 0.125 of principal is a midpoint at cents: 0.12 under ToEven and 0.13 under AwayFromZero.
    [Theory]
    [InlineData(MidpointMode.ToEven, "0.88")]
    [InlineData(MidpointMode.AwayFromZero, "0.87")]
    public void Principal_is_rounded_to_cents_with_the_profile_midpoint(MidpointMode midpoint, string fees)
    {
        var row = (Infer with { Midpoint = midpoint }).Apply(RuleBench.Row().Canonical(("Side", "BUY"), ("AssetClass", "Equity"), ("Quantity", 1m), ("Price", 0.125m), ("NetCash", 1.00m)));

        Assert.Equal(fees, Fees(row));
    }

    [Fact]
    public void A_product_past_the_decimal_range_leaves_the_fee_null_and_does_not_throw()
    {
        var row = Infer.Apply(RuleBench.Row().Canonical(("Side", "BUY"), ("AssetClass", "Equity"), ("Quantity", decimal.MaxValue), ("Price", 2m), ("NetCash", 1m)));

        Assert.True(row.Values["Fees"].IsNull);
        Assert.Empty(row.Diagnostics);
    }

    [Fact]
    public void The_trace_names_the_rule()
    {
        var row = Infer.Apply(Bond("BUY", 988925.00m));

        Assert.Equal(new TraceStep("fees.inferFromNet", "Fees", "988925.00", "50.00"), row.Traces[^1]);
    }

    private static RowContext Bond(string side, decimal net) =>
        RuleBench.Row().Canonical(("Side", side), ("AssetClass", "Bond"), ("Quantity", 1000000m), ("Price", 98.75m), ("Accrued", 1375.00m), ("NetCash", net));

    private static string? Fees(RowContext row) => row.Values["Fees"].Number.ToString(CultureInfo.InvariantCulture);
}
