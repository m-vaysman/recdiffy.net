using RecDiffy.Results;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

// Spec v3 §3.3: "Rows that carry only a lifecycle event (a CANC) skip the trade rules. A cancellation does not have to be a
// valid trade." Fixture 2.0 (§9.2) reverses the NEW in its CANC: quantity -18,900,000 under the NEW's code B. Found by the
// acceptance-fixture matrix (#39): that CANC fails side.resolve with SideSignConflict, and the whole chain is quarantined.
public class CancelEventTradeRuleTests
{
    // The control: a CANC whose code and sign agree passes the trade rules, and the chain collapses to the CORR.
    [Fact]
    public void A_cancel_whose_code_and_sign_agree_collapses_its_chain()
    {
        var result = Run([new("R1", "NEW", null, "B", "100"), new("R2", "CANC", "R1", "", "-100"), new("R3", "CORR", "R2", "B", "100")]);

        Assert.Equal(Disposition.Matched, Of(result, "R3"));
        Assert.Equal(Disposition.Suppressed, Of(result, "R1"));
        Assert.Equal(Disposition.Suppressed, Of(result, "R2"));
    }

    [Fact]
    public void A_cancel_that_reverses_the_new_skips_the_trade_rules_and_collapses_its_chain()
    {
        var result = Run([new("R1", "NEW", null, "B", "100"), new("R2", "CANC", "R1", "B", "-100"), new("R3", "CORR", "R2", "B", "100")]);

        Assert.DoesNotContain(result.Rows, row => row.Disposition == Disposition.Quarantined);
        Assert.Equal(Disposition.Matched, Of(result, "R3"));
        Assert.Equal(Disposition.Suppressed, Of(result, "R1"));
        Assert.Equal(Disposition.Suppressed, Of(result, "R2"));
    }

    private static Disposition Of(IReconciliationResult result, string key) =>
        Assert.Single(result.Rows, row => row.Row.RowKey == key).Disposition;

    private sealed record Row(string Id, string? Action, string? Target, string Code, string Quantity);

    private static IReconciliationResult Run(IReadOnlyList<Row> right) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), TradingRulePack.AddTo(RuleRegistry.CreateCore())),
            RecordSources.FromObjects("left", [new Row("L1", null, null, "BUY", "100")], Map()),
            RecordSources.FromObjects("right", right, Map()),
            ReferenceDataSet.Empty);

    private static SourceMap<Row> Map() =>
        new SourceMap<Row>()
            .Text("Id", row => row.Id)
            .Text("Counterparty", _ => "ACME")
            .Text("Asset", _ => "Bond")
            .Text("Code", row => row.Code)
            .Text("Qty", row => row.Quantity)
            .Text("Action", row => row.Action)
            .Text("Target", row => row.Target);

    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "cancel-event-trade-rules",
          "version": "1.0.0",
          "culture": "en-US",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "AssetClass", "type": "Symbol", "role": "Key" },
            { "name": "Side", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Key" },
            { "name": "EventAction", "type": "Symbol", "role": "Lifecycle" },
            { "name": "EventTarget", "type": "Identifier", "role": "Lifecycle" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" }
              ],
              "derive": [
                { "rule": "side.resolve", "code": "Code", "quantity": "Qty" },
                { "rule": "quantity.absolute", "from": "Qty", "to": "Quantity" }
              ]
            },
            "right": {
              "identify": [
                { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" },
                { "rule": "symbol.map", "from": "Action", "to": "EventAction" },
                { "rule": "identifier.map", "from": "Target", "to": "EventTarget", "scheme": "REPORT" }
              ],
              "derive": [
                { "rule": "side.resolve", "code": "Code", "quantity": "Qty" },
                { "rule": "quantity.absolute", "from": "Qty", "to": "Quantity" }
              ]
            }
          },
          "consolidation": { "right": { "lifecycle": { "action": "EventAction", "target": "EventTarget", "scope": "Counterparty", "corrTarget": "CANC" } } },
          "paths": [ { "id": "trades", "route": { "rule": "route.all" }, "passes": [
            { "id": "P2", "pass": "match.naturalKey", "key": ["Counterparty", "AssetClass", "Side", "Quantity"] }
          ] } ]
        }
        """;
}
