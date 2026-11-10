using RecDiffy.Results;
using RecDiffy.Rules;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

public class TradingAttributionTests
{
    private const decimal Huge = 79_228_162_514_264_337_593_543_950_335m;

    private static readonly RuleRegistry Trading = TradingRulePack.AddTo(RuleRegistry.CreateCore());
    private static readonly IAttribution NetCash = Trading.Attributions.Find("trading.netCash")!;
    private static readonly IAttribution ProRata = Trading.Attributions.Find("trading.proRataToQuantity")!;
    private static readonly FieldDeclaration Accrued = new("Accrued", FieldType.Decimal, FieldRole.Economic, 2);
    private static readonly FieldDeclaration Cash = new("NetCash", FieldType.Decimal, FieldRole.Economic, 2);

    [Theory]
    [InlineData("3827.78", true)]
    [InlineData("3827.77", false)]
    public void Pro_rata_explains_a_field_that_moves_with_quantity(string rightAccrued, bool explained) =>
        Assert.Equal(explained, ProRata.Explains(
            Row(Side.Left, ("Quantity", 1_000_000m), ("Accrued", 4_784.72m)),
            Row(Side.Right, ("Quantity", 800_000m), ("Accrued", decimal.Parse(rightAccrued, System.Globalization.CultureInfo.InvariantCulture))),
            Accrued,
            0m,
            MidpointMode.ToEven));

    [Fact]
    public void Pro_rata_leaves_a_zero_left_quantity_unexplained() =>
        Assert.False(ProRata.Explains(Row(Side.Left, ("Quantity", 0m), ("Accrued", 0m)), Row(Side.Right, ("Quantity", 10m), ("Accrued", 5m)), Accrued, 5m, MidpointMode.ToEven));

    [Fact]
    public void Pro_rata_leaves_a_missing_quantity_unexplained() =>
        Assert.False(ProRata.Explains(Row(Side.Left, ("Quantity", 10m), ("Accrued", 5m)), Row(Side.Right, ("Accrued", 5m)), Accrued, 0m, MidpointMode.ToEven));

    [Fact]
    public void Pro_rata_leaves_an_expectation_past_the_decimal_range_unexplained() =>
        Assert.False(ProRata.Explains(Row(Side.Left, ("Quantity", 1m), ("Accrued", Huge)), Row(Side.Right, ("Quantity", 10m), ("Accrued", 1m)), Accrued, 0m, MidpointMode.ToEven));

    [Fact]
    public void Pro_rata_takes_the_measured_outcome_of_Quantity_and_names_it()
    {
        var roots = Roots(("Quantity", FieldOutcome.WithinTolerance), ("Accrued", FieldOutcome.Break));

        Assert.Equal(FieldOutcome.WithinTolerance, ProRata.Outcome(roots));
        Assert.Equal(["Quantity"], ProRata.AttributedTo(roots), StringComparer.Ordinal);
    }

    [Fact]
    public void Net_cash_explains_a_rate_move_to_the_cent() =>
        Assert.True(NetCash.Explains(
            Row(Side.Left, ("Quantity", 1_000_000m), ("Price", 1.2705m)),
            Row(Side.Right, ("Quantity", 1_000_000m), ("Price", 1.2706m)),
            Cash,
            100m,
            MidpointMode.ToEven));

    [Theory]
    [InlineData("Bond", "-5000", true)]
    [InlineData("Equity", "-5000", false)]
    [InlineData("Equity", "-500000", true)]
    public void Net_cash_divides_a_bond_principal_by_100(string asset, string delta, bool explained) =>
        Assert.Equal(explained, NetCash.Explains(
            Symbol(Row(Side.Left, ("Quantity", 1_000_000m), ("Price", 100m)), "AssetClass", asset),
            Row(Side.Right, ("Quantity", 1_000_000m), ("Price", 99.5m)),
            Cash,
            decimal.Parse(delta, System.Globalization.CultureInfo.InvariantCulture),
            MidpointMode.ToEven));

