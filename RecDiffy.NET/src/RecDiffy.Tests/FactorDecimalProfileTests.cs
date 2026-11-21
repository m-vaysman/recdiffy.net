using RecDiffy.Results;

namespace RecDiffy.Tests;

// decimal.scale in a profile (#547): the p07 shape from the blind discovery run, where the right side reports quantity
// and market value in thousands. One factor per field replaces the symbol tables the blind run had to write, and the
// fields stay Decimal, so a real difference is still a numeric break.
public class FactorDecimalProfileTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "factor-lots",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "LotId" }, "right": { "rowKey": "LotId" } },
          "fields": [
            { "name": "Lot", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" },
            { "name": "MarketValue", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "symbol.map", "from": "LotId", "to": "Lot" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Qty", "to": "Quantity" }, { "rule": "decimal.parse", "from": "Mv", "to": "MarketValue" } ]
            },
            "right": {
              "identify": [ { "rule": "symbol.map", "from": "LotId", "to": "Lot" } ],
              "parse": [ { "rule": "decimal.scale", "from": "Qty", "to": "Quantity", "factor": 1000 }, { "rule": "decimal.scale", "from": "Mv", "to": "MarketValue", "factor": "1000" } ]
            }
          },
          "paths": [ { "id": "all", "route": { "rule": "route.all" }, "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": [ "Lot" ] } ] } ]
        }
        """;

    [Fact]
    public void Thousands_on_one_side_compare_exactly_with_units_on_the_other()
    {
        var result = Run(Compile(Profile), [("L-1", "3700", "37000"), ("L-2", "100", "1025")], [("L-1", "3.7", "37"), ("L-2", "0.1", "1.025")]);

        Assert.All(result.Rows, row => Assert.Equal(Disposition.Matched, row.Disposition));
        Assert.Equal(2, result.Entries.Count(entry => entry is { Alignment: Alignment.Matched, Outcome: FieldOutcome.Exact }));
    }

    [Fact]
    public void A_real_difference_is_still_a_numeric_break()
    {
        var result = Run(Compile(Profile), [("L-1", "3700", "37000")], [("L-1", "3.6", "37")]);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(FieldOutcome.Break, entry.Outcome);
        Assert.Equal(-100m, Assert.Single(entry.Diffs, diff => diff.Field.Name == "Quantity").Delta.Number);
    }

    [Fact]
    public void The_factor_is_part_of_the_profile_hash()
    {
        var other = Profile.Replace("\"factor\": \"1000\"", "\"factor\": \"100\"", StringComparison.Ordinal);

        Assert.NotEqual(Profile, other, StringComparer.Ordinal);
        Assert.NotEqual(Compile(Profile).Hash, Compile(other).Hash, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("\"factor\": 1000 }", "}")]
    [InlineData("\"factor\": 1000 }", "\"factor\": [ \"1000\" ] }")]
    public void A_missing_or_list_factor_fails_compile_with_a_profile_error(string original, string replacement)
    {
        var json = Profile.Replace(original, replacement, StringComparison.Ordinal).Replace("\"Quantity\", }", "\"Quantity\" }", StringComparison.Ordinal);

        var failure = Assert.Throws<ProfileCompileException>(() => Compile(json));

        Assert.NotEqual(Profile, json, StringComparer.Ordinal);
        Assert.Contains(failure.Errors, error => error.Code is "MissingParameter" or "InvalidParameter" && error.Message.Contains("factor", StringComparison.Ordinal));
    }

    private static CompiledProfile Compile(string json) =>
        Reconciler.Compile(ProfileDocument.Parse(json), RuleRegistry.CreateCore());

    private static IReconciliationResult Run(CompiledProfile profile, (string LotId, string Qty, string Mv)[] left, (string LotId, string Qty, string Mv)[] right)
    {
        var map = new SourceMap<(string LotId, string Qty, string Mv)>().Text("LotId", row => row.LotId).Text("Qty", row => row.Qty).Text("Mv", row => row.Mv);
        return Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", left, map),
            RecordSources.FromObjects("right", right, map),
            ReferenceDataSet.Empty);
    }
}
