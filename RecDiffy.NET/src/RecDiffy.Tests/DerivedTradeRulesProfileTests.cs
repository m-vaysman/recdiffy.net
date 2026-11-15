using RecDiffy.Results;
using RecDiffy.Rules;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

// price.dirtyToClean, cash.toPerspective and fees.inferFromNet as a profile names them: registered by the trading pack,
// ordered by what each reads (spec v3.1 §3.3), and applied under a `when`. The right side reports a dealer-view dirty price
// with signed cash and no fee; the left side is the internal clean price with its commission.
public class DerivedTradeRulesProfileTests
{
    private static readonly RuleRegistry Registry = TradingRulePack.AddTo(RuleRegistry.CreateCore());

    private sealed record Trade(string Id, string Quote, string Price, string Accrued, string Qty, string Net, string Perspective, string Fees);

    // 1,000,000 face of a bond at clean 98.75: 987,500.00 principal, 1,375.00 accrued, 50.00 fee. Dirty is 98.8875.
    private static readonly Trade Internal = new("T1", "CLEAN", "98.75", "1375.00", "1000000", "-988925.00", "CLIENT_VIEW", "50.00");

    private static readonly Trade Street = new("T1", "DIRTY", "98.8875", "1375.00", "1000000", "988925.00", "DEALER_BILATERAL_VIEW", "");

    [Fact]
    public void A_dealer_view_dirty_row_with_no_fee_reconciles_exactly_with_its_internal_trade() =>
        Assert.Equal((1, 0, 0), Counts(Run(Internal, Street)));

    [Fact]
    public void A_fee_the_street_cash_embeds_and_the_internal_fee_disagrees_on_is_a_break() =>
        Assert.Equal((0, 0, 1), Counts(Run(Internal, Street with { Net = "989375.00" })));

    [Fact]
    public void A_dirty_price_that_is_not_cleaned_would_break()
    {
        var withoutCleaning = Street with { Quote = "CLEAN" };

        Assert.Equal((0, 0, 1), Counts(Run(Internal, withoutCleaning)));
    }

    [Fact]
    public void Cash_that_is_not_negated_for_a_dealer_view_would_break()
    {
        var withoutNegation = Street with { Perspective = "CLIENT_VIEW" };

        Assert.Equal((0, 0, 1), Counts(Run(Internal, withoutNegation)));
    }

    // Compile rejects a rule that reads a field no earlier rule wrote (FieldReadBeforeWrite), so these are the contract
    // §3.3 gives each rule: what it needs, and the one field it writes.
    [Theory]
    [InlineData("price.dirtyToClean", "Price,Accrued,Quantity", "Price")]
    [InlineData("fees.inferFromNet", "Side,AssetClass,Quantity,Price,Accrued,NetCash", "Fees")]
    [InlineData("cash.toPerspective", "NetCash", "NetCash")]
    public void Each_rule_is_a_derive_rule_that_reads_what_the_spec_says_it_needs(string rule, string reads, string writes)
    {
        var built = new RuleBench(rule).With("to", writes).With("invertWhen", "DEALER_BILATERAL_VIEW").Build();

        Assert.Equal(RulePhase.Derive, built.Phase);
        Assert.Equal(reads.Split(','), built.Reads.Select(field => field.Name), StringComparer.Ordinal);
        Assert.Equal([writes], built.Writes.Select(field => field.Name), StringComparer.Ordinal);
    }

    private static (int Exact, int Within, int Break) Counts(IReconciliationResult result) =>
        (result.Summary.Exact, result.Summary.WithinTolerance, result.Summary.Break);

    private static IReconciliationResult Run(Trade left, Trade right)
    {
        var map = new SourceMap<Trade>()
            .Text("Id", row => row.Id)
            .Text("Quote", row => row.Quote)
            .Text("Price", row => row.Price)
            .Text("Accrued", row => row.Accrued)
            .Text("Qty", row => row.Qty)
            .Text("Net", row => row.Net)
            .Text("Reporting_Perspective", row => row.Perspective)
            .Text("Fees", row => row.Fees)
            .Text("Side", _ => "BUY")
            .Text("Asset", _ => "Bond");
        return Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), Registry),
            RecordSources.FromObjects("left", [left], map),
            RecordSources.FromObjects("right", [right], map),
            ReferenceDataSet.Empty);
    }

    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "derived-trade-rules",
          "version": "1.0.0",
          "rounding": { "midpoint": "AwayFromZero" },
          "culture": "en-US",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Id", "type": "Identifier", "role": "Key" },
            { "name": "AssetClass", "type": "Symbol", "role": "Economic" },
            { "name": "Side", "type": "Symbol", "role": "Economic" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Price", "type": "Decimal", "scale": 6, "role": "Economic" },
            { "name": "Accrued", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Fees", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "ID" },
                { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" },
                { "rule": "symbol.map", "from": "Side", "to": "Side" }
              ],
              "parse": [
                { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" },
                { "rule": "decimal.parse", "from": "Price", "to": "Price" },
                { "rule": "decimal.parse", "from": "Accrued", "to": "Accrued" },
                { "rule": "decimal.parse", "from": "Fees", "to": "Fees" },
                { "rule": "decimal.parse", "from": "Net", "to": "NetCash" }
              ]
            },
            "right": {
              "identify": [
                { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "ID" },
                { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" },
                { "rule": "symbol.map", "from": "Side", "to": "Side" }
              ],
              "parse": [
                { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" },
                { "rule": "decimal.parse", "from": "Price", "to": "Price" },
                { "rule": "decimal.parse", "from": "Accrued", "to": "Accrued" },
                { "rule": "decimal.parse", "from": "Net", "to": "NetCash" }
              ],
              "derive": [
                { "rule": "price.dirtyToClean", "to": "Price", "when": { "field": "Quote", "equals": "DIRTY" } },
                { "rule": "cash.toPerspective", "to": "NetCash", "perspective": "Reporting_Perspective", "invertWhen": "DEALER_BILATERAL_VIEW" },
                { "rule": "fees.inferFromNet", "to": "Fees" }
              ]
            }
          },
          "paths": [ { "id": "all", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": [ "Id" ] } ] } ]
        }
        """;
}
