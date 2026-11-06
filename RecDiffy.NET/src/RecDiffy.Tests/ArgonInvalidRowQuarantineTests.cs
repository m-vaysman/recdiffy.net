using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

public class ArgonInvalidRowQuarantineTests
{
    [Fact]
    public void Invalid_row_from_normalization_receives_Quarantined_disposition()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(SimpleDecimalProfile), RuleRegistry.CreateCore());
        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", [new Dictionary<string, string>(StringComparer.Ordinal) { ["Id"] = "L1", ["Quantity"] = "not-a-number" }], SimpleDecimalMap()),
            RecordSources.FromObjects("right", Array.Empty<Dictionary<string, string>>(), SimpleDecimalMap()),
            ReferenceDataSet.Empty);

        var row = Assert.Single(result.Rows);
        Assert.Equal(InputValidity.Invalid, row.Validity);
        Assert.Equal(Disposition.Quarantined, row.Disposition);
        Assert.Equal(1, result.Summary.Quarantined);
    }

    private static SourceMap<Dictionary<string, string>> SimpleDecimalMap() =>
        new SourceMap<Dictionary<string, string>>()
            .Text("Id", row => row["Id"])
            .Text("Quantity", row => row["Quantity"]);

    private const string SimpleDecimalProfile = """
        {
          "formatVersion": 1,
          "profileId": "simple-decimal",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [ { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" } ],
          "normalization": {
            "left": { "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
            "right": { "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
          },
          "paths": [ { "id": "p", "route": { "rule": "route.all" } } ]
        }
        """;
}
