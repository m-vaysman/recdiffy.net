namespace RecDiffy.Tests;

public class TwoOperatorConditionTests
{
    private const string OneOperator = """{ "field": "Book", "equals": "RATES" }""";

    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "two-operator-condition",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "Book", "type": "Text", "role": "Text" },
            { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" }, { "rule": "text.copy", "from": "Book", "to": "Book" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount", "when": { "field": "Book", "equals": "RATES" } } ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" }, { "rule": "text.copy", "from": "Book", "to": "Book" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount", "when": { "field": "Book", "equals": "RATES" } } ]
            }
          },
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ] } ]
        }
        """;

    [Theory(Skip = "#339: a condition with two operators compiles, and the second operator is ignored. Unskip with the fix.")]
    [InlineData("""{ "field": "Book", "equals": "RATES", "notEquals": "RATES" }""")]
    [InlineData("""{ "field": "Book", "equals": "RATES", "in": ["FX"] }""")]
    [InlineData("""{ "field": "Book", "notEquals": "RATES", "in": ["RATES"] }""")]
    public void A_condition_that_declares_two_operators_is_rejected_at_compile(string when)
    {
        var json = Profile.Replace(OneOperator, when, StringComparison.Ordinal);
        Assert.Contains(when, json, StringComparison.Ordinal);

        Assert.Throws<ProfileCompileException>(() => Reconciler.Compile(ProfileDocument.Parse(json), RuleRegistry.CreateCore()));
    }
}
