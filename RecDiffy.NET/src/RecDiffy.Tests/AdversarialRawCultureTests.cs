using System.Globalization;

namespace RecDiffy.Tests;

// Adversarial pass on main. Text RecDiffy makes from a value is invariant (spec §3.5 rule 6, README "Text RecDiffy
// makes"). ReconciliationResult.Raw turns a typed raw value into text with the server's culture.
public class AdversarialRawCultureTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "adv-raw-culture",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Id", "type": "Identifier", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Day", "type": "Date", "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "id" } ],
                      "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "date.parse", "from": "Day", "to": "Day" } ] },
            "right": { "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "id" } ],
                       "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "date.parse", "from": "Day", "to": "Day" } ] }
          },
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Id"] } ] } ]
        }
        """;

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void Raw_text_of_a_typed_value_is_the_same_on_every_server(string server)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(server);
            var map = new SourceMap<(string Id, decimal Qty, DateOnly Day)>()
                .Text("Id", row => row.Id)
                .Decimal("Qty", row => row.Qty)
                .Date("Day", row => row.Day);
            var result = Reconciler.Run(
                Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
                RecordSources.FromObjects<(string Id, decimal Qty, DateOnly Day)>("left", [("T1", 1234.5m, new DateOnly(2025, 6, 11))], map),
                RecordSources.FromObjects<(string Id, decimal Qty, DateOnly Day)>("right", [("T1", 1234.5m, new DateOnly(2025, 6, 11))], map),
                ReferenceDataSet.Empty);
            var row = result.Rows.Single(row => row.Row.Source == "left").Row;

            Assert.Equal((Invariant(1234.5m), Invariant(new DateOnly(2025, 6, 11))), (result.Raw(row, "Qty"), result.Raw(row, "Day")));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // The trace already writes a raw value this way (InvariantText).
    private static string Invariant(IFormattable value) => value.ToString(null, CultureInfo.InvariantCulture);
}
