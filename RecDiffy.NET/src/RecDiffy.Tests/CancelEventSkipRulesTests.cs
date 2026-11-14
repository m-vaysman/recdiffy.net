using RecDiffy.Results;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

public class CancelEventSkipRulesTests
{
    [Fact]
    public void Cancel_event_skips_parse_derive_and_validate_rules_and_keeps_identify_fields()
    {
        var result = RunWithLifecycle(
        [
            new("R1", "ACME", "Bond", "NEW", null, "B", "100", "2026-06-10"),
            new("R2", "ACME", "Bond", "CANC", "R1", "B", "-100", "not-a-date"),
            new("R3", "ACME", "Bond", "CORR", "R2", "B", "100", "2026-06-10"),
        ]);

        Assert.DoesNotContain(result.Rows, row => row.Disposition == Disposition.Quarantined);
        Assert.Equal(Disposition.Matched, Of(result, "R3").Disposition);
        Assert.Equal((Disposition.Suppressed, SuppressionReason.Superseded), (Of(result, "R1").Disposition, Of(result, "R1").Suppression));
        Assert.Equal((Disposition.Suppressed, SuppressionReason.CancelEvent), (Of(result, "R2").Disposition, Of(result, "R2").Suppression));
    }

    [Fact]
    public void Cancel_event_with_missing_required_trade_fields_skips_required_present_and_collapses_chain()
    {
        var result = RunWithLifecycle(
        [
            new("R1", "ACME", "Bond", "NEW", null, "B", "100", "2026-06-10"),
            new("R2", "ACME", "Bond", "CANC", "R1", "", "", ""),
            new("R3", "ACME", "Bond", "CORR", "R2", "B", "100", "2026-06-10"),
        ]);

        Assert.DoesNotContain(result.Rows, row => row.Disposition == Disposition.Quarantined);
        Assert.Equal(Disposition.Matched, Of(result, "R3").Disposition);
        Assert.Equal(Disposition.Suppressed, Of(result, "R1").Disposition);
        Assert.Equal(Disposition.Suppressed, Of(result, "R2").Disposition);
    }

    [Fact]
    public void Cancel_event_still_runs_identify_rules_and_quarantines_chain_when_identify_fails()
    {
        var result = RunWithLifecycle(
        [
            new("R1", "ACME", "Bond", "NEW", null, "B", "100", "2026-06-10"),
            new("R2", "ACME", "UNKNOWN_ASSET", "CANC", "R1", "B", "-100", "2026-06-10"),
            new("R3", "ACME", "Bond", "CORR", "R2", "B", "100", "2026-06-10"),
        ]);

        Assert.Equal(Disposition.Quarantined, Of(result, "R2").Disposition);
        Assert.Contains(Of(result, "R2").Diagnostics, diagnostic => diagnostic.Code == "UnknownSymbol");
        Assert.Equal(Disposition.Quarantined, Of(result, "R1").Disposition);
        Assert.Equal(Disposition.Quarantined, Of(result, "R3").Disposition);
    }

    [Fact]
    public void Correction_event_with_reversal_sign_still_runs_trade_rules_and_quarantines_chain()
    {
        var result = RunWithLifecycle(
        [
            new("R1", "ACME", "Bond", "NEW", null, "B", "100", "2026-06-10"),
            new("R2", "ACME", "Bond", "CANC", "R1", "B", "-100", "2026-06-10"),
            new("R3", "ACME", "Bond", "CORR", "R2", "B", "-100", "2026-06-10"),
        ]);

        Assert.Equal(Disposition.Quarantined, Of(result, "R3").Disposition);
        Assert.Contains(Of(result, "R3").Diagnostics, diagnostic => diagnostic.Code == "SideSignConflict");
        Assert.Equal(Disposition.Quarantined, Of(result, "R1").Disposition);
        Assert.Equal(Disposition.Quarantined, Of(result, "R2").Disposition);
    }

