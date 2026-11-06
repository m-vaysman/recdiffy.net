using RecDiffy.Excel.Tests.Support;
using RecDiffy.Results;

namespace RecDiffy.Excel.Tests;

public class ArgonLifecycleChainsTests
{
    private static readonly DateOnly AsOf = new(2026, 11, 4);

    [Fact]
    public void LifecycleChains_report_handles_both_sides_collapsed_in_one_matched_entry()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse("""
            {
              "formatVersion": 1,
              "profileId": "both-sides-lifecycle",
              "version": "1.0.0",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Counterparty", "type": "Symbol", "role": "Key" },
                { "name": "EventAction", "type": "Symbol", "role": "Lifecycle" },
                { "name": "EventTarget", "type": "Text", "role": "Lifecycle" },
                { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
              ],
              "normalization": {
                "left": {
                  "identify": [
                    { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                    { "rule": "symbol.map", "from": "EventAction", "to": "EventAction" },
                    { "rule": "text.copy", "from": "EventTarget", "to": "EventTarget" }
                  ],
                  "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
                },
                "right": {
                  "identify": [
                    { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                    { "rule": "symbol.map", "from": "EventAction", "to": "EventAction" },
                    { "rule": "text.copy", "from": "EventTarget", "to": "EventTarget" }
                  ],
                  "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ]
                }
              },
              "consolidation": {
                "left": { "lifecycle": { "action": "EventAction", "target": "EventTarget", "scope": "Counterparty", "corrTarget": "CANC" } },
                "right": { "lifecycle": { "action": "EventAction", "target": "EventTarget", "scope": "Counterparty", "corrTarget": "CANC" } }
              },
              "paths": [ {
                "id": "p",
                "route": { "rule": "route.all" },
                "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Counterparty", "Quantity"] } ]
              } ]
            }
            """), RuleRegistry.CreateCore());

        var map = new SourceMap<(string Id, string Action, string? Target, string Qty)>()
            .Text("Id", r => r.Id)
            .Text("Counterparty", _ => "ACME")
            .Text("EventAction", r => r.Action)
            .Text("EventTarget", r => r.Target)
            .Text("Quantity", r => r.Qty);

        var leftRows = new[]
        {
            ("L1n", "NEW", (string?)null, "50"),
            ("L1c", "CANC", "L1n", "50"),
            ("L1r", "CORR", "L1c", "100"),
        };
        var rightRows = new[]
        {
            ("R1n", "NEW", (string?)null, "80"),
            ("R1c", "CANC", "R1n", "80"),
            ("R1r", "CORR", "R1c", "100"),
        };

        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", leftRows, map),
            RecordSources.FromObjects("right", rightRows, map),
            ReferenceDataSet.Empty);

        using var ms = new MemoryStream();
        var outcome = result.WriteReport(ReportKind.LifecycleChains, ms, new ExcelReportOptions { AsOf = AsOf, Profile = profile });
        Assert.True(outcome.Written);

        var path = Path.Combine(Paths.Scratch("both-life"), outcome.FileName);
        File.WriteAllBytes(path, ms.ToArray());
        var sheet = Book.Read(path)["BO4_LifecycleChains"];
        Assert.Contains(sheet.Values, c => c.Value == "L1n");
        Assert.Contains(sheet.Values, c => c.Value == "R1n");
    }
}
