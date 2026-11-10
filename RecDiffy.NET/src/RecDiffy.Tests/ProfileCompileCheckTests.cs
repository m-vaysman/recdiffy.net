namespace RecDiffy.Tests;

// The compile checks #223 added: the code each one reports, and the profiles they must still accept.
public class ProfileCompileCheckTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "compile-checks",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Book", "type": "Symbol", "role": "Key" },
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "EventAction", "type": "Symbol", "role": "Lifecycle" },
            { "name": "EventTarget", "type": "Text", "role": "Lifecycle" },
            { "name": "PackageRef", "type": "Text", "role": "Package" },
            { "name": "LegRole", "type": "Symbol", "role": "Package" },
            { "name": "SettleDate", "type": "Date", "role": "Economic" },
            { "name": "Price", "type": "Decimal", "scale": 4, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "symbol.map", "from": "Book", "to": "Book" },
                { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" },
                { "rule": "text.copy", "from": "Link", "to": "PackageRef" },
                { "rule": "symbol.map", "from": "Role", "to": "LegRole" }
              ],
              "parse": [ { "rule": "date.parse", "from": "Settle", "to": "SettleDate", "formats": ["yyyy-MM-dd"] }, { "rule": "decimal.parse", "from": "Px", "to": "Price" } ]
            },
            "right": {
              "identify": [
                { "rule": "symbol.map", "from": "Book", "to": "Book" },
                { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" },
                { "rule": "symbol.map", "from": "Action", "to": "EventAction" },
                { "rule": "text.copy", "from": "Target", "to": "EventTarget" }
              ],
              "parse": [ { "rule": "date.parse", "from": "Settle", "to": "SettleDate", "formats": ["yyyy-MM-dd"] }, { "rule": "decimal.parse", "from": "Price", "to": "Price" } ]
            }
          },
          "consolidation": {
            "left": { "packages": [ { "link": "PackageRef", "role": "LegRole", "requiredRoles": ["PAY", "REC"], "aggregate": { "SettleDate": "Same", "Price": "FromRole(PAY)" } } ] },
            "right": { "lifecycle": { "action": "EventAction", "target": "EventTarget", "scope": "Book", "corrTarget": "CANC" } }
          },
          "paths": [ {
            "id": "p", "route": { "rule": "route.all" },
            "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ],
            "diff": { "fields": { "Price": { "tolerance": { "absolute": 0.01 } } } }
          } ]
        }
        """;

    [Fact]
    public void The_base_profile_compiles_with_Same_on_a_date_field() =>
        Assert.NotNull(Compile(Profile));

    [Theory]
    [InlineData("FromRole is set on a Date field", "\"SettleDate\": \"Same\", \"Price\": \"FromRole(PAY)\"", "\"SettleDate\": \"FromRole(REC)\"")]
    [InlineData("a sharedRef pass has no key, so it takes its default key", "\"pass\": \"match.naturalKey\", \"key\": [\"Ref\"]", "\"pass\": \"match.sharedRef\", \"key\": []")]
    [InlineData("a groupSum pass has no key", "{ \"id\": \"P1\", \"pass\": \"match.naturalKey\", \"key\": [\"Ref\"] }", "{ \"id\": \"P3\", \"pass\": \"match.groupSum\", \"many\": \"left\", \"block\": [\"Ref\"], \"conserve\": [\"Price\"] }")]
    public void A_profile_the_new_checks_do_not_cover_still_compiles(string what, string from, string to)
    {
        var changed = Changed(from, to);

        var failure = Record.Exception(() => Compile(changed));

        Assert.True(failure is null, $"Compile rejected a profile in which {what}: {failure?.Message}");
    }

    [Theory]
    [InlineData("\"action\": \"EventAction\"", "\"action\": \"EventActoin\"", "UnknownField", "right lifecycle action names unknown field 'EventActoin'.")]
    [InlineData("\"scope\": \"Book\"", "\"scope\": \"Bok\"", "UnknownField", "right lifecycle scope names unknown field 'Bok'.")]
    [InlineData("\"link\": \"PackageRef\"", "\"link\": \"PackageRf\"", "UnknownField", "left package link names unknown field 'PackageRf'.")]
    [InlineData("\"Price\": \"FromRole(PAY)\"", "\"Prise\": \"FromRole(PAY)\"", "UnknownField", "left aggregator names unknown field 'Prise'.")]
    [InlineData("\"SettleDate\": \"Same\"", "\"SettleDate\": \"Sum\"", "InvalidParameter", "left aggregator Sum on 'SettleDate' needs a Decimal field, not a Date field.")]
    [InlineData("\"SettleDate\": \"Same\"", "\"SettleDate\": \"DifferenceByRole(PAY,REC)\"", "InvalidParameter", "left aggregator DifferenceByRole on 'SettleDate' needs a Decimal field, not a Date field.")]
    [InlineData("\"Price\": \"FromRole(PAY)\"", "\"Price\": \"DifferenceByRole(PAY)\"", "MissingParameter", "left aggregator DifferenceByRole on 'Price' requires two roles.")]
    [InlineData("\"pass\": \"match.naturalKey\", \"key\": [\"Ref\"]", "\"pass\": \"match.naturalKey\", \"key\": []", "MissingParameter", "Pass 'P1' requires key.")]
    [InlineData("\"pass\": \"match.naturalKey\", \"key\": [\"Ref\"]", "\"pass\": \"match.reducedKey\", \"key\": []", "MissingParameter", "Pass 'P1' requires key.")]
    [InlineData("{ \"rule\": \"decimal.parse\", \"from\": \"Price\", \"to\": \"Price\" }", "{ \"rule\": \"decimal.parse\", \"from\": \"Price\", \"to\": \"Px\" }", "FieldNotProduced", "Path 'p' compares 'Price', which a side does not write.")]
    public void A_profile_that_cannot_work_names_the_reason(string from, string to, string code, string message)
    {
        var changed = Changed(from, to);

        var failure = Assert.Throws<ProfileCompileException>(() => Compile(changed));

        Assert.Contains(new ProfileError(code, message), failure.Errors);
    }

    private static string Changed(string from, string to)
    {
        var changed = Profile.Replace(from, to, StringComparison.Ordinal);
        Assert.False(string.Equals(Profile, changed, StringComparison.Ordinal));
        return changed;
    }

    private static CompiledProfile Compile(string profile) =>
        Reconciler.Compile(ProfileDocument.Parse(profile), RuleRegistry.CreateCore());
}
