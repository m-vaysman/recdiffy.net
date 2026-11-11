using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

public class R3CompileValidationTests
{
    private const string LeftOnlyNormalizationProfile = """
        {
          "formatVersion": 1,
          "profileId": "r3-missing-side",
          "version": "1.0.0",
          "culture": "en-US",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "TradeRef", "type": "Identifier", "role": "Key" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "identifier.map", "from": "Ref", "to": "TradeRef", "scheme": "shared" }
              ]
            }
          },
          "paths": [
            {
              "id": "main",
              "route": { "rule": "route.all" },
              "passes": [
                { "id": "P1", "pass": "match.naturalKey", "key": ["TradeRef"] }
              ]
            }
          ]
        }
        """;

    [Theory]
    [InlineData("left")]
    [InlineData("right")]
    public void Omitting_either_side_from_normalization_reports_FieldNotProduced_for_unwritten_key(string presentSide)
    {
        var profile = LeftOnlyNormalizationProfile.Replace("\"left\": {\n              \"identify\"", $"\"{presentSide}\": {{\n              \"identify\"", StringComparison.Ordinal)
            .Replace("\"left\": {\r\n              \"identify\"", $"\"{presentSide}\": {{\r\n              \"identify\"", StringComparison.Ordinal);

        var exception = Assert.Throws<ProfileCompileException>(() =>
            Reconciler.Compile(ProfileDocument.Parse(profile), RuleRegistry.CreateCore()));

        Assert.Contains(exception.Errors, error => error.Code == "FieldNotProduced" && error.Message.Contains("TradeRef", StringComparison.Ordinal));
    }

    [Fact]
    public void SharedRef_with_default_key_reports_FieldNotProduced_when_declared_default_fields_are_not_written()
    {
        const string profile = """
            {
              "formatVersion": 1,
              "profileId": "r3-shared-ref-unwritten-defaults",
              "version": "1.0.0",
              "culture": "en-US",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Counterparty", "type": "Symbol", "role": "Key" },
                { "name": "TradeRef", "type": "Identifier", "role": "Identity" }
              ],
              "normalization": {
                "left": {
                  "identify": [
                    { "rule": "symbol.map", "from": "Cpty", "to": "Counterparty" }
                  ]
                },
                "right": {
                  "identify": [
                    { "rule": "symbol.map", "from": "Cpty", "to": "Counterparty" }
                  ]
                }
              },
              "paths": [
                {
                  "id": "main",
                  "route": { "rule": "route.all" },
                  "passes": [
                    { "id": "P1", "pass": "match.sharedRef" }
                  ]
                }
              ]
            }
            """;

        var exception = Assert.Throws<ProfileCompileException>(() =>
            Reconciler.Compile(ProfileDocument.Parse(profile), RuleRegistry.CreateCore()));

        var error = Assert.Single(exception.Errors);
        Assert.Equal("FieldNotProduced", error.Code);
        Assert.Contains("TradeRef", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Standalone_party_resolveAlias_compiles_without_FieldReadBeforeWrite_and_resolves_alias()
    {
        const string profile = """
            {
              "formatVersion": 1,
              "profileId": "r3-standalone-party-alias",
              "version": "1.0.0",
              "culture": "en-US",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Counterparty", "type": "Identifier", "role": "Key" }
              ],
              "normalization": {
                "left": {
                  "identify": [
                    { "rule": "party.resolveAlias", "from": "BrokerName", "to": "Counterparty" }
                  ]
                },
                "right": {
                  "identify": [
                    { "rule": "party.resolveAlias", "from": "BrokerName", "to": "Counterparty" }
                  ]
                }
              },
              "paths": [
                {
                  "id": "main",
                  "route": { "rule": "route.all" },
                  "passes": [
                    { "id": "P1", "pass": "match.naturalKey", "key": ["Counterparty"] }
                  ]
                }
              ]
            }
            """;

        var registry = TradingRulePack.AddTo(RuleRegistry.CreateCore());
        var compiled = Reconciler.Compile(ProfileDocument.Parse(profile), registry);
        var reference = ReferenceDataSet.Create(
            "ref",
            new DateOnly(2026, 1, 15),
            "h1",
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["J.P. Morgan"] = "JPM" },
            new Dictionary<string, string>(StringComparer.Ordinal));
        var map = new SourceMap<(string Id, string BrokerName)>()
            .Text("Id", row => row.Id)
            .Text("BrokerName", row => row.BrokerName);

        var result = Reconciler.Run(
            compiled,
            RecordSources.FromObjects("left", [("L1", "J.P. Morgan")], map),
            RecordSources.FromObjects("right", [("R1", "J.P. Morgan")], map),
            reference);

        Assert.Equal(1, result.Summary.Exact);
    }

    [Theory]
    [InlineData(
        "\"parse\": [ { \"rule\": \"symbol.map\", \"from\": \"Ref\", \"to\": \"TradeRef\" } ]",
        "WrongPhase")]
    [InlineData(
        "\"paths\": [ { \"id\": \"p1\", \"route\": { \"rule\": \"route.all\" }, \"passes\": [ { \"id\": \"P1\", \"pass\": \"match.naturalKey\", \"key\": [\"TradeRef\"] } ] }, { \"id\": \"p2\", \"route\": { \"rule\": \"route.all\" }, \"passes\": [ { \"id\": \"P1\", \"pass\": \"match.naturalKey\", \"key\": [\"TradeRef\"] } ] } ]",
        "OverlappingRoute")]
    [InlineData(
        "\"paths\": [ { \"id\": \"main\", \"route\": { \"rule\": \"route.all\" }, \"passes\": [ { \"id\": \"P1\", \"pass\": \"match.naturalKey\", \"key\": [\"TradeRef\"] } ], \"diff\": { \"fields\": { \"TradeRef\": { \"tolerance\": { \"days\": 1 } } } } } ]",
        "ToleranceMismatch")]
    public void Compile_rejects_wrong_phase_overlapping_route_and_tolerance_mismatch_with_named_codes(string replacement, string expectedCode)
    {
        const string baseProfile = """
            {
              "formatVersion": 1,
              "profileId": "r3-compile-codes",
              "version": "1.0.0",
              "culture": "en-US",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "TradeRef", "type": "Identifier", "role": "Key" }
              ],
              "normalization": {
                "left": {
                  "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "TradeRef", "scheme": "shared" } ]
                },
                "right": {
                  "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "TradeRef", "scheme": "shared" } ]
                }
              },
              "paths": [ { "id": "main", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["TradeRef"] } ] } ]
            }
            """;

        var mutated = expectedCode is "WrongPhase"
            ? baseProfile.Replace(
                "\"identify\": [ { \"rule\": \"identifier.map\", \"from\": \"Ref\", \"to\": \"TradeRef\", \"scheme\": \"shared\" } ]",
                replacement,
                StringComparison.Ordinal)
            : baseProfile.Replace(
                "\"paths\": [ { \"id\": \"main\", \"route\": { \"rule\": \"route.all\" }, \"passes\": [ { \"id\": \"P1\", \"pass\": \"match.naturalKey\", \"key\": [\"TradeRef\"] } ] } ]",
                replacement,
                StringComparison.Ordinal);

        var exception = Assert.Throws<ProfileCompileException>(() =>
            Reconciler.Compile(ProfileDocument.Parse(mutated), RuleRegistry.CreateCore()));

        Assert.Contains(exception.Errors, error => error.Code == expectedCode);
    }
}
