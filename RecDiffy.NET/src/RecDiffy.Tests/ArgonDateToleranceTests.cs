using RecDiffy.Results;

namespace RecDiffy.Tests;

public class ArgonDateToleranceTests
{
    [Fact]
    public void Date_days_tolerance_allows_dates_within_configured_days()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse("""
            {
              "formatVersion": 1,
              "profileId": "date-days-tolerance",
              "version": "1.0.0",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Sku", "type": "Symbol", "role": "Key" },
                { "name": "SettleDate", "type": "Date", "role": "Economic" }
              ],
              "normalization": {
                "left": {
                  "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ],
                  "parse": [ { "rule": "date.parse", "from": "SettleDate", "to": "SettleDate", "formats": ["yyyy-MM-dd"] } ]
                },
                "right": {
                  "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ],
                  "parse": [ { "rule": "date.parse", "from": "SettleDate", "to": "SettleDate", "formats": ["yyyy-MM-dd"] } ]
                }
              },
              "paths": [ {
                "id": "p",
                "route": { "rule": "route.all" },
                "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Sku"] } ],
                "diff": { "fields": { "SettleDate": { "tolerance": { "days": 2 } } } }
              } ]
            }
            """), RuleRegistry.CreateCore());

        var map = new SourceMap<(string Id, string Sku, string Date)>()
            .Text("Id", r => r.Id)
            .Text("Sku", r => r.Sku)
            .Text("SettleDate", r => r.Date);

        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", [("L1", "A", "2025-06-12")], map),
            RecordSources.FromObjects("right", [("R1", "A", "2025-06-13")], map),
            ReferenceDataSet.Empty);

        Assert.Equal(FieldOutcome.WithinTolerance, Assert.Single(result.Entries).Outcome);
    }
}
