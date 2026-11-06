using RecDiffy.Results;

namespace RecDiffy.Tests;

public class ArgonLifecycleDiagnosticTests
{
    [Fact]
    public void Lifecycle_quarantine_diagnostic_code_is_preserved_in_row_result_diagnostics()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse("""
            {
              "formatVersion": 1,
              "profileId": "life-missing-diag",
              "version": "1.0.0",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Counterparty", "type": "Symbol", "role": "Key" },
                { "name": "EventAction", "type": "Symbol", "role": "Lifecycle" },
                { "name": "EventTarget", "type": "Text", "role": "Lifecycle" }
              ],
              "normalization": {
                "right": {
                  "identify": [
                    { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                    { "rule": "symbol.map", "from": "EventAction", "to": "EventAction" },
                    { "rule": "text.copy", "from": "EventTarget", "to": "EventTarget" }
                  ]
                }
              },
              "consolidation": {
                "right": { "lifecycle": { "action": "EventAction", "target": "EventTarget", "scope": "Counterparty", "corrTarget": "CANC" } }
              },
              "paths": [ { "id": "p", "route": { "rule": "route.all" } } ]
            }
            """), RuleRegistry.CreateCore());

        var map = new SourceMap<(string Id, string Action, string Target)>()
            .Text("Id", r => r.Id)
            .Text("Counterparty", _ => "ACME")
            .Text("EventAction", r => r.Action)
            .Text("EventTarget", r => r.Target);

        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", Array.Empty<(string, string, string)>(), map),
            RecordSources.FromObjects("right", [("C1", "CANC", "MISSING")], map),
            ReferenceDataSet.Empty);

        var row = Assert.Single(result.Rows);
        Assert.Equal(Disposition.Quarantined, row.Disposition);
        Assert.Contains(row.Diagnostics, d => d.Code == "MissingLifecycleTarget");
    }
}
