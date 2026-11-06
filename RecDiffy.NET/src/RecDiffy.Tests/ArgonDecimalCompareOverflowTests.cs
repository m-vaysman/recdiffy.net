using RecDiffy.Results;
using RecDiffy.Rules;

namespace RecDiffy.Tests;

public class ArgonDecimalCompareOverflowTests
{
    [Fact]
    public void Decimal_comparison_does_not_overflow_when_comparing_extreme_decimals()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse("""
            {
              "formatVersion": 1,
              "profileId": "extreme-decimal",
              "version": "1.0.0",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Sku", "type": "Symbol", "role": "Key" },
                { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
              ],
              "normalization": {
                "left": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
                "right": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
              },
              "paths": [ {
                "id": "stock",
                "route": { "rule": "route.all" },
                "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Sku"] } ],
                "diff": { "fields": { "Quantity": { "tolerance": { "absolute": "10" } } } }
              } ]
            }
            """), RuleRegistry.CreateCore());

        var map = new SourceMap<(string Id, string Sku, decimal Qty)>()
            .Text("Id", r => r.Id)
            .Text("Sku", r => r.Sku)
            .Decimal("Quantity", r => r.Qty);

        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", [("L1", "A", decimal.MaxValue)], map),
            RecordSources.FromObjects("right", [("R1", "A", decimal.MinValue)], map),
            ReferenceDataSet.Empty);

        Assert.Equal(FieldOutcome.Break, Assert.Single(result.Entries).Outcome);
    }
}
