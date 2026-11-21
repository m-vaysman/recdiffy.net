namespace RecDiffy.Tests;

// A rule argument that is a JSON list holding a number, or an object holding a non-string value, crashes ProfileParser
// with InvalidOperationException instead of a ProfileCompileException. ReadRule calls GetString on every item. Found
// while testing decimal.scale (#547).
public class ProfileParserNonStringItemTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "non-string-item",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [ { "name": "Sku", "type": "Symbol", "role": "Key" } ],
          "normalization": {
            "left": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ] },
            "right": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ] }
          },
          "paths": [ { "id": "all", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": [ "Sku" ] } ] } ]
        }
        """;

    [Theory(Skip = "Defect: ProfileParser.ReadRule calls GetString on every list item and object value, so a number there throws InvalidOperationException instead of a ProfileError.")]
    [InlineData("\"from\": [ 1 ]")]
    [InlineData("\"map\": { \"A\": 1 }, \"from\": \"Sku\"")]
    public void A_non_string_item_in_a_rule_argument_is_a_profile_error_not_a_crash(string argument)
    {
        const string original = "\"from\": \"Sku\"";
        var at = Profile.IndexOf(original, StringComparison.Ordinal);
        var json = Profile[..at] + argument + Profile[(at + original.Length)..];

        var failure = Record.Exception(() => Reconciler.Compile(ProfileDocument.Parse(json), RuleRegistry.CreateCore()));

        Assert.IsType<ProfileCompileException>(failure);
    }
}
