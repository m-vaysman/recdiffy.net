using RecDiffy.Excel.Tests.Support;
using RecDiffy.Results;

namespace RecDiffy.Excel.Tests;

public class ArgonExcelSniffTests
{
    private static readonly DateOnly AsOf = new(2026, 11, 4);

    [Fact]
    public void Clean_does_not_split_surrogate_pair_at_32767_character_boundary()
    {
        var prefix = new string('A', 32_766);
        var emoji = "\U0001F600"; // 2 UTF-16 code units: high surrogate at index 32766, low surrogate at index 32767
        var text = prefix + emoji;

        var profile = Reconciler.Compile(ProfileDocument.Parse("""
            {
              "formatVersion": 1,
              "profileId": "surrogate-boundary",
              "version": "1.0.0",
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Counterparty", "type": "Symbol", "role": "Key" },
                { "name": "Instrument", "type": "Symbol", "role": "Key" },
                { "name": "Side", "type": "Symbol", "role": "Key" },
                { "name": "Currency", "type": "Symbol", "role": "Economic" },
                { "name": "TradeDate", "type": "Date", "role": "Key" },
                { "name": "SettleDate", "type": "Date", "role": "Economic" }
              ],
              "normalization": {
                "left": {
                  "identify": [
                    { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                    { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" },
                    { "rule": "symbol.map", "from": "Side", "to": "Side" },
                    { "rule": "symbol.map", "from": "Currency", "to": "Currency" }
                  ],
                  "parse": [
                    { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
                    { "rule": "date.parse", "from": "SettleDate", "to": "SettleDate", "formats": ["yyyy-MM-dd"] }
                  ]
                }
              },
              "paths": [ { "id": "p", "route": { "rule": "route.all" } } ]
            }
            """), RuleRegistry.CreateCore());

        var map = new SourceMap<(string Id, string Cpty)>()
            .Text("Id", r => r.Id)
            .Text("Counterparty", r => r.Cpty)
            .Text("Instrument", _ => "EQ-1")
            .Text("Side", _ => "BUY")
            .Text("Currency", _ => "USD")
            .Text("TradeDate", _ => "2026-11-02")
            .Text("SettleDate", _ => "2026-11-04");

        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("left", [("L1", text)], map),
            RecordSources.FromObjects("right", Array.Empty<(string, string)>(), map),
            ReferenceDataSet.Empty);

        using var ms = new MemoryStream();
        var outcome = result.WriteReport(ReportKind.ExceptionBlotter, ms, new ExcelReportOptions { AsOf = AsOf, Profile = profile });
        Assert.True(outcome.Written);
    }

}
