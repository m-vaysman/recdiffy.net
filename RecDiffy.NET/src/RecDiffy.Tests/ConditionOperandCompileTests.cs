using RecDiffy.Results;

namespace RecDiffy.Tests;

// #219: a condition needs an operand (spec v3 §9.3: compile rejects a profile when "a parameter is missing"). An empty
// text operand is a blank check, because a blank cell is data (§7.5), and it hashes apart from every other operand.
public class ConditionOperandCompileTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "condition-operand-compile",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "AssetClass", "type": "Symbol", "role": "Key" },
            { "name": "Accrued", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Price", "type": "Decimal", "scale": 4, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" }, { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Acc", "to": "Accrued", "when": { "field": "Asset", "notEquals": "Equity" } }, { "rule": "decimal.parse", "from": "Px", "to": "Price" } ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" }, { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Acc", "to": "Accrued" }, { "rule": "decimal.parse", "from": "Px", "to": "Price" } ]
            }
          },
          "paths": [ {
            "id": "p", "route": { "rule": "route.all" },
            "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ],
            "diff": { "fields": {
              "Accrued": { "applicableWhen": { "field": "AssetClass", "in": ["Bond", "Swap"] } },
              "Price": { "tolerance": { "absolute": 0.01, "when": { "field": "AssetClass", "equals": "Bond" } } }
            } }
          } ]
        }
        """;

    private const string RuleWhen = "\"when\": { \"field\": \"Asset\", \"notEquals\": \"Equity\" }";
    private const string Applicable = "\"applicableWhen\": { \"field\": \"AssetClass\", \"in\": [\"Bond\", \"Swap\"] }";
    private const string ToleranceWhen = "\"when\": { \"field\": \"AssetClass\", \"equals\": \"Bond\" }";

    private static readonly SourceMap<Row> Map = new SourceMap<Row>()
        .Text("Id", row => row.Id).Text("Ref", row => "T1").Text("Asset", row => row.Asset).Text("Acc", row => row.Acc).Text("Px", row => "1");

    // Captured on dev before #219. A profile whose operands are all non-empty keeps its hash.
    [Fact]
    public void A_profile_with_no_empty_operand_keeps_its_hash() =>
        Assert.Equal("1771f6b2abc174930437612377ec33b69d1494bbd68cc9b9595cb33bc5e7fdc8", Compile(Profile).Hash);

    [Theory]
    [InlineData(RuleWhen, "\"when\": { \"field\": \"Asset\" }", "left.decimal.parse when on 'Asset' needs equals, notEquals or in.")]
    [InlineData(Applicable, "\"applicableWhen\": { \"field\": \"AssetClass\" }", "Path 'p' applicableWhen for 'Accrued' on 'AssetClass' needs equals, notEquals or in.")]
    [InlineData(ToleranceWhen, "\"when\": { \"field\": \"AssetClass\" }", "Path 'p' tolerance when for 'Price' on 'AssetClass' needs equals, notEquals or in.")]
    [InlineData(RuleWhen, "\"when\": { \"field\": \"Asset\", \"in\": [] }", "left.decimal.parse when on 'Asset' has an empty in list.")]
    [InlineData(Applicable, "\"applicableWhen\": { \"field\": \"AssetClass\", \"in\": [] }", "Path 'p' applicableWhen for 'Accrued' on 'AssetClass' has an empty in list.")]
    public void A_condition_with_no_operand_is_rejected(string from, string to, string message)
    {
        var failure = Assert.Throws<ProfileCompileException>(() => Compile(Changed(from, to)));

        Assert.Equal(new ProfileError("MissingParameter", message), Assert.Single(failure.Errors));
    }

    [Theory]
    [InlineData(RuleWhen, "\"when\": { \"field\": \"Asset\", \"equals\": \"\" }")]
    [InlineData(RuleWhen, "\"when\": { \"field\": \"Asset\", \"notEquals\": \"\" }")]
    [InlineData(Applicable, "\"applicableWhen\": { \"field\": \"AssetClass\", \"in\": [\"\"] }")]
    [InlineData(ToleranceWhen, "\"when\": { \"field\": \"AssetClass\", \"equals\": \"\" }")]
    public void A_blank_check_compiles(string from, string to) =>
        Assert.NotNull(Compile(Changed(from, to)));

    // Each spelling is a different condition, so no two share a hash.
    [Fact]
    public void Blank_checks_hash_apart_from_each_other_and_from_other_operands()
    {
        string[] spellings =
        [
            "\"when\": { \"field\": \"Asset\", \"equals\": \"\" }",
            "\"when\": { \"field\": \"Asset\", \"notEquals\": \"\" }",
            "\"when\": { \"field\": \"Asset\", \"in\": [\"\"] }",
            "\"when\": { \"field\": \"Asset\", \"in\": [\"\", \"Bond\"] }",
            "\"when\": { \"field\": \"Asset\", \"equals\": \"\", \"in\": [\"Bond\"] }",
            "\"when\": { \"field\": \"Asset\", \"in\": [\"Bond\"] }",
            "\"when\": { \"field\": \"Asset\", \"notEquals\": \"\", \"in\": [\"Bond\"] }",
        ];

        var hashes = spellings.Select(spelling => Compile(Changed(RuleWhen, spelling)).Hash).ToArray();

        Assert.Equal(spellings.Length, hashes.Distinct(StringComparer.Ordinal).Count());
    }

    // A blank raw cell reads as empty text, so equals "" holds on it and notEquals "" does not (§7.5).
    [Theory]
    [InlineData("\"equals\": \"\"", "", true)]
    [InlineData("\"equals\": \"\"", "Bond", false)]
    [InlineData("\"notEquals\": \"\"", "", false)]
    [InlineData("\"notEquals\": \"\"", "Bond", true)]
    public void An_empty_operand_checks_for_a_blank_cell(string operand, string asset, bool parses)
    {
        var profile = Compile(Changed(RuleWhen, $"\"when\": {{ \"field\": \"Asset\", {operand} }}"));

        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", [new Row("L1", asset, "10")], Map),
            RecordSources.FromObjects("right", [new Row("R1", asset, "10")], Map),
            ReferenceDataSet.Empty);

        var left = Assert.Single(result.Rows, row => row.Row.Source == "left");
        Assert.Equal(parses, result.Canonical(left.Row)["Accrued"].IsNull is false);
    }

    private static string Changed(string from, string to)
    {
        var changed = Profile.Replace(from, to, StringComparison.Ordinal);
        Assert.False(string.Equals(Profile, changed, StringComparison.Ordinal));
        return changed;
    }

    private static CompiledProfile Compile(string profile) =>
        Reconciler.Compile(ProfileDocument.Parse(profile), RuleRegistry.CreateCore());

    public sealed record Row(string Id, string Asset, string Acc);
}
