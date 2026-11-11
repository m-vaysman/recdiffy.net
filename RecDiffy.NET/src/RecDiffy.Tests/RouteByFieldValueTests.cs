using RecDiffy.Results;

namespace RecDiffy.Tests;

// #284. route.byField compares the value a profile writes in equals with the field's value. For an Identifier that is
// the value alone, not the scheme and the value that pair keys use. The schemes differ between the two sides here, so
// a route that read the scheme could not take both rows.
public class RouteByFieldValueTests
{
    private const string Template = """
        {
          "formatVersion": 1,
          "profileId": "route-by-field-value",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "TradeId", "type": "Identifier", "role": "Key" },
            { "name": "Book", "type": "Identifier", "role": "Key" },
            { "name": "Desk", "type": "Symbol", "role": "Key" },
            { "name": "Name", "type": "Text", "role": "Text" },
            { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "TradeDate", "type": "Date", "role": "Key" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "identifier.map", "from": "Id", "to": "TradeId", "scheme": "trade" },
                { "rule": "identifier.map", "from": "Book", "to": "Book", "scheme": "book" },
                { "rule": "symbol.map", "from": "Desk", "to": "Desk" },
                { "rule": "text.copy", "from": "Name", "to": "Name" }
              ],
              "parse": [
                { "rule": "decimal.parse", "from": "Amount", "to": "Amount" },
                { "rule": "date.parse", "from": "Date", "to": "TradeDate", "formats": ["yyyy-MM-dd"] }
              ]
            },
            "right": {
              "identify": [
                { "rule": "identifier.map", "from": "Id", "to": "TradeId", "scheme": "trade" },
                { "rule": "identifier.map", "from": "Book", "to": "Book", "scheme": "desk" },
                { "rule": "symbol.map", "from": "Desk", "to": "Desk" },
                { "rule": "text.copy", "from": "Name", "to": "Name" }
              ],
              "parse": [
                { "rule": "decimal.parse", "from": "Amount", "to": "Amount" },
                { "rule": "date.parse", "from": "Date", "to": "TradeDate", "formats": ["yyyy-MM-dd"] }
              ]
            }
          },
          "paths": [ { "id": "rates", "route": { "rule": "route.byField", "field": "Book", "equals": "RATES" },
            "passes": [ { "id": "P2", "pass": "match.naturalKey", "key": ["TradeId"] } ] } ]
        }
        """;

    private const string TemplateRoute = """{ "rule": "route.byField", "field": "Book", "equals": "RATES" }""";

    private static readonly SourceMap<(string Id, string Book, string Desk, string Name, string Amount, string Date)> Map =
        new SourceMap<(string Id, string Book, string Desk, string Name, string Amount, string Date)>()
            .Text("Id", row => row.Id)
            .Text("Book", row => row.Book)
            .Text("Desk", row => row.Desk)
            .Text("Name", row => row.Name)
            .Text("Amount", row => row.Amount)
            .Text("Date", row => row.Date);

    private static (string Id, string Book, string Desk, string Name, string Amount, string Date) Trade(string id, string book) =>
        (id, book, "FX", "Acme Ltd", "100", "2026-11-10");

    [Theory]
    [InlineData("Book", "RATES")]
    [InlineData("Desk", "FX")]
    [InlineData("Name", "Acme Ltd")]
    [InlineData("Amount", "100.00")]
    [InlineData("TradeDate", "2026-11-10")]
    public void A_route_takes_the_rows_whose_field_value_it_names(string field, string equals)
    {
        var result = Run(field, equals, [Trade("T1", "RATES")], [Trade("T1", "RATES")]);

        var entry = Assert.Single(result.Entries);
        Assert.Equal((Alignment.Matched, "rates"), (entry.Alignment, entry.PathId));
    }

    [Fact]
    public void A_route_on_an_identifier_takes_the_row_of_either_scheme_and_leaves_the_others()
    {
        var result = Run("Book", "RATES", [Trade("T1", "RATES"), Trade("T2", "CREDIT")], [Trade("T1", "RATES"), Trade("T2", "CREDIT")]);

        var entries = result.Entries
            .Select(entry => (entry.Alignment, entry.PathId))
            .OrderBy(entry => entry.PathId, StringComparer.Ordinal)
            .ThenBy(entry => entry.Alignment)
            .ToArray();
        Assert.Equal([(Alignment.LeftOnly, ""), (Alignment.RightOnly, ""), (Alignment.Matched, "rates")], entries);
    }

    [Fact]
    public void A_route_does_not_read_the_scheme_of_an_identifier()
    {
        var result = Run("Book", "book\\u001eRATES", [Trade("T1", "RATES")], [Trade("T1", "RATES")]);

        var entries = result.Entries.Select(entry => (entry.Alignment, entry.PathId)).OrderBy(entry => entry.Alignment).ToArray();
        Assert.Equal([(Alignment.LeftOnly, ""), (Alignment.RightOnly, "")], entries);
    }

    [Fact]
    public void A_blank_identifier_is_not_routed()
    {
        var result = Run("Book", "RATES", [Trade("T1", "")], [Trade("T1", "")]);

        var entries = result.Entries.Select(entry => (entry.Alignment, entry.PathId)).OrderBy(entry => entry.Alignment).ToArray();
        Assert.Equal([(Alignment.LeftOnly, ""), (Alignment.RightOnly, "")], entries);
    }

    private static ReconciliationResult Run(
        string field,
        string equals,
        (string Id, string Book, string Desk, string Name, string Amount, string Date)[] left,
        (string Id, string Book, string Desk, string Name, string Amount, string Date)[] right) =>
        Reconciler.Run(
            Reconciler.Compile(
                ProfileDocument.Parse(Template.Replace(TemplateRoute, $$"""{ "rule": "route.byField", "field": "{{field}}", "equals": "{{equals}}" }""", StringComparison.Ordinal)),
                RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", left, Map),
            RecordSources.FromObjects("right", right, Map),
            ReferenceDataSet.Empty);
}
