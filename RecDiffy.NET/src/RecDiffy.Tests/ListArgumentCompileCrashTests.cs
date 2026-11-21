namespace RecDiffy.Tests;

// A rule's string argument given as a JSON list passes ProfileCompiler's missing-parameter check, which accepts a list
// for any required name, and then the registered factory reads call.Strings and throws KeyNotFoundException. R3 wants a
// named ProfileError instead. Found while testing text.regex (#547), which checks its own arguments.
public class ListArgumentCompileCrashTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "list-argument",
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

    [Fact(Skip = "Defect: a list for a required string argument crashes compile with KeyNotFoundException instead of a ProfileError (ProfileCompiler.Missing accepts a list for any name).")]
    public void A_list_for_a_required_string_argument_is_a_profile_error_not_a_crash()
    {
        const string argument = "\"from\": \"Sku\"";
        var at = Profile.IndexOf(argument, StringComparison.Ordinal);
        var json = Profile[..at] + "\"from\": [ \"Sku\" ]" + Profile[(at + argument.Length)..];

        var failure = Record.Exception(() => Reconciler.Compile(ProfileDocument.Parse(json), RuleRegistry.CreateCore()));
        Assert.IsType<ProfileCompileException>(failure);
    }
}