    [Fact]
    public void Side_without_lifecycle_plan_runs_trade_rules_even_when_a_field_holds_canc()
    {
        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(ProfileWithoutLifecycle), TradingRulePack.AddTo(RuleRegistry.CreateCore())),
            RecordSources.FromObjects("left", [new Row("L1", "ACME", "Bond", "CANC", null, "B", "-100", "2026-06-10")], Map()),
            RecordSources.FromObjects("right", [new Row("R1", "ACME", "Bond", "NEW", null, "B", "100", "2026-06-10")], Map()),
            ReferenceDataSet.Empty);

        var left = Assert.Single(result.Rows, row => row.Row.RowKey == "L1");
        Assert.Equal(Disposition.Quarantined, left.Disposition);
        Assert.Contains(left.Diagnostics, diagnostic => diagnostic.Code == "SideSignConflict");
    }

    private static RowResult Of(IReconciliationResult result, string key) =>
        Assert.Single(result.Rows, row => row.Row.RowKey == key);

    private sealed record Row(
        string Id,
        string Counterparty,
        string Asset,
        string? Action,
        string? Target,
        string Code,
        string Quantity,
        string TradeDate);

    private static IReconciliationResult RunWithLifecycle(IReadOnlyList<Row> right) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(ProfileWithLifecycle), TradingRulePack.AddTo(RuleRegistry.CreateCore())),
            RecordSources.FromObjects("left", [new Row("L1", "ACME", "Bond", null, null, "BUY", "100", "2026-06-10")], Map()),
            RecordSources.FromObjects("right", right, Map()),
            ReferenceDataSet.Empty);

    private static SourceMap<Row> Map() =>
        new SourceMap<Row>()
            .Text("Id", row => row.Id)
            .Text("Counterparty", row => row.Counterparty)
            .Text("Asset", row => row.Asset)
            .Text("Action", row => row.Action)
            .Text("Target", row => row.Target)
            .Text("Code", row => row.Code)
            .Text("Qty", row => row.Quantity)
            .Text("TradeDate", row => row.TradeDate);

    private const string ProfileWithLifecycle = """
        {
          "formatVersion": 1,
          "profileId": "cancel-skip-trade-rules-with-lifecycle",
          "version": "1.0.0",
          "culture": "en-US",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "AssetClass", "type": "Symbol", "role": "Key" },
            { "name": "Side", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Key" },
            { "name": "TradeDate", "type": "Date", "role": "Economic" },
            { "name": "EventAction", "type": "Symbol", "role": "Lifecycle" },
            { "name": "EventTarget", "type": "Identifier", "role": "Lifecycle" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                { "rule": "symbol.map", "from": "Asset", "to": "AssetClass", "passthrough": false, "map": { "Bond": "Bond" } }
              ],
              "parse": [
                { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] }
              ],
              "derive": [
                { "rule": "side.resolve", "code": "Code", "quantity": "Qty" },
                { "rule": "quantity.absolute", "from": "Qty", "to": "Quantity" }
              ],
              "validate": [
                { "rule": "required.present", "fields": ["Side", "Quantity", "TradeDate"] }
              ]
            },
            "right": {
              "identify": [
                { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                { "rule": "symbol.map", "from": "Action", "to": "EventAction" },
                { "rule": "symbol.map", "from": "Asset", "to": "AssetClass", "passthrough": false, "map": { "Bond": "Bond" } },
                { "rule": "identifier.map", "from": "Target", "to": "EventTarget", "scheme": "REPORT" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] }
              ],
              "derive": [
                { "rule": "side.resolve", "code": "Code", "quantity": "Qty" },
                { "rule": "quantity.absolute", "from": "Qty", "to": "Quantity" }
              ],
              "validate": [
                { "rule": "required.present", "fields": ["Side", "Quantity", "TradeDate"] }
              ]
            }
          },
          "consolidation": { "right": { "lifecycle": { "action": "EventAction", "target": "EventTarget", "scope": "Counterparty", "corrTarget": "CANC" } } },
          "paths": [ { "id": "trades", "route": { "rule": "route.all" }, "passes": [
            { "id": "P2", "pass": "match.naturalKey", "key": ["Counterparty", "AssetClass", "Side", "Quantity"] }
          ] } ]
        }
        """;

    private const string ProfileWithoutLifecycle = """
        {
          "formatVersion": 1,
          "profileId": "cancel-skip-trade-rules-without-lifecycle",
          "version": "1.0.0",
          "culture": "en-US",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "AssetClass", "type": "Symbol", "role": "Key" },
            { "name": "Side", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Key" },
            { "name": "EventAction", "type": "Symbol", "role": "Lifecycle" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" },
                { "rule": "symbol.map", "from": "Action", "to": "EventAction" }
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
                { "rule": "symbol.map", "from": "Action", "to": "EventAction" }
              ],
              "derive": [
                { "rule": "side.resolve", "code": "Code", "quantity": "Qty" },
                { "rule": "quantity.absolute", "from": "Qty", "to": "Quantity" }
              ]
            }
          },
          "paths": [ { "id": "trades", "route": { "rule": "route.all" }, "passes": [
            { "id": "P2", "pass": "match.naturalKey", "key": ["Counterparty", "AssetClass", "Side", "Quantity"] }
          ] } ]
        }
        """;
}
