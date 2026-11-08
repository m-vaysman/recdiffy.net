using RecDiffy.Results;

namespace RecDiffy.Tests;

// Adversarial pass on main. Spec §3.4: DuplicateConflict is the code for conflicting duplicate rows. A row
// quarantined for any other reason, with no duplicate anywhere, should carry only its own code.
public class AdversarialQuarantineCodeTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "adv-quarantine-code",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Day", "type": "Date", "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "date.parse", "from": "Day", "to": "Day", "formats": ["yyyy-MM-dd"] } ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "date.parse", "from": "Day", "to": "Day", "formats": ["yyyy-MM-dd"] } ]
            }
          },
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ] } ]
        }
        """;

    [Theory(Skip = "🔴 #173 (#174 C): every intake quarantine also reports DuplicateConflict")]
    [InlineData("L1", "abc", "2025-06-11", "DecimalParse")]
    [InlineData("L1", "100", "11/06/2025", "DateParse")]
    [InlineData("", "100", "2025-06-11", "MissingKey")]
    public void A_quarantined_row_with_no_duplicate_carries_only_its_own_code(string id, string quantity, string day, string code)
    {
        var map = new SourceMap<(string Id, string Qty, string Day)>().Text("Id", row => row.Id).Text("Ref", row => "T1").Text("Qty", row => row.Qty).Text("Day", row => row.Day);
        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects<(string, string, string)>("left", [(id, quantity, day)], map),
            RecordSources.FromObjects<(string, string, string)>("right", [("R1", "100", "2025-06-11")], map),
            ReferenceDataSet.Empty);

        var row = Assert.Single(result.Rows, row => row.Row.Source == "left");
        Assert.Equal(Disposition.Quarantined, row.Disposition);
        Assert.Equal([code], row.Diagnostics.Select(diagnostic => diagnostic.Code), StringComparer.Ordinal);
    }
}
