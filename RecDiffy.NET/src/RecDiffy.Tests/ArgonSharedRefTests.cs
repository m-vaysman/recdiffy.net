using RecDiffy.Results;

namespace RecDiffy.Tests;

public class ArgonSharedRefTests
{
    [Fact]
    public void SharedRef_pass_ignores_broker_scheme_and_matches_shared_scheme_only()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse("""
            {
              "formatVersion": 1,
              "profileId": "shared-ref-broker-scheme",
              "version": "1.0.0",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Counterparty", "type": "Symbol", "role": "Key" },
                { "name": "TradeRef", "type": "Identifier", "role": "Identity" }
              ],
              "normalization": {
                "left": {
                  "identify": [
                    { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                    { "rule": "identifier.map", "from": "TradeRef", "to": "TradeRef", "scheme": "broker" }
                  ]
                },
                "right": {
                  "identify": [
                    { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                    { "rule": "identifier.map", "from": "TradeRef", "to": "TradeRef", "scheme": "shared" }
                  ]
                }
              },
              "paths": [ {
                "id": "p",
                "route": { "rule": "route.all" },
                "passes": [ { "id": "P1", "pass": "match.sharedRef" } ]
              } ]
            }
            """), RuleRegistry.CreateCore());

        var map = new SourceMap<(string Id, string Cpty, string Ref)>()
            .Text("Id", r => r.Id)
            .Text("Counterparty", r => r.Cpty)
            .Text("TradeRef", r => r.Ref);

        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", [("L1", "ACME", "REF-1")], map),
            RecordSources.FromObjects("right", [("R1", "ACME", "REF-1")], map),
            ReferenceDataSet.Empty);

        Assert.DoesNotContain(result.Entries, e => e.Alignment == Alignment.Matched);
    }
}
