using RecDiffy.Results;

namespace RecDiffy.Tests;

public class DaysToleranceWhenTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "days-tolerance-when-cases",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Symbol", "role": "Key" },
            { "name": "AssetClass", "type": "Symbol", "role": "Identity" },
            { "name": "SettleDate", "type": "Date", "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "symbol.map", "from": "Ref", "to": "Ref" },
                { "rule": "symbol.map", "from": "AssetClass", "to": "AssetClass" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "SettleDate", "to": "SettleDate", "formats": ["yyyy-MM-dd"] }
              ]
            },
            "right": {
              "identify": [
                { "rule": "symbol.map", "from": "Ref", "to": "Ref" },
                { "rule": "symbol.map", "from": "AssetClass", "to": "AssetClass" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "SettleDate", "to": "SettleDate", "formats": ["yyyy-MM-dd"] }
              ]
            }
          },
          "paths": [ {
            "id": "trades",
            "route": { "rule": "route.all" },
            "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ],
            "diff": { "fields": { "SettleDate": { "tolerance": { "days": 2, "when": { "field": "AssetClass", "in": ["Bond", "Swap"] } } } } }
          } ]
        }
        """;

    private static readonly SourceMap<(string Id, string Ref, string AssetClass, string SettleDate)> Map = new SourceMap<(string Id, string Ref, string AssetClass, string SettleDate)>()
        .Text("Id", row => row.Id)
        .Text("Ref", row => row.Ref)
        .Text("AssetClass", row => row.AssetClass)
        .Text("SettleDate", row => row.SettleDate);

    [Theory]
    [InlineData("Bond", "Swap", "2026-06-10", "2026-06-12", FieldOutcome.WithinTolerance, 2)]
    [InlineData("Bond", "Bond", "2026-06-10", "2026-06-13", FieldOutcome.Break, 3)]
    [InlineData("Equity", "Bond", "2026-06-10", "2026-06-11", FieldOutcome.Break, 1)]
    [InlineData("Bond", "Equity", "2026-06-10", "2026-06-11", FieldOutcome.Break, 1)]
    [InlineData("Equity", "Equity", "2026-06-10", "2026-06-11", FieldOutcome.Break, 1)]
    public void Days_tolerance_requires_when_on_both_sides_and_preserves_days_tolerance_id_and_delta(
        string leftAsset,
        string rightAsset,
        string leftDate,
        string rightDate,
        FieldOutcome expectedOutcome,
        int expectedDays)
    {
        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", [("L1", "T-1", leftAsset, leftDate)], Map),
            RecordSources.FromObjects("right", [("R1", "T-1", rightAsset, rightDate)], Map),
            ReferenceDataSet.Empty);

        var entry = Assert.Single(result.Entries);
        var diff = Assert.Single(entry.Diffs, item => item.Field.Name == "SettleDate");
        Assert.Equal(expectedOutcome, diff.Outcome);
        Assert.Equal("Days", diff.Tolerance.Value);
        Assert.Equal(expectedDays, diff.Delta.Days);
    }

    [Fact]
    public void Equal_dates_are_exact_even_when_days_tolerance_when_fails()
    {
        var result = Reconciler.Run(
            Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore()),
            RecordSources.FromObjects("left", [("L1", "T-1", "Equity", "2026-06-10")], Map),
            RecordSources.FromObjects("right", [("R1", "T-1", "Equity", "2026-06-10")], Map),
            ReferenceDataSet.Empty);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
        Assert.Empty(entry.Diffs);
    }
}