    [Theory]
    [InlineData("SELL", "0.02", true)]
    [InlineData("REC", "0.02", true)]
    [InlineData("REC_FIXED", "0.02", true)]
    [InlineData("BUY", "-0.02", true)]
    [InlineData("BUY", "0.02", false)]
    public void Net_cash_subtracts_the_fee_on_a_sell(string side, string delta, bool explained) =>
        Assert.Equal(explained, NetCash.Explains(
            Symbol(Row(Side.Left, ("Fees", 67.50m)), "Side", side),
            Row(Side.Right, ("Fees", 67.48m)),
            Cash,
            decimal.Parse(delta, System.Globalization.CultureInfo.InvariantCulture),
            MidpointMode.ToEven));

    [Fact]
    public void Net_cash_counts_a_missing_accrued_as_zero() =>
        Assert.True(NetCash.Explains(Row(Side.Left, ("Accrued", 10m)), Row(Side.Right), Cash, -10m, MidpointMode.ToEven));

    [Fact]
    public void Net_cash_leaves_a_principal_past_the_decimal_range_unexplained() =>
        Assert.False(NetCash.Explains(Row(Side.Left, ("Quantity", Huge), ("Price", 2m)), Row(Side.Right, ("Quantity", 1m), ("Price", 1m)), Cash, 0m, MidpointMode.ToEven));

    [Fact]
    public void Net_cash_takes_the_worst_root_outcome() =>
        Assert.Equal(
            FieldOutcome.WithinTolerance,
            NetCash.Outcome(Roots(("Quantity", FieldOutcome.Exact), ("Price", FieldOutcome.WithinTolerance), ("Accrued", FieldOutcome.NotCompared), ("Fees", FieldOutcome.Exact))));

    [Fact]
    public void Net_cash_breaks_when_a_root_breaks() =>
        Assert.Equal(
            FieldOutcome.Break,
            NetCash.Outcome(Roots(("Quantity", FieldOutcome.WithinTolerance), ("Price", FieldOutcome.Exact), ("Accrued", FieldOutcome.Break), ("Fees", FieldOutcome.Exact))));

    [Fact]
    public void Net_cash_breaks_when_no_root_moved() =>
        Assert.Equal(
            FieldOutcome.Break,
            NetCash.Outcome(Roots(("Quantity", FieldOutcome.Exact), ("Price", FieldOutcome.Exact), ("Accrued", FieldOutcome.Exact), ("Fees", FieldOutcome.Exact))));

    [Fact]
    public void Net_cash_counts_a_root_that_is_not_a_step_as_a_break() =>
        Assert.Equal(
            FieldOutcome.Break,
            NetCash.Outcome(Roots(("Quantity", FieldOutcome.WithinTolerance), ("Price", FieldOutcome.Exact), ("Accrued", FieldOutcome.Exact))));

    [Fact]
    public void Net_cash_names_the_moved_roots_in_a_fixed_order_whatever_the_step_order() =>
        Assert.Equal(
            ["Quantity", "Accrued", "Fees"],
            NetCash.AttributedTo(Roots(("Fees", FieldOutcome.Break), ("Accrued", FieldOutcome.WithinTolerance), ("Price", FieldOutcome.NotCompared), ("Quantity", FieldOutcome.Break))),
            StringComparer.Ordinal);

    private static RootOutcomes Roots(params (string Field, FieldOutcome Outcome)[] roots) =>
        new(
            [.. roots.Select(root => new DiffStep(new FieldDeclaration(root.Field, FieldType.Decimal, FieldRole.Economic, 2), null, null, null, null))],
            [.. roots.Select(root => root.Outcome)]);

    private static RowContext Row(Side side, params (string Field, decimal Value)[] values) =>
        values.Aggregate(
            RowContext.Create(side.ToString(), side, new Dictionary<string, object?>(StringComparer.Ordinal)),
            (row, value) => row.Write(value.Field, CanonicalCell.FromDecimal(value.Value), "test", null, null));

    private static RowContext Symbol(RowContext row, string field, string value)
    {
        var (pool, interned) = row.Pool.InternSymbol(value);
        return row.WithPool(pool).Write(field, CanonicalCell.FromInterned(FieldType.Symbol, interned), "test", null, value);
    }
}
