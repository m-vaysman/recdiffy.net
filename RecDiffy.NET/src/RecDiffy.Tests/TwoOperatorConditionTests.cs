namespace RecDiffy.Tests;

// #339: a condition that declares two operands compiled, and the matcher read the first one it found, in the order
// equals, notEquals, in, so the second was dropped with no sign. R3 rejects an error knowable from the profile at
// compile time, so a two-operand clause is now rejected with MultipleOperands, naming the clause.
//
// The profile carries each condition site once: a rule's when on the left, and the path's applicableWhen and
// tolerance when. That way one changed clause reports one error.
public class TwoOperatorConditionTests
{
    private const string RuleWhen = """{ "field": "Book", "equals": "RATES" }""";

    private const string ApplicableWhen = """{ "field": "Book", "in": ["RATES"] }""";

    private const string ToleranceWhen = """{ "field": "Book", "notEquals": "FX" }""";

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
            { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Accrued", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" }, { "rule": "text.copy", "from": "Book", "to": "Book" } ],
              "parse": [
                { "rule": "decimal.parse", "from": "Amount", "to": "Amount", "when": { "field": "Book", "equals": "RATES" } },
                { "rule": "decimal.parse", "from": "Acc", "to": "Accrued" }
              ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" }, { "rule": "text.copy", "from": "Book", "to": "Book" } ],
              "parse": [
                { "rule": "decimal.parse", "from": "Amount", "to": "Amount" },
                { "rule": "decimal.parse", "from": "Acc", "to": "Accrued" }
              ]
            }
          },
          "paths": [ {
            "id": "p",
            "route": { "rule": "route.all" },
            "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ],
            "diff": { "fields": {
              "Accrued": {
                "applicableWhen": { "field": "Book", "in": ["RATES"] },
                "tolerance": { "absolute": 0.01, "when": { "field": "Book", "notEquals": "FX" } }
              }
            } }
          } ]
        }
        """;

    [Theory]
    [InlineData("""{ "field": "Book", "equals": "RATES", "notEquals": "RATES" }""", "equals and notEquals")]
    [InlineData("""{ "field": "Book", "equals": "RATES", "in": ["FX"] }""", "equals and in")]
    [InlineData("""{ "field": "Book", "notEquals": "RATES", "in": ["RATES"] }""", "notEquals and in")]
    [InlineData("""{ "field": "Book", "equals": "", "notEquals": "RATES" }""", "equals and notEquals")]
    [InlineData("""{ "field": "Book", "equals": "RATES", "notEquals": "", "in": [""] }""", "equals, notEquals and in")]
    public void A_condition_that_declares_two_operators_is_rejected_at_compile(string when, string named)
    {
        var failure = Assert.Throws<ProfileCompileException>(() => Compile(Changed(RuleWhen, when)));

        Assert.Equal(
            new ProfileError("MultipleOperands", $"left.decimal.parse when on 'Book' declares {named}, so the rest would be ignored. Give exactly one operand."),
            Assert.Single(failure.Errors));
    }

    // The same check covers all three places a condition can stand (#339).
    [Theory]
    [InlineData(RuleWhen, "left.decimal.parse when")]
    [InlineData(ApplicableWhen, "Path 'p' applicableWhen for 'Accrued'")]
    [InlineData(ToleranceWhen, "Path 'p' tolerance when for 'Accrued'")]
    public void The_rule_and_both_path_conditions_are_checked(string from, string named)
    {
        var failure = Assert.Throws<ProfileCompileException>(() =>
            Compile(Changed(from, """{ "field": "Book", "equals": "RATES", "in": ["FX"] }""")));

        var error = Assert.Single(failure.Errors);
        Assert.Equal("MultipleOperands", error.Code);
        Assert.StartsWith($"{named} on 'Book' declares equals and in", error.Message, StringComparison.Ordinal);
    }

    // A clause with one operand still compiles, so the check rejects only the case that dropped one silently.
    [Theory]
    [InlineData(RuleWhen, """{ "field": "Book", "notEquals": "FX" }""")]
    [InlineData(RuleWhen, """{ "field": "Book", "in": ["RATES", "FX"] }""")]
    [InlineData(RuleWhen, """{ "field": "Book", "equals": "" }""")]
    [InlineData(ApplicableWhen, """{ "field": "Book", "in": [""] }""")]
    [InlineData(ToleranceWhen, """{ "field": "Book", "equals": "" }""")]
    public void A_condition_with_one_operand_still_compiles(string from, string to) =>
        Assert.NotNull(Compile(Changed(from, to)));

    private static ProfileDocument Changed(string from, string to)
    {
        var changed = Profile.Replace(from, to, StringComparison.Ordinal);
        Assert.False(string.Equals(Profile, changed, StringComparison.Ordinal));
        return ProfileDocument.Parse(changed);
    }

    private static CompiledProfile Compile(ProfileDocument document) =>
        Reconciler.Compile(document, RuleRegistry.CreateCore());
}
