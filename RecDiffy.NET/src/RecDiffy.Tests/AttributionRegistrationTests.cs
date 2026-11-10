using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

public class AttributionRegistrationTests
{
    private static readonly RuleRegistry Trading = TradingRulePack.AddTo(RuleRegistry.CreateCore());

    [Fact]
    public void The_trading_pack_registers_trading_netCash_to_NetCashAttribution() =>
        Assert.IsType<NetCashAttribution>(Trading.Attributions.Find("trading.netCash"));

    [Fact]
    public void The_trading_pack_registers_trading_proRataToQuantity_to_ProRataToQuantityAttribution() =>
        Assert.IsType<ProRataToQuantityAttribution>(Trading.Attributions.Find("trading.proRataToQuantity"));

    [Fact]
    public void The_core_registers_no_attribution()
    {
        var core = RuleRegistry.CreateCore();

        Assert.Null(core.Attributions.Find("trading.netCash"));
        Assert.Null(core.Attributions.Find("trading.proRataToQuantity"));
    }

    [Fact]
    public void A_missing_name_finds_nothing() =>
        Assert.Null(Trading.Attributions.Find(null));

    [Fact]
    public void Registering_returns_a_new_registry_and_leaves_the_original_without_it()
    {
        var core = RuleRegistry.CreateCore();

        var extended = core.RegisterAttribution(new NetCashAttribution());

        Assert.Null(core.Attributions.Find("trading.netCash"));
        Assert.IsType<NetCashAttribution>(extended.Attributions.Find("trading.netCash"));
    }

    [Fact]
    public void A_name_cannot_be_registered_twice() =>
        Assert.Throws<ArgumentException>(() => Trading.RegisterAttribution(new NetCashAttribution()));

    [Fact]
    public void A_null_attribution_is_rejected() =>
        Assert.Throws<ArgumentNullException>(() => RuleRegistry.CreateCore().RegisterAttribution(null!));

    [Fact]
    public void Net_cash_needs_Quantity_and_Price_and_is_finished_last()
    {
        var attribution = Trading.Attributions.Find("trading.netCash")!;

        Assert.Equal(["Quantity", "Price"], attribution.Roots, StringComparer.Ordinal);
        Assert.Equal(AttributionStage.Finished, attribution.Stage);
    }

    [Fact]
    public void Pro_rata_needs_Quantity_and_is_finished_from_measured_outcomes()
    {
        var attribution = Trading.Attributions.Find("trading.proRataToQuantity")!;

        Assert.Equal(["Quantity"], attribution.Roots, StringComparer.Ordinal);
        Assert.Equal(AttributionStage.Measured, attribution.Stage);
    }
}
