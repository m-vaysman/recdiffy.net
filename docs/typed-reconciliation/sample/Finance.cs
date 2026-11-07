using System.Data;
using System.Globalization;
using RecDiffy;
using Sylvan.Data.Csv;
using Sylvan.Data.Excel;

// What the broker says we traded. One CSV line per confirmation.
public sealed record BrokerConfirm(
    string ConfirmId, string? TradeRef, DateOnly TradeDate, string Isin, string Side,
    decimal Quantity, decimal Price, decimal NetAmount);

// What our order management system booked. One Excel row per trade.
public sealed record BlotterTrade(
    string TradeId, DateTime ExecutedOn, string Isin, string BuySell,
    long Qty, decimal AvgPrice, decimal NetConsideration);

public static class Finance
{
    public static void Run(string folder)
    {
        Console.WriteLine("=== Finance: broker confirms (CSV) vs trade blotter (Excel) ===");

        var confirms = ReadConfirms(Path.Combine(folder, "broker-confirms.csv"));
        var blotter = ReadBlotter(Path.Combine(folder, "blotter.xlsx"));

        var result = RecBuilder.Between(confirms, blotter)
            .Named("broker-vs-blotter", "1.0.0")
            .MatchOn(confirm => confirm.TradeRef, trade => trade.TradeId)
            .ThenMatchOn(confirm => confirm.Isin, trade => trade.Isin)
                .And(confirm => confirm.TradeDate, trade => trade.ExecutedOn)
                .And(confirm => confirm.Side, trade => trade.BuySell)
            .Compare(confirm => confirm.Quantity, trade => trade.Qty)
            .Compare(confirm => confirm.Price, trade => trade.AvgPrice, tolerance: 0.0001m)
            .Compare(confirm => confirm.NetAmount, trade => trade.NetConsideration, tolerance: 0.01m)
            .Run();

        Report.Print(result, confirm => confirm.ConfirmId, trade => trade.TradeId);
        Console.WriteLine();
        Console.WriteLine(result.Profile.ToJson());
        Console.WriteLine();
    }

    // Reader 1: Sylvan.Data.Csv. Every CSV cell is text, so parse it with the invariant culture.
    static IEnumerable<BrokerConfirm> ReadConfirms(string path)
    {
        using var csv = CsvDataReader.Create(path);
        while (csv.Read())
            yield return new BrokerConfirm(
                ConfirmId: csv.GetString("ConfirmId"),
                TradeRef: csv.GetString("TradeRef") is { Length: > 0 } tradeRef ? tradeRef : null,
                TradeDate: DateOnly.ParseExact(csv.GetString("TradeDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Isin: csv.GetString("ISIN"),
                Side: csv.GetString("Side"),
                Quantity: decimal.Parse(csv.GetString("Quantity"), CultureInfo.InvariantCulture),
                Price: decimal.Parse(csv.GetString("Price"), CultureInfo.InvariantCulture),
                NetAmount: decimal.Parse(csv.GetString("NetAmount"), CultureInfo.InvariantCulture));
    }

    // Reader 2: Sylvan.Data.Excel. Excel stores numbers as doubles and dates as dates.
    static IEnumerable<BlotterTrade> ReadBlotter(string path)
    {
        using var excel = ExcelDataReader.Create(path);
        while (excel.Read())
            yield return new BlotterTrade(
                TradeId: excel.GetString("Trade ID"),
                ExecutedOn: excel.GetDateTime("Exec Date"),
                Isin: excel.GetString("ISIN"),
                BuySell: excel.GetString("B/S"),
                Qty: (long)excel.GetDouble("Qty"),
                AvgPrice: (decimal)excel.GetDouble("Avg Px"),
                NetConsideration: (decimal)excel.GetDouble("Net Consideration"));
    }
}
