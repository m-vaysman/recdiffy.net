using System.Globalization;
using RecDiffy.Results;

namespace RecDiffy.Tests;

// The proof test from #219, from an adversarial pass on dev. Spec §10.1: "Same rows (in any order), same profile hash,
// same resolved side cultures, same reference snapshot and same engine version give the same groups, alignments,
// outcomes, deltas, dispositions and diagnostic codes." Two profiles that treat the same rows differently must therefore
// not share a hash. A profile that Compile rejects has no hash to share.
public class ConditionOperandHashTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "condition-operand-hash",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "AssetClass", "type": "Symbol", "role": "Key" },
            { "name": "Accrued", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" }, { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Acc", "to": "Accrued", "when": { "field": "Asset", "in": ["Bond"] } } ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" }, { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Acc", "to": "Accrued", "when": { "field": "Asset", "in": ["Bond"] } } ]
            }
          },
          "paths": [ {
            "id": "p", "route": { "rule": "route.all" },
            "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ],
            "diff": { "fields": { "Accrued": { "applicableWhen": { "field": "AssetClass", "equals": "Bond" } } } }
          } ]
        }
        """;

    private const string Applicable = "{ \"field\": \"AssetClass\", \"equals\": \"Bond\" }";
    private const string RuleWhen = "\"when\": { \"field\": \"Asset\", \"in\": [\"Bond\"] }";

    private static readonly SourceMap<Row> Map = new SourceMap<Row>()
        .Text("Id", row => row.Id).Text("Ref", row => row.Ref).Text("Asset", row => row.Asset).Text("Acc", row => row.Acc);

    // One bond on each side, accrued 10 against 99. Each case swaps one condition for two spellings that treat the
    // bond differently. The first four come from the issue; the last three are blank checks that also shared a hash.
    [Theory]
    [InlineData("applicableWhen with an empty in list", Applicable, "{ \"field\": \"AssetClass\" }", "{ \"field\": \"AssetClass\", \"in\": [] }")]
    [InlineData("applicableWhen that equals the empty text", Applicable, "{ \"field\": \"AssetClass\" }", "{ \"field\": \"AssetClass\", \"equals\": \"\" }")]
    [InlineData("rule when with an empty in list", RuleWhen, "\"when\": { \"field\": \"Asset\" }", "\"when\": { \"field\": \"Asset\", \"in\": [] }")]
    [InlineData("rule when that equals the empty text", RuleWhen, "\"when\": { \"field\": \"Asset\" }", "\"when\": { \"field\": \"Asset\", \"equals\": \"\" }")]
    [InlineData("rule when that equals or does not equal the empty text", RuleWhen, "\"when\": { \"field\": \"Asset\", \"equals\": \"\" }", "\"when\": { \"field\": \"Asset\", \"notEquals\": \"\" }")]
    [InlineData("rule when with an in list of the empty text", RuleWhen, "\"when\": { \"field\": \"Asset\", \"in\": [\"\"] }", "\"when\": { \"field\": \"Asset\", \"notEquals\": \"\" }")]
    [InlineData("applicableWhen with an empty equals beside an in list", Applicable, "{ \"field\": \"AssetClass\", \"in\": [\"Bond\"] }", "{ \"field\": \"AssetClass\", \"equals\": \"\", \"in\": [\"Bond\"] }")]
    public void Profiles_that_share_a_hash_give_the_same_result(string what, string from, string firstSpelling, string secondSpelling)
    {
        var first = Compile(from, firstSpelling);
        var second = Compile(from, secondSpelling);
        if (first is null || second is null)
            return;

        var firstResult = Describe(Run(first));
        var secondResult = Describe(Run(second));

        Assert.True(
            !string.Equals(first.Hash, second.Hash, StringComparison.Ordinal) || string.Equals(firstResult, secondResult, StringComparison.Ordinal),
            $"{what}: both profiles hash to {first.Hash}. First: {firstResult}. Second: {secondResult}.");
    }

    // Null when Compile rejects the profile.
    private static CompiledProfile? Compile(string from, string to)
    {
        var changed = Profile.Replace(from, to, StringComparison.Ordinal);
        Assert.False(string.Equals(Profile, changed, StringComparison.Ordinal));
        try
        {
            return Reconciler.Compile(ProfileDocument.Parse(changed), RuleRegistry.CreateCore());
        }
        catch (ProfileCompileException)
        {
            return null;
        }
    }

    private static string Describe(ReconciliationResult result) =>
        string.Join(" || ", result.Entries.Select(entry =>
            $"{entry.Alignment}/{entry.Outcome} [{string.Join(", ", entry.Diffs.Select(diff => $"{diff.Field.Name}:{diff.Outcome}:{diff.Delta.Number.ToString(CultureInfo.InvariantCulture)}"))}]"));

    private static ReconciliationResult Run(CompiledProfile profile) =>
        Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", [new Row("L1", "T1", "Bond", "10")], Map),
            RecordSources.FromObjects("right", [new Row("R1", "T1", "Bond", "99")], Map),
            ReferenceDataSet.Empty);

    public sealed record Row(string Id, string Ref, string Asset, string Acc);
}
