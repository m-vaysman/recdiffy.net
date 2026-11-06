using RecDiffy.Excel;

namespace RecDiffy.Samples;

public static class ExcelReports
{
    public static bool Run()
    {
        SampleReport.Heading("6. Excel reports");
        var profile = Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore());
        var folder = Path.Combine(Path.GetTempPath(), "recdiffy-samples", "excel");
        var outcomes = RecDiffyExcel.RunAndWrite(
            profile,
            RecordSources.FromObjects("book", Book, Map()),
            RecordSources.FromObjects("street", Street, Map()),
            ReferenceDataSet.Empty,
            folder,
            new ExcelReportOptions { AsOf = new DateOnly(2025, 6, 13), Title = "Sample desk" });
        foreach (var outcome in outcomes)
            Console.WriteLine(outcome.Written ? $"wrote {outcome.Path}" : $"skipped {outcome.FileName}: {outcome.SkipReason}");
        return outcomes.Where(outcome => outcome.Written).Select(outcome => outcome.Kind).ToHashSet()
            .IsSupersetOf([ReportKind.ExceptionBlotter, ReportKind.SideBySideCompare, ReportKind.CashLadder, ReportKind.ControlTotals, ReportKind.Scorecard]);
    }

    private sealed record Trade(string Id, string Counterparty, string Instrument, string Asset, string Side, string Quantity, string Price, string NetCash, string SettleDate, string Currency = "USD");

    private static readonly Trade[] Book =
    [
        new("B1", "CITI", "US91282CT687", "Bond", "SELL", "3000000", "99.5", "2985000.00", "2025-06-13"),
        new("B2", "CITI", "AAPL", "Equity", "BUY", "1000", "201.10", "201100.00", "2025-06-12"),
        new("B3", "JPM", "MSFT", "Equity", "BUY", "500", "450.00", "225000.00", "2025-06-16"),
    ];

    private static readonly Trade[] Street =
    [
        new("S1", "CITI", "US91282CT687", "Bond", "SELL", "3000000", "99.5", "2985000.00", "2025-06-13"),
        new("S2", "CITI", "AAPL", "Equity", "BUY", "1000", "201.15", "201150.00", "2025-06-12"),
        new("S4", "JPM", "NVDA", "Equity", "SELL", "200", "130.00", "26000.00", "2025-06-16"),
    ];

    private static SourceMap<Trade> Map() =>
        new SourceMap<Trade>()
            .Text("Id", trade => trade.Id)
            .Text("Counterparty", trade => trade.Counterparty)
            .Text("Instrument", trade => trade.Instrument)
            .Text("Asset", trade => trade.Asset)
            .Text("Side", trade => trade.Side)
            .Text("TradeDate", _ => "2025-06-11")
            .Text("SettleDate", trade => trade.SettleDate)
            .Text("Currency", trade => trade.Currency)
            .Text("Quantity", trade => trade.Quantity)
            .Text("Price", trade => trade.Price)
            .Text("NetCash", trade => trade.NetCash);

    private const string Side = """
        {
          "identify": [
            { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
            { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" },
            { "rule": "symbol.map", "from": "Asset", "to": "Asset" },
            { "rule": "symbol.map", "from": "Side", "to": "Side" },
            { "rule": "symbol.map", "from": "Currency", "to": "Currency" }
          ],
          "parse": [
            { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
            { "rule": "date.parse", "from": "SettleDate", "to": "SettleDate", "formats": ["yyyy-MM-dd"] },
            { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
            { "rule": "decimal.parse", "from": "Price", "to": "Price" },
            { "rule": "decimal.parse", "from": "NetCash", "to": "NetCash" }
          ]
        }
        """;

    private static string Profile => $$"""
        {
          "formatVersion": 1,
          "profileId": "sample-desk",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Instrument", "type": "Symbol", "role": "Key" },
            { "name": "Asset", "type": "Symbol", "role": "Key" },
            { "name": "TradeDate", "type": "Date", "role": "Key" },
            { "name": "Side", "type": "Symbol", "role": "Key" },
            { "name": "SettleDate", "type": "Date", "role": "Economic" },
            { "name": "Currency", "type": "Symbol", "role": "Economic" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Price", "type": "Decimal", "scale": 6, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": { "left": {{Side}}, "right": {{Side}} },
          "paths": [ { "id": "trades", "route": { "rule": "route.all" }, "passes": [
            { "id": "P2", "pass": "match.naturalKey", "key": ["Counterparty", "Instrument", "TradeDate", "Side", "Quantity"] }
          ] } ]
        }
        """;
}
