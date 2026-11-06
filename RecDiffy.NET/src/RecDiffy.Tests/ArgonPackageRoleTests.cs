namespace RecDiffy.Tests;

public class ArgonPackageRoleTests
{
    [Fact]
    public void ProfileCompile_rejects_package_aggregator_role_not_in_required_roles()
    {
        var exception = Assert.Throws<ProfileCompileException>(() =>
            Reconciler.Compile(ProfileDocument.Parse("""
                {
                  "formatVersion": 1,
                  "profileId": "pkg-bad-role",
                  "version": "1.0.0",
                  "fields": [
                    { "name": "PackageRef", "type": "Text", "role": "Package" },
                    { "name": "LegRole", "type": "Symbol", "role": "Package" },
                    { "name": "Price", "type": "Decimal", "scale": 2, "role": "Economic" }
                  ],
                  "consolidation": {
                    "left": {
                      "packages": [ {
                        "link": "PackageRef",
                        "role": "LegRole",
                        "requiredRoles": ["LEG_1_PAY", "LEG_2_RECEIVE"],
                        "aggregate": { "Price": "FromRole(NON_EXISTENT_ROLE)" }
                      } ]
                    }
                  },
                  "paths": [ { "id": "p", "route": { "rule": "route.all" } } ]
                }
                """), RuleRegistry.CreateCore()));

        Assert.NotEmpty(exception.Errors);
    }
}
