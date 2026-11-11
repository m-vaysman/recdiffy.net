using RecDiffy.Results;

namespace RecDiffy.Tests;

public class SideBySideBlankMeasureTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "side-by-side-blank-measure",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" } ], "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ] },
            "right": { "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" } ], "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ] }
          },
          "paths": [ { "id": "payments", "route": { "rule": "route.all" }, "passes": [ { "id": "ref", "pass": "match.naturalKey", "key": ["Ref"] } ] } ]
        }
        """;

    private static readonly SourceMap<Payment> Map = new SourceMap<Payment>()
        .Text("Id", row => row.Id).Text("Ref", row => row.Ref).Text("Amount", row => row.Amount);

    [Fact]
    public void A_blank_amount_does_not_total_the_same_as_a_zero_amount()
    {
        var blank = Cell("");
        var zero = Cell("0.00");

        Assert.NotEqual(zero, blank);
        Assert.Equal(new SideBySideCell(100.00m, 100.00m, 0.00m, 0.00m, 0.00m, 0.00m), blank);
        Assert.Equal(new SideBySideCell(100.00m, 120.00m, 20.00m, 0.00m, 0.00m, 20.00m), zero);
    }

    private static SideBySideCell Cell(string ledgerAmount)
    {
        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("ledger", [new Payment("L1", "A-1", "100.00"), new Payment("L2", "A-2", ledgerAmount)], Map),
            RecordSources.FromObjects("bank", [new Payment("B1", "A-1", "100.00"), new Payment("B2", "A-2", "20.00")], Map),
            ReferenceDataSet.Empty);
        return Assert.Single(result.BuildSideBySide(new SideBySideSpec([], [], [new MeasureSpec("Amount")], null)).Lines).Cells["Amount"];
    }

    public sealed record Payment(string Id, string Ref, string Amount);
}
