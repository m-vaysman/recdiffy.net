using RecDiffy.Results;

namespace RecDiffy.Tests;

// Spec v3 §6.1 and §6.5: LifecycleCollapsed flags an entry whose lifecycle chain was collapsed. A known exact pair is
// OneToOne; a chain such as NEW, CANC, CORR is OneToOne + LifecycleCollapsed. A lone NEW report has nothing to collapse.
// Found by the acceptance-fixture matrix (#39): every Sonnet and fixture 2.0 match is flagged, not only the five chains.
public class LoneLifecycleEventCompositionTests
{
    [Fact]
    public void A_chain_that_collapses_is_flagged_LifecycleCollapsed()
    {
        var result = Run(
            [new("L1", "T1", null, null)],
            [new("R1", "T1", "NEW", null), new("R2", "T1", "CANC", "R1"), new("R3", "T1", "CORR", "R2")]);

        var entry = Assert.Single(result.Entries, entry => entry.Alignment == Alignment.Matched);

        Assert.Equal(Composition.OneToOne | Composition.LifecycleCollapsed, entry.Composition);
    }

    [Fact]
    public void A_lone_NEW_report_with_nothing_to_collapse_is_OneToOne()
    {
        var result = Run([new("L1", "T1", null, null)], [new("R1", "T1", "NEW", null)]);

        var entry = Assert.Single(result.Entries);

        Assert.Equal(Alignment.Matched, entry.Alignment);
        Assert.Equal(Composition.OneToOne, entry.Composition);
    }

    private sealed record Row(string Id, string Trade, string? Action, string? Target);

    private static ReconciliationResult Run(IReadOnlyList<Row> left, IReadOnlyList<Row> right) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map()),
            RecordSources.FromObjects("right", right, Map()),
            ReferenceDataSet.Empty);

    private static SourceMap<Row> Map() =>
        new SourceMap<Row>()
            .Text("Id", row => row.Id)
            .Text("Counterparty", _ => "ACME")
            .Text("Trade", row => row.Trade)
            .Text("Quantity", _ => "100")
            .Text("Action", row => row.Action)
            .Text("Target", row => row.Target);

    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "lone-lifecycle-event",
          "version": "1.0.0",
          "culture": "en-US",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Trade", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "EventAction", "type": "Symbol", "role": "Lifecycle" },
            { "name": "EventTarget", "type": "Identifier", "role": "Lifecycle" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                { "rule": "symbol.map", "from": "Trade", "to": "Trade" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            },
            "right": {
              "identify": [
                { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                { "rule": "symbol.map", "from": "Trade", "to": "Trade" },
                { "rule": "symbol.map", "from": "Action", "to": "EventAction" },
                { "rule": "identifier.map", "from": "Target", "to": "EventTarget", "scheme": "REPORT" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
            }
          },
          "consolidation": { "right": { "lifecycle": { "action": "EventAction", "target": "EventTarget", "scope": "Counterparty", "corrTarget": "CANC" } } },
          "paths": [ { "id": "trades", "route": { "rule": "route.all" }, "passes": [
            { "id": "P2", "pass": "match.naturalKey", "key": ["Counterparty", "Trade"] }
          ] } ]
        }
        """;
}
