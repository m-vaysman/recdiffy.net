namespace RecDiffy.Tests;

// #223 rejects a DifferenceByRole with one role at compile. The writer still writes that form back as it was read, so a
// profile saved from an editor keeps what its author typed and the compile error can name it.
public class OneRoleDifferenceWriterTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "one-role-difference",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "PackageRef", "type": "Text", "role": "Package" },
            { "name": "LegRole", "type": "Symbol", "role": "Package" },
            { "name": "Fees", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" },
                { "rule": "text.copy", "from": "Link", "to": "PackageRef" },
                { "rule": "symbol.map", "from": "Role", "to": "LegRole" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Fee", "to": "Fees" } ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Fee", "to": "Fees" } ]
            }
          },
          "consolidation": { "left": { "packages": [ { "link": "PackageRef", "role": "LegRole", "requiredRoles": ["PAY", "REC"], "aggregate": { "Fees": "DifferenceByRole(PAY)" } } ] } },
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ] } ]
        }
        """;

    [Fact]
    public void A_one_role_difference_is_written_back_as_it_was_read()
    {
        var document = ProfileDocument.Parse(Profile);

        var written = document.ToJson();

        Assert.Contains("\"Fees\": \"DifferenceByRole(PAY)\"", written, StringComparison.Ordinal);
        Assert.Equal(written, ProfileDocument.Parse(written).ToJson());
    }

    [Fact]
    public void A_one_role_difference_read_back_from_its_written_form_is_still_rejected()
    {
        var reread = ProfileDocument.Parse(ProfileDocument.Parse(Profile).ToJson());

        var failure = Assert.Throws<ProfileCompileException>(() => Reconciler.Compile(reread, RuleRegistry.CreateCore()));

        Assert.Equal(new ProfileError("MissingParameter", "left aggregator DifferenceByRole on 'Fees' requires two roles."), Assert.Single(failure.Errors));
    }
}
