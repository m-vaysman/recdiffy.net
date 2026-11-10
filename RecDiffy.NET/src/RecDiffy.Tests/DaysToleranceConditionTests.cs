using RecDiffy.Results;

namespace RecDiffy.Tests;

public class DaysToleranceConditionTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "days-tolerance-when",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Symbol", "role": "Key" },
            { "name": "AssetClass", "type": "Symbol", "role": "Identity" },
            { "name": "SettleDate", "type": "Date", "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "symbol.map", "from": "Ref", "to": "Ref" }, { "rule": "symbol.map", "from": "AssetClass", "to": "AssetClass" } ], "parse": [ { "rule": "date.parse", "from": "SettleDate", "to": "SettleDate", "formats": ["yyyy-MM-dd"] } ] },
            "right": { "identify": [ { "rule": "symbol.map", "from": "Ref", "to": "Ref" }, { "rule": "symbol.map", "from": "AssetClass", "to": "AssetClass" } ], "parse": [ { "rule": "date.parse", "from": "SettleDate", "to": "SettleDate", "formats": ["yyyy-MM-dd"] } ] }
          },
          "paths": [ {
            "id": "trades",
            "route": { "rule": "route.all" },
            "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ],
            "diff": { "fields": { "SettleDate": { "tolerance": { "days": 3, "when": { "field": "AssetClass", "equals": "Bond" } } } } }
          } ]
        }
        """;

    private static readonly SourceMap<(string Id, string Ref, string AssetClass, string SettleDate)> Map = new SourceMap<(string Id, string Ref, string AssetClass, string SettleDate)>()
        .Text("Id", row => row.Id).Text("Ref", row => row.Ref).Text("AssetClass", row => row.AssetClass).Text("SettleDate", row => row.SettleDate);

    [Fact]
    public void A_days_tolerance_applies_when_its_condition_holds_on_both_sides() =>
        Assert.Equal(FieldOutcome.WithinTolerance, SettleDate("Bond", "Bond"));

    [Fact(Skip = "🔴 #289: FieldDiffer.Date reads only Days and ignores the tolerance's when.")]
    public void A_days_tolerance_does_not_apply_when_its_condition_fails_on_both_sides() =>
        Assert.Equal(FieldOutcome.Break, SettleDate("Equity", "Equity"));

    [Fact(Skip = "🔴 #289: FieldDiffer.Date reads only Days and ignores the tolerance's when.")]
    public void A_days_tolerance_does_not_apply_when_its_condition_fails_on_one_side() =>
        Assert.Equal(FieldOutcome.Break, SettleDate("Bond", "Equity"));

    private static FieldOutcome SettleDate(string leftAsset, string rightAsset)
    {
        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", [("L1", "T-1", leftAsset, "2026-06-10")], Map),
            RecordSources.FromObjects("right", [("R1", "T-1", rightAsset, "2026-06-12")], Map),
            ReferenceDataSet.Empty);
        var entry = Assert.Single(result.Entries);
        return Assert.Single(entry.Diffs, diff => diff.Field.Name == "SettleDate").Outcome;
    }
}
