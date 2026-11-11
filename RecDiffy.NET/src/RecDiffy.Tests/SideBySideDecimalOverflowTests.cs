using RecDiffy.Results;

namespace RecDiffy.Tests;

public class SideBySideDecimalOverflowTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "side-by-side-overflow",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "Amount", "type": "Decimal", "scale": 0, "role": "Economic" },
            { "name": "Fee", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" } ],
              "parse": [
                { "rule": "decimal.parse", "from": "Amount", "to": "Amount" },
                { "rule": "decimal.parse", "from": "Fee", "to": "Fee" }
              ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" } ],
              "parse": [
                { "rule": "decimal.parse", "from": "Amount", "to": "Amount" },
                { "rule": "decimal.parse", "from": "Fee", "to": "Fee" }
              ]
            }
          },
          "paths": [ { "id": "payments", "route": { "rule": "route.all" }, "passes": [ { "id": "ref", "pass": "match.naturalKey", "key": ["Ref"] } ] } ]
        }
        """;

    private static readonly SourceMap<Payment> Map = new SourceMap<Payment>()
        .Text("Id", row => row.Id)
        .Text("Ref", row => row.Ref)
        .Text("Amount", row => row.Amount)
        .Text("Fee", row => row.Fee);

    [Fact]
    public void BuildSideBySide_does_not_throw_when_bucket_sum_overflows_decimal()
    {
        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", [
                new Payment("L1", "A-1", "50000000000000000000000000000", "10.00"),
                new Payment("L2", "A-2", "50000000000000000000000000000", "5.00")
            ], Map),
            RecordSources.FromObjects("right", [
                new Payment("R1", "A-1", "50000000000000000000000000000", "10.00"),
                new Payment("R2", "A-2", "50000000000000000000000000000", "5.00")
            ], Map),
            ReferenceDataSet.Empty);

        var view = result.BuildSideBySide(new SideBySideSpec([], [], [new MeasureSpec("Amount"), new MeasureSpec("Fee")], null));

        var line = Assert.Single(view.Lines);
        Assert.False(line.Cells.ContainsKey("Amount"));
        Assert.Equal(new SideBySideCell(15.00m, 15.00m, 0.00m, 0.00m, 0.00m, 0.00m), line.Cells["Fee"]);
    }

    [Fact]
    public void BuildSideBySide_does_not_throw_when_opposite_sign_delta_overflows_decimal()
    {
        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", [
                new Payment("L1", "A-1", "-70000000000000000000000000000", "1.00")
            ], Map),
            RecordSources.FromObjects("right", [
                new Payment("R1", "A-1", "70000000000000000000000000000", "1.00")
            ], Map),
            ReferenceDataSet.Empty);

        var view = result.BuildSideBySide(new SideBySideSpec([], [], [new MeasureSpec("Amount")], null));

        Assert.Empty(view.Lines);
    }

    public sealed record Payment(string Id, string Ref, string Amount, string Fee);
}
