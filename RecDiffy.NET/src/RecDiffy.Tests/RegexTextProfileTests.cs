using RecDiffy.Results;

namespace RecDiffy.Tests;

// text.regex in a profile (#547): the p03 shape from the blind discovery run, where one trade id is TRD-000001 on the
// left and trd1 on the right. One pattern per side replaces a 640-entry symbol.map.
public class RegexTextProfileTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "regex-trade-ids",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "TradeId" }, "right": { "rowKey": "TradeId" } },
          "fields": [
            { "name": "Trade", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "text.regex", "from": "TradeId", "to": "Trade", "pattern": "^TRD-0*(\\d+)$", "replacement": "$1", "passthrough": false } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ]
            },
            "right": {
              "identify": [ { "rule": "text.regex", "from": "TradeId", "to": "Trade", "pattern": "^trd(\\d+)$", "replacement": "$1", "ignoreCase": true, "passthrough": false } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" } ]
            }
          },
          "paths": [ { "id": "all", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": [ "Trade" ] } ] } ]
        }
        """;

    private const string LeftPattern = "^TRD-0*(\\\\d+)$";

    [Fact]
    public void Two_spellings_of_each_trade_id_match_exactly()
    {
        var result = Run(Compile(Profile), [("TRD-000001", "10"), ("TRD-000002", "20")], [("trd1", "10"), ("TRD2", "20")]);

        Assert.All(result.Rows, row => Assert.Equal(Disposition.Matched, row.Disposition));
        Assert.Equal(2, result.Entries.Count(entry => entry is { Alignment: Alignment.Matched, Outcome: FieldOutcome.Exact }));
    }

    [Fact]
    public void An_id_that_fits_neither_pattern_is_quarantined_with_PatternNoMatch()
    {
        var result = Run(Compile(Profile), [("TRD-000001", "10")], [("trd1", "10"), ("X-99", "5")]);

        var stray = Assert.Single(result.Rows, row => row.Row.RowKey == "X-99");
        Assert.Equal(Disposition.Quarantined, stray.Disposition);
        Assert.Contains(stray.Diagnostics, diagnostic => diagnostic is { Code: "PatternNoMatch", RuleId: "text.regex", Field: "Trade", RawText: "X-99" });
    }

    [Fact]
    public void The_pattern_is_part_of_the_profile_hash()
    {
        var first = Compile(Profile);
        var second = Compile(Profile.Replace(LeftPattern, "^TRD-(\\\\d+)$", StringComparison.Ordinal));

        Assert.Contains(LeftPattern, Profile, StringComparison.Ordinal);
        Assert.NotEqual(first.Hash, second.Hash, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("\"pattern\": \"^TRD-0*(\\\\d+)$\"", "\"pattern\": \"^TRD-(\"")]
    [InlineData("\"pattern\": \"^TRD-0*(\\\\d+)$\"", "\"pattern\": \"^(?=TRD)TRD-(\\\\d+)$\"")]
    [InlineData("\"pattern\": \"^TRD-0*(\\\\d+)$\"", "\"pattern\": \"^(\\\\d)\\\\1$\"")]
    [InlineData("\"replacement\": \"$1\", \"passthrough\": false } ],", "\"replacement\": \"$1\", \"passthrough\": \"no\" } ],")]
    [InlineData("\"replacement\": \"$1\", \"passthrough\": false } ],", "\"replacement\": \"$1\", \"passthrough\": false, \"ignoreCase\": \"yes\" } ],")]
    [InlineData("\"replacement\": \"$1\", \"passthrough\": false } ],", "\"replacement\": \"$1\", \"passthrough\": false, \"case\": \"title\" } ],")]
    [InlineData("{ \"name\": \"Trade\", \"type\": \"Symbol\", \"role\": \"Key\" }", "{ \"name\": \"Trade\", \"type\": \"Date\", \"role\": \"Key\" }")]
    public void A_bad_parameter_fails_compile_with_InvalidParameter(string original, string replacement)
    {
        var json = Profile.Replace(original, replacement, StringComparison.Ordinal);

        var failure = Assert.Throws<ProfileCompileException>(() => Compile(json));

        Assert.NotEqual(Profile, json, StringComparer.Ordinal);
        Assert.Contains(failure.Errors, error => error.Code == "InvalidParameter" && error.Message.StartsWith("text.regex ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("\"pattern\": \"^TRD-0*(\\\\d+)$\", ", "pattern")]
    [InlineData("\"replacement\": \"$1\", \"passthrough\": false } ],", "replacement")]
    public void A_missing_pattern_or_replacement_fails_compile_with_MissingParameter(string removed, string name)
    {
        var json = Profile.Replace(removed, name is "replacement" ? "\"passthrough\": false } ]," : "", StringComparison.Ordinal);

        var failure = Assert.Throws<ProfileCompileException>(() => Compile(json));

        Assert.NotEqual(Profile, json, StringComparer.Ordinal);
        Assert.Contains(new ProfileError("MissingParameter", $"left.text.regex is missing '{name}'."), failure.Errors);
    }

    private static CompiledProfile Compile(string json) =>
        Reconciler.Compile(ProfileDocument.Parse(json), RuleRegistry.CreateCore());

    private static IReconciliationResult Run(CompiledProfile profile, (string TradeId, string Qty)[] left, (string TradeId, string Qty)[] right)
    {
        var map = new SourceMap<(string TradeId, string Qty)>().Text("TradeId", row => row.TradeId).Text("Qty", row => row.Qty);
        return Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", left, map),
            RecordSources.FromObjects("right", right, map),
            ReferenceDataSet.Empty);
    }
}
