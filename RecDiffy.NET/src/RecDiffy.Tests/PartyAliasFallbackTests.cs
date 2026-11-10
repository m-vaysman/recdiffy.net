using System.Collections.Immutable;
using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

public class PartyAliasFallbackTests
{
    // party.resolveAlias writes only when Counterparty is still empty, so it reads as the fallback for a row with no LEI.
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "party-alias-fallback",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Symbol", "role": "Key" },
            { "name": "Counterparty", "type": "Identifier", "role": "Key" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "symbol.map", "from": "Ref", "to": "Ref" }, { "rule": "party.resolveLei", "from": "Lei", "to": "Counterparty" }, { "rule": "party.resolveAlias", "from": "Name", "to": "Counterparty" } ] },
            "right": { "identify": [ { "rule": "symbol.map", "from": "Ref", "to": "Ref" }, { "rule": "party.resolveLei", "from": "Lei", "to": "Counterparty" } ] }
          },
          "paths": [ { "id": "trades", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ] } ]
        }
        """;

    private static readonly ReferenceDataSet Parties = ReferenceDataSet.Create(
        "parties",
        new DateOnly(2026, 1, 1),
        "parties-v1",
        ImmutableDictionary.CreateRange(StringComparer.Ordinal, [KeyValuePair.Create("529900ACME0000000001", "ACME")]),
        ImmutableDictionary.CreateRange(StringComparer.Ordinal, [KeyValuePair.Create("Acme Corp", "ACME")]),
        ImmutableDictionary<string, string>.Empty);

    private static readonly SourceMap<(string Id, string Ref, string Lei, string Name)> Map = new SourceMap<(string Id, string Ref, string Lei, string Name)>()
        .Text("Id", row => row.Id).Text("Ref", row => row.Ref).Text("Lei", row => row.Lei).Text("Name", row => row.Name);

    [Fact(Skip = "🔴 #291: neither party rule declares a read of to, so the alias fallback is AmbiguousWrite.")]
    public void An_alias_resolves_the_counterparty_of_a_row_whose_LEI_is_blank()
    {
        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), TradingRulePack.AddTo(RuleRegistry.CreateCore())),
            RecordSources.FromObjects("left", [("L1", "T-1", "529900ACME0000000001", ""), ("L2", "T-2", "", "Acme Corp")], Map),
            RecordSources.FromObjects("right", [("R1", "T-1", "529900ACME0000000001", ""), ("R2", "T-2", "529900ACME0000000001", "")], Map),
            Parties);

        Assert.All(result.Rows.Where(row => row.Row.Source == "left"), row => Assert.Equal("ACME", result.Canonical(row.Row).Text("Counterparty")));
        Assert.Equal(2, result.Summary.Exact);
    }
}
