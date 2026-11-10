using RecDiffy.Results;

namespace RecDiffy.Tests;

// route.byField compares equals with the field's match-key text. An Identifier's key text is its scheme and its value
// joined by a separator, so no equals value can name it, and every row on the path is Unrouted (#278).
public class RouteByIdentifierFieldTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "route-by-identifier",
          "version": "1.0.0",
          "culture": "en-US",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Desk", "type": "Identifier", "role": "Identity" },
            { "name": "Ref", "type": "Symbol", "role": "Key" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "identifier.map", "from": "Desk", "to": "Desk", "scheme": "DESK" }, { "rule": "symbol.map", "from": "Id", "to": "Ref" } ] },
            "right": { "identify": [ { "rule": "identifier.map", "from": "Desk", "to": "Desk", "scheme": "DESK" }, { "rule": "symbol.map", "from": "Id", "to": "Ref" } ] }
          },
          "paths": [
            {
              "id": "rates",
              "route": { "rule": "route.byField", "field": "Desk", "equals": "RATES" },
              "passes": [ { "id": "ref", "pass": "match.naturalKey", "key": ["Ref"] } ]
            }
          ]
        }
        """;

    [Fact(Skip = "#278: route.byField on an Identifier field accepts no row.")]
    public void A_byField_route_on_an_identifier_field_accepts_a_row_whose_value_equals_it()
    {
        var map = new SourceMap<(string Id, string Desk)>().Text("Id", row => row.Id).Text("Desk", row => row.Desk);

        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects<(string Id, string Desk)>("left", [("A", "RATES")], map),
            RecordSources.FromObjects<(string Id, string Desk)>("right", [("A", "RATES")], map),
            ReferenceDataSet.Empty);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(("rates", Alignment.Matched), (entry.PathId, entry.Alignment));
    }
}
