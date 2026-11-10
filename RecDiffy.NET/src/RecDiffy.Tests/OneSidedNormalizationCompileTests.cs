namespace RecDiffy.Tests;

public class OneSidedNormalizationCompileTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "one-sided-normalization",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Sku", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
          },
          "paths": [ { "id": "stock", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Sku"] } ] } ]
        }
        """;

    [Fact(Skip = "🔴 #287: a side with no normalization block is never checked, so FieldNotProduced misses a key it never writes.")]
    public void A_side_with_no_normalization_block_fails_compile_because_it_writes_no_key_field()
    {
        var error = Assert.Throws<ProfileCompileException>(() => Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()));

        Assert.Contains(error.Errors, problem => problem.Code == "FieldNotProduced" && problem.Message.Contains("'Sku'", StringComparison.Ordinal));
    }
}
