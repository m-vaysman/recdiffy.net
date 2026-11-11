using RecDiffy.Results;

namespace RecDiffy.Tests;

// #284, found by #40 while rechecking #24. route.byField compares MatchKeys.Text with the route's equals value. Since
// #133, MatchKeys.Text of an Identifier is its scheme, a record separator, then its value, so it never equals the value
// a profile writes. Compile accepts the route, and at run time every row is Unrouted.
public class RouteByIdentifierFieldTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "route-by-identifier",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "TradeId", "type": "Identifier", "role": "Key" },
            { "name": "Book", "type": "Identifier", "role": "Key" },
            { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "identifier.map", "from": "Id", "to": "TradeId", "scheme": "trade" }, { "rule": "identifier.map", "from": "Book", "to": "Book", "scheme": "book" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Id", "to": "TradeId", "scheme": "trade" }, { "rule": "identifier.map", "from": "Book", "to": "Book", "scheme": "book" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ]
            }
          },
          "paths": [ { "id": "rates", "route": { "rule": "route.byField", "field": "Book", "equals": "RATES" },
            "passes": [ { "id": "P2", "pass": "match.naturalKey", "key": ["TradeId"] } ] } ]
        }
        """;

    private static readonly SourceMap<(string Id, string Book, string Amount)> Map = new SourceMap<(string Id, string Book, string Amount)>()
        .Text("Id", row => row.Id)
        .Text("Book", row => row.Book)
        .Text("Amount", row => row.Amount);

    [Fact(Skip = "#284: route.byField on an Identifier field compares scheme and value, so it never matches.")]
    public void A_route_on_an_identifier_field_takes_the_rows_whose_value_it_names()
    {
        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", [("T1", "RATES", "100")], Map),
            RecordSources.FromObjects("right", [("T1", "RATES", "100")], Map),
            ReferenceDataSet.Empty);

        var entry = Assert.Single(result.Entries);
        Assert.Equal((Alignment.Matched, "rates"), (entry.Alignment, entry.PathId));
    }
}
