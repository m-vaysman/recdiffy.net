using RecDiffy.Results;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

// #433. trading.netCashSigned is trading.netCash for cash whose sign says which way the money moves (spec §9.2: fixture
// 2.0, where a BUY is negative). A bond's signed cash is:
//
//   BUY:  -(quantity x price / 100 + accrued) - fees        SELL:  quantity x price / 100 + accrued - fees
//
// Every row below is a bond of 1,000,000 at 100 with accrued 4,784.72 and a fee of 50, so a BUY's cash is -1,004,834.72
// and a SELL's is 1,004,734.72. The tests run a profile through the trading pack, so they also prove the registration.
public class SignedNetCashAttributionTests
{
    private const string Signed = "trading.netCashSigned";

    private const string Unsigned = "trading.netCash";

    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "signed-net-cash",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "AssetClass", "type": "Symbol", "role": "Key" },
            { "name": "Side", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Accrued", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Price", "type": "Decimal", "scale": 6, "role": "Economic" },
            { "name": "Fees", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" },
                { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" }, { "rule": "symbol.map", "from": "Side", "to": "Side" }
              ],
              "parse": [
                { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "decimal.parse", "from": "Px", "to": "Price" },
                { "rule": "decimal.parse", "from": "Acc", "to": "Accrued" }, { "rule": "decimal.parse", "from": "Fee", "to": "Fees" },
                { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" }
              ]
            },
            "right": {
              "identify": [
                { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" },
                { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" }, { "rule": "symbol.map", "from": "Side", "to": "Side" }
              ],
              "parse": [
                { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "decimal.parse", "from": "Px", "to": "Price" },
                { "rule": "decimal.parse", "from": "Acc", "to": "Accrued" }, { "rule": "decimal.parse", "from": "Fee", "to": "Fees" },
                { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" }
              ]
            }
          },
          "paths": [ {
            "id": "p", "route": { "rule": "route.all" },
            "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ],
            "diff": { "fields": {
              "Quantity": { "tolerance": { "relative": 0.0001 } },
              "Accrued": { "attributed": "trading.proRataToQuantity" },
              "NetCash": { "tolerance": { "attributed": "trading.netCashSigned" } }
            } }
          } ]
        }
        """;

    private static readonly RuleRegistry Trading = TradingRulePack.AddTo(RuleRegistry.CreateCore());

    private static readonly SourceMap<Row> Map = new SourceMap<Row>()
        .Text("Id", row => row.Id).Text("Ref", row => "T1").Text("Asset", row => "Bond").Text("Side", row => row.Side).Text("Qty", row => row.Qty)
        .Text("Px", row => row.Px).Text("Acc", row => row.Acc).Text("Fee", row => row.Fee).Text("Cash", row => row.Cash);

    private static readonly Row Buy = new("L1", "BUY", "1000000", "100", "4784.72", "50", "-1004834.72");

    // The price moves to 100.25, so principal grows by 2,500 and a BUY pays 2,500 more: its cash falls by 2,500.
    private static readonly Row BuyAtHigherPrice = Buy with { Id = "R1", Px = "100.25", Cash = "-1007334.72" };

    [Fact]
    public void A_signed_BUYs_net_cash_difference_that_follows_from_the_price_is_attributed_to_the_price()
    {
        var cash = NetCash(Run(Signed, Buy, BuyAtHigherPrice));

        Assert.Equal(-2500m, cash.Delta.Number);
        Assert.Equal(["Price"], cash.AttributedTo, StringComparer.Ordinal);
    }

    // The defect of #433: the unsigned identity expects +2,500 and finds -2,500, so net cash is a root of its own.
    [Fact]
    public void The_same_rows_under_trading_netCash_leave_net_cash_unexplained()
    {
        var cash = NetCash(Run(Unsigned, Buy, BuyAtHigherPrice));

        Assert.Equal(FieldOutcome.Break, cash.Outcome);
        Assert.Empty(cash.AttributedTo);
    }

    // Quantity moves by 50 on 1,000,000, inside its 0.01% tolerance, and accrued moves pro rata to 4,784.96. The BUY pays
    // 50.24 more. Every root is within tolerance, so net cash is too: the near-match of fixture 2.0.
    [Fact]
    public void A_signed_BUYs_near_match_is_WithinTolerance()
    {
        var right = Buy with { Id = "R1", Qty = "1000050", Acc = "4784.96", Cash = "-1004884.96" };

        var result = Run(Signed, Buy, right);

        var cash = NetCash(result);
        Assert.Equal(-50.24m, cash.Delta.Number);
        Assert.Equal(FieldOutcome.WithinTolerance, cash.Outcome);
        Assert.Equal(["Quantity", "Accrued"], cash.AttributedTo, StringComparer.Ordinal);
        Assert.Equal(FieldOutcome.WithinTolerance, Assert.Single(result.Entries).Outcome);
    }

    // A fee is paid whichever way the trade goes, so a larger fee lowers a BUY's cash by the same amount.
    [Fact]
    public void A_signed_BUYs_net_cash_difference_that_follows_from_the_fee_is_attributed_to_the_fee()
    {
        var right = Buy with { Id = "R1", Fee = "55", Cash = "-1004839.72" };

        var cash = NetCash(Run(Signed, Buy, right));

        Assert.Equal(-5m, cash.Delta.Number);
        Assert.Equal(["Fees"], cash.AttributedTo, StringComparer.Ordinal);
    }

    // A SELL receives cash under both conventions, so the two attributions agree on it.
    [Theory]
    [InlineData(Signed)]
    [InlineData(Unsigned)]
    public void A_SELLs_net_cash_difference_is_explained_by_both_attributions(string attribution)
    {
        var left = Buy with { Side = "SELL", Cash = "1004734.72" };
        var right = left with { Id = "R1", Px = "100.25", Cash = "1007234.72" };

        var cash = NetCash(Run(attribution, left, right));

        Assert.Equal(2500m, cash.Delta.Number);
        Assert.Equal(["Price"], cash.AttributedTo, StringComparer.Ordinal);
    }

    // Unsigned cash under the signed name is not explained: the attribution does not accept either sign.
    [Fact]
    public void A_BUY_with_unsigned_cash_is_not_explained_by_the_signed_attribution()
    {
        var left = Buy with { Cash = "1004834.72" };
        var right = BuyAtHigherPrice with { Cash = "1007334.72" };

        var cash = NetCash(Run(Signed, left, right));

        Assert.Equal(FieldOutcome.Break, cash.Outcome);
        Assert.Empty(cash.AttributedTo);
    }

    // A net-cash difference the roots do not add up to stays a root of its own.
    [Fact]
    public void A_signed_BUYs_net_cash_difference_the_roots_do_not_add_up_to_is_not_explained()
    {
        var right = BuyAtHigherPrice with { Cash = "-1007400.00" };

        var cash = NetCash(Run(Signed, Buy, right));

        Assert.Equal(FieldOutcome.Break, cash.Outcome);
        Assert.Empty(cash.AttributedTo);
    }

    [Fact]
    public void The_trading_pack_registers_the_name_and_the_core_does_not()
    {
        Assert.IsType<SignedNetCashAttribution>(Trading.Attributions.Find(Signed));
        Assert.Null(RuleRegistry.CreateCore().Attributions.Find(Signed));
    }

    [Fact]
    public void It_needs_the_roots_of_trading_netCash_and_is_finished_at_the_same_stage()
    {
        var signed = Trading.Attributions.Find(Signed)!;
        var unsigned = Trading.Attributions.Find(Unsigned)!;

        Assert.Equal(unsigned.Roots, signed.Roots, StringComparer.Ordinal);
        Assert.Equal(unsigned.Stage, signed.Stage);
    }

    private static FieldDiff NetCash(IReconciliationResult result) =>
        Assert.Single(Assert.Single(result.Entries).Diffs, diff => diff.Field.Name == "NetCash");

    // The profile names the signed attribution. The unsigned run swaps only that name.
    private static IReconciliationResult Run(string attribution, Row left, Row right) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile.Replace($"\"{Signed}\"", $"\"{attribution}\"", StringComparison.Ordinal)), Trading),
            RecordSources.FromObjects("left", [left], Map),
            RecordSources.FromObjects("right", [right], Map),
            ReferenceDataSet.Empty);

    public sealed record Row(string Id, string Side, string Qty, string Px, string Acc, string Fee, string Cash);
}
