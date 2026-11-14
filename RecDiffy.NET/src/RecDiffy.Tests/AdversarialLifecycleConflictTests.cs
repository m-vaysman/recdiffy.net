using RecDiffy.Results;

namespace RecDiffy.Tests;

// Adversarial pass on main, through the real intake. Spec §3.2: "A conflicting copy is a lifecycle event: the whole
// chain is quarantined"; §5.1 rule 5 gives ChainMemberQuarantined; §12.4: "conflicting duplicate CANC: whole chain
// quarantined". Intake quarantines conflicting copies before any rule runs, so they carry no EventAction and
// lifecycle never sees them as events.
public class AdversarialLifecycleConflictTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "adv-lifecycle-conflict",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "EventAction", "type": "Symbol", "role": "Lifecycle" },
            { "name": "EventTarget", "type": "Text", "role": "Lifecycle" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "symbol.map", "from": "Cpty", "to": "Counterparty" }, { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ]
            },
            "right": {
              "identify": [
                { "rule": "symbol.map", "from": "Cpty", "to": "Counterparty" },
                { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" },
                { "rule": "symbol.map", "from": "Action", "to": "EventAction" },
                { "rule": "text.copy", "from": "Target", "to": "EventTarget" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ]
            }
          },
          "consolidation": { "right": { "lifecycle": { "action": "EventAction", "target": "EventTarget", "scope": "Counterparty", "corrTarget": "CANC" } } },
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ] } ]
        }
        """;

    private static readonly SourceMap<Row> Map = new SourceMap<Row>()
        .Text("Id", row => row.Id).Text("Cpty", row => "ACME").Text("Ref", row => row.Ref)
        .Text("Action", row => row.Action).Text("Target", row => row.Target).Text("Qty", row => row.Qty);

    // E2 cancels E1, and E2 arrives twice with different quantities: a conflicting duplicate CANC.
    [Fact]
    public void A_conflicting_duplicate_cancel_quarantines_the_trade_it_cancels()
    {
        var result = Run(
            [new("L1", "T1", "", "", "100")],
            [new("E1", "T1", "NEW", "", "100"), new("E2", "T1", "CANC", "E1", "100"), new("E2", "T1", "CANC", "E1", "200")]);

        Assert.All(result.Rows.Where(row => row.Row.Source == "right"), row =>
            Assert.True(row.Disposition == Disposition.Quarantined, $"{row.Row.RowKey}#{row.Row.Ordinal}: {row.Disposition} [{string.Join(",", row.Diagnostics.Select(d => d.Code))}]"));
    }

    // E1 arrives twice with different quantities, and E2 cancels it. E1 exists, so E2's target is not missing.
    [Fact]
    public void A_cancel_of_a_conflicting_duplicate_reports_ChainMemberQuarantined_not_a_missing_target()
    {
        var result = Run(
            [],
            [new("E1", "T1", "NEW", "", "100"), new("E1", "T1", "NEW", "", "200"), new("E2", "T1", "CANC", "E1", "100")]);

        var cancel = Assert.Single(result.Rows, row => row.Row.RowKey == "E2");
        Assert.Equal(Disposition.Quarantined, cancel.Disposition);
        Assert.Equal(["ChainMemberQuarantined"], cancel.Diagnostics.Select(diagnostic => diagnostic.Code), StringComparer.Ordinal);
    }

    private static IReconciliationResult Run(Row[] left, Row[] right) =>
        Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", right, Map),
            ReferenceDataSet.Empty);

    public sealed record Row(string Id, string Ref, string Action, string Target, string Qty);
}
