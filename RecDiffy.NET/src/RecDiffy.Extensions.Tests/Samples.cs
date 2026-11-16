namespace RecDiffy.Extensions.Tests;

public sealed record OmsFill(string ClOrdId, string ExecId, string Ticker, string Isin, DateOnly TradeDate, string Side, int Qty, decimal Price, decimal Fees)
{
    public decimal Cash => Side == "BUY" ? (Qty * Price) + Fees : (Qty * Price) - Fees;
}

public sealed record BrokerConfirm(string ConfirmRef, string Ticker, string Isin, DateOnly TradeDate, string Side, int Qty, decimal Price, decimal Fees)
{
    public decimal Cash => Side == "BUY" ? (Qty * Price) + Fees : (Qty * Price) - Fees;
}

public sealed record IborPosition(string Fund, string Ticker, string Isin, int Qty, decimal Price)
{
    public decimal MarketValue => Qty * Price;
}

public sealed record AborLot(string Fund, string Isin, string LotId, int Qty, decimal Price)
{
    public decimal MarketValue => Qty * Price;
}

internal static class Samples
{
    private static readonly DateOnly Day = new(2026, 11, 13);

    // The OMS-vs-broker sample: price is part of the match key, quantity is summed and cash is compared.
    // AAPL exact; MSFT three fills that add up, cash too; NVDA and JPM break on cash; XOM and GOOGL differ in price, so the
    // fill and the confirm stay unmatched; AMD has no confirm; META has no fill.
    public static RecResult<OmsFill, BrokerConfirm> OmsBroker()
    {
        OmsFill[] fills =
        [
            new("PM1-0001", "EX-45101", "AAPL", "US0378331005", Day, "BUY", 5_000, 228.40m, 150.00m),
            new("PM1-0002", "EX-45102", "MSFT", "US5949181045", Day, "BUY", 1_000, 415.20m, 30.00m),
            new("PM1-0002", "EX-45107", "MSFT", "US5949181045", Day, "BUY", 500, 415.20m, 15.00m),
            new("PM1-0002", "EX-45113", "MSFT", "US5949181045", Day, "BUY", 500, 415.20m, 15.00m),
            new("PM1-0003", "EX-45120", "NVDA", "US67066G1040", Day, "SELL", 3_000, 141.10m, 90.00m),
            new("PM1-0003", "EX-45121", "NVDA", "US67066G1040", Day, "SELL", 3_000, 141.10m, 90.00m),
            new("PM1-0003", "EX-45125", "NVDA", "US67066G1040", Day, "SELL", 2_000, 141.10m, 60.00m),
            new("PM1-0004", "EX-45130", "JPM", "US46625H1005", Day, "BUY", 3_000, 238.75m, 90.00m),
            new("PM1-0005", "EX-45141", "XOM", "US30231G1022", Day, "SELL", 6_000, 112.40m, 180.00m),
            new("PM1-0006", "EX-45152", "GOOGL", "US02079K3059", Day, "BUY", 2_400, 85.10m, 72.00m),
            new("PM1-0007", "EX-45160", "AMD", "US0079031078", Day, "BUY", 3_000, 146.20m, 90.00m),
        ];

        BrokerConfirm[] confirms =
        [
            new("CNF-0001", "AAPL", "US0378331005", Day, "BUY", 5_000, 228.40m, 150.00m),
            new("CNF-0002", "MSFT", "US5949181045", Day, "BUY", 2_000, 415.20m, 60.00m),
            new("CNF-0003", "NVDA", "US67066G1040", Day, "SELL", 8_000, 141.10m, 265.30m),
            new("CNF-0004", "JPM", "US46625H1005", Day, "BUY", 3_000, 238.75m, 105.00m),
            new("CNF-0005", "XOM", "US30231G1022", Day, "SELL", 6_000, 112.45m, 180.00m),
            new("CNF-0006", "GOOGL", "US02079K3059", Day, "BUY", 2_400, 85.11m, 72.00m),
            new("CNF-0007", "META", "US30303M1027", Day, "BUY", 1_200, 585.00m, 36.00m),
        ];

        return RecBuilder.Between(fills, confirms)
            .MatchOn(fill => fill.Isin, confirm => confirm.Isin)
                .And(fill => fill.TradeDate, confirm => confirm.TradeDate)
                .And(fill => fill.Side, confirm => confirm.Side)
                .And(fill => fill.Price, confirm => confirm.Price)
            .Sum(fill => fill.Qty, confirm => confirm.Qty)
            .Compare(fill => fill.Cash, confirm => confirm.Cash, tolerance: 0.01m)
            .Run();
    }

    // The IBOR-vs-ABOR sample: one position per fund and security against its tax lots, quantity summed, market value compared.
    public static RecResult<IborPosition, AborLot> IborAbor()
    {
        IborPosition[] ibor =
        [
            new("GROWTH", "AAPL", "US0378331005", 5_000, 228.40m),
            new("GROWTH", "MSFT", "US5949181045", 2_000, 415.20m),
            new("GROWTH", "NVDA", "US67066G1040", 8_000, 141.10m),
            new("GROWTH", "META", "US30303M1027", 1_200, 585.00m),
            new("INCOME", "JNJ", "US4781601046", 4_000, 156.30m),
            new("INCOME", "XOM", "US30231G1022", 6_000, 112.40m),
            new("INCOME", "AAPL", "US0378331005", 1_000, 228.40m),
        ];

        AborLot[] abor =
        [
            new("GROWTH", "US0378331005", "L-1001", 3_000, 228.40m),
            new("GROWTH", "US0378331005", "L-1002", 2_000, 228.40m),
            new("GROWTH", "US5949181045", "L-1003", 2_000, 415.21m),
            new("GROWTH", "US67066G1040", "L-1004", 7_000, 141.10m),
            new("GROWTH", "US88160R1014", "L-1005", 1_500, 321.20m),
            new("INCOME", "US4781601046", "L-2001", 2_500, 156.30m),
            new("INCOME", "US4781601046", "L-2002", 1_500, 156.30m),
            new("INCOME", "US30231G1022", "L-2003", 6_000, 112.95m),
            new("INCOME", "US0378331005", "L-2005", 600, 228.40m),
            new("INCOME", "US0378331005", "L-2006", 600, 228.40m),
        ];

        return RecBuilder.Between(ibor, abor)
            .MatchOn(position => position.Fund, lot => lot.Fund)
                .And(position => position.Isin, lot => lot.Isin)
            .Sum(position => position.Qty, lot => lot.Qty)
            .Compare(position => position.MarketValue, lot => lot.MarketValue, tolerance: 25.00m)
            .Run();
    }
}
