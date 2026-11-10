namespace RecDiffy.Tests;

// Spec v3 §9.3: "Reconciler.Compile rejects a profile before any row is read when ... a parameter is missing or
// mistyped; ... a key or tolerance names a field no rule produces; ...; an aggregator names an undeclared role".
// Each case below changes one setting of a valid profile into one that cannot work (#223).
public class ProfileCompileGapTests
{
    private const string Lifecycle = """
        {
          "formatVersion": 1,
          "profileId": "compile-gap-lifecycle",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "EventAction", "type": "Symbol", "role": "Lifecycle" },
            { "name": "EventTarget", "type": "Text", "role": "Lifecycle" },
            { "name": "Price", "type": "Decimal", "scale": 4, "role": "Economic" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "symbol.map", "from": "Cpty", "to": "Counterparty" }, { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "decimal.parse", "from": "Px", "to": "Price" } ]
            },
            "right": {
              "identify": [
                { "rule": "symbol.map", "from": "Cpty", "to": "Counterparty" },
                { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" },
                { "rule": "symbol.map", "from": "Action", "to": "EventAction" },
                { "rule": "text.copy", "from": "Target", "to": "EventTarget" }
              ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "decimal.parse", "from": "Px", "to": "Price" } ]
            }
          },
          "consolidation": { "right": { "lifecycle": { "action": "EventAction", "target": "EventTarget", "scope": "Counterparty", "corrTarget": "CANC" } } },
          "paths": [ {
            "id": "p", "route": { "rule": "route.all" },
            "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ],
            "diff": { "fields": { "Price": { "tolerance": { "absolute": 0.01 } } } }
          } ]
        }
        """;

    private const string Package = """
        {
          "formatVersion": 1,
          "profileId": "compile-gap-package",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "PackageRef", "type": "Text", "role": "Package" },
            { "name": "LegRole", "type": "Symbol", "role": "Package" },
            { "name": "Uti", "type": "Identifier", "role": "Key" },
            { "name": "SettleDate", "type": "Date", "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "text.copy", "from": "Link", "to": "PackageRef" },
                { "rule": "symbol.map", "from": "Role", "to": "LegRole" },
                { "rule": "identifier.map", "from": "Uti", "to": "Uti", "scheme": "uti" }
              ],
              "parse": [ { "rule": "date.parse", "from": "Settle", "to": "SettleDate", "formats": ["yyyy-MM-dd"] }, { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" } ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Uti", "to": "Uti", "scheme": "uti" } ],
              "parse": [ { "rule": "date.parse", "from": "Settle", "to": "SettleDate", "formats": ["yyyy-MM-dd"] }, { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" } ]
            }
          },
          "consolidation": { "left": { "packages": [ { "link": "PackageRef", "role": "LegRole", "requiredRoles": ["PAY", "REC"], "aggregate": { "NetCash": "DifferenceByRole(PAY,REC)" } } ] } },
          "paths": [ { "id": "p", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Uti"] } ] } ]
        }
        """;

    [Fact]
    public void The_unchanged_profiles_compile()
    {
        Assert.NotNull(Reconciler.Compile(ProfileDocument.Parse(Lifecycle), RuleRegistry.CreateCore()));
        Assert.NotNull(Reconciler.Compile(ProfileDocument.Parse(Package), RuleRegistry.CreateCore()));
    }

    [Theory]
    [InlineData("lifecycle action names a field that is not declared", "\"action\": \"EventAction\"", "\"action\": \"EventActoin\"")]
    [InlineData("lifecycle target names a field that is not declared", "\"target\": \"EventTarget\"", "\"target\": \"EventTargte\"")]
    [InlineData("lifecycle scope names a field that is not declared", "\"scope\": \"Counterparty\"", "\"scope\": \"Counterpart\"")]
    [InlineData("a tolerance names a field no rule produces", "{ \"rule\": \"decimal.parse\", \"from\": \"Qty\", \"to\": \"Quantity\" }, { \"rule\": \"decimal.parse\", \"from\": \"Px\", \"to\": \"Price\" }", "{ \"rule\": \"decimal.parse\", \"from\": \"Qty\", \"to\": \"Quantity\" }")]
    [InlineData("a natural key pass has no key field", "\"key\": [\"Ref\"]", "\"key\": []")]
    public void A_lifecycle_profile_that_cannot_work_is_rejected_at_compile(string what, string from, string to) =>
        AssertRejected(what, Lifecycle, from, to);

    [Theory]
    [InlineData("package link names a field that is not declared", "\"link\": \"PackageRef\"", "\"link\": \"PackageRf\"")]
    [InlineData("package role names a field that is not declared", "\"role\": \"LegRole\", \"requiredRoles\"", "\"role\": \"LegRol\", \"requiredRoles\"")]
    [InlineData("an aggregator names a field that is not declared", "\"NetCash\": \"DifferenceByRole(PAY,REC)\"", "\"NetCsh\": \"Sum\"")]
    [InlineData("a Sum aggregator is set on a Date field", "\"NetCash\": \"DifferenceByRole(PAY,REC)\"", "\"SettleDate\": \"Sum\"")]
    public void A_package_profile_that_cannot_work_is_rejected_at_compile(string what, string from, string to) =>
        AssertRejected(what, Package, from, to);

    [Fact]
    public void A_DifferenceByRole_aggregator_with_one_role_is_rejected_at_compile() =>
        AssertRejected("DifferenceByRole names one role", Package, "DifferenceByRole(PAY,REC)", "DifferenceByRole(PAY)");

    private static void AssertRejected(string what, string profile, string from, string to)
    {
        var changed = profile.Replace(from, to, StringComparison.Ordinal);
        Assert.False(string.Equals(profile, changed, StringComparison.Ordinal));

        var failure = Record.Exception(() => Reconciler.Compile(ProfileDocument.Parse(changed), RuleRegistry.CreateCore()));

        Assert.True(failure is ProfileCompileException, $"Compile accepted a profile in which {what}.");
    }
}
