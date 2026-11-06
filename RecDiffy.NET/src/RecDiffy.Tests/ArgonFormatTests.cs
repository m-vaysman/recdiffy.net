using RecDiffy.Results;

namespace RecDiffy.Tests;

public class ArgonFormatTests
{
    [Fact]
    public void Format_resolves_symbols_and_identifiers()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse("""
            {
              "formatVersion": 1,
              "profileId": "format-symbol",
              "version": "1.0.0",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Side", "type": "Symbol", "role": "Key" },
                { "name": "Uti", "type": "Identifier", "role": "Identity" }
              ],
              "normalization": {
                "left": {
                  "identify": [
                    { "rule": "symbol.map", "from": "Side", "to": "Side" },
                    { "rule": "identifier.map", "from": "Uti", "to": "Uti", "scheme": "UTI" }
                  ]
                }
              },
              "paths": [ { "id": "p", "route": { "rule": "route.all" } } ]
            }
            """), RuleRegistry.CreateCore());

        var map = new SourceMap<(string Id, string Side, string Uti)>()
            .Text("Id", r => r.Id)
            .Text("Side", r => r.Side)
            .Text("Uti", r => r.Uti);

        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", [("L1", "BUY", "UTI-123")], map),
            RecordSources.FromObjects("right", Array.Empty<(string, string, string)>(), map),
            ReferenceDataSet.Empty);

        var canonical = result.Canonical(result.Rows[0].Row);
        Assert.Equal("BUY", result.Format(canonical["Side"]));
        Assert.Equal("UTI-123", result.Format(canonical["Uti"]));
    }
}
