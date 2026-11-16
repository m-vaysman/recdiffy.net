<Query Kind="Program">
  <NuGetReference Version="1.2.0-preview.7" Prerelease="true">RecDiffy</NuGetReference>
  <NuGetReference Version="0.1.0-preview.1" Prerelease="true">RecDiffy.Extensions</NuGetReference>
  <Namespace>RecDiffy</Namespace>
  <Namespace>RecDiffy.Extensions</Namespace>
  <Namespace>RecDiffy.Results</Namespace>
  <RuntimeVersion>8.0</RuntimeVersion>
</Query>

// OMS fills vs broker confirms, trade date 13 Nov 2026 (US T+1, settles 16 Nov), with the RecDiffy.Extensions package.
// Price is part of the match key: a different price is not the trade we agreed, so it never matches.
// Qty is summed and Cash is compared, so several fills at one price match one confirm, and their cash must add up.
// Sample data only: the tickers and ISINs are real; the trades, IDs and fees are made up in real-world formats
// (FIX ClOrdID tag 11, ExecID tag 17, OrderID tag 37, SWIFT :20C::SEME// confirm reference).
//
// RecDiffy.Extensions has no dependencies. It works beside the RecDiffy package this query also references.
// Each table has two forms: result.ToXxxTable() returns a DataTable, result.ToXxxHtml() returns an HTML string.
void Main()
{
    Util.HtmlHead.AddStyles(HtmlTable.Css);

    var day = new DateOnly(2026, 11, 13);

    OmsFill[] fills =
    [
        new("PM1-20261113-0001", "20261113-000045101", "XNAS", "AAPL",  "US0378331005", day, "BUY",  5_000, 228.40m, 150.00m),

        new("PM1-20261113-0002", "20261113-000045102", "XNAS", "MSFT",  "US5949181045", day, "BUY",  1_000, 415.20m,  30.00m),
        new("PM1-20261113-0002", "20261113-000045107", "XNAS", "MSFT",  "US5949181045", day, "BUY",    500, 415.20m,  15.00m),
        new("PM1-20261113-0002", "20261113-000045113", "XNAS", "MSFT",  "US5949181045", day, "BUY",    500, 415.20m,  15.00m),

        new("PM1-20261113-0003", "20261113-000045120", "XNAS", "NVDA",  "US67066G1040", day, "SELL", 3_000, 141.10m,  90.00m),
        new("PM1-20261113-0003", "20261113-000045121", "XNAS", "NVDA",  "US67066G1040", day, "SELL", 3_000, 141.10m,  90.00m),
        new("PM1-20261113-0003", "20261113-000045125", "XNAS", "NVDA",  "US67066G1040", day, "SELL", 2_000, 141.10m,  60.00m),

        new("PM1-20261113-0004", "20261113-000045130", "XNYS", "JPM",   "US46625H1005", day, "BUY",  3_000, 238.75m,  90.00m),
        new("PM1-20261113-0005", "20261113-000045141", "XNYS", "XOM",   "US30231G1022", day, "SELL", 6_000, 112.40m, 180.00m),
        new("PM1-20261113-0006", "20261113-000045152", "XNAS", "GOOGL", "US02079K3059", day, "BUY",  2_400,  85.10m,  72.00m),
        new("PM1-20261113-0007", "20261113-000045160", "XNAS", "AMD",   "US0079031078", day, "BUY",  3_000, 146.20m,  90.00m),
    ];

    BrokerConfirm[] confirms =
    [
        new("CNF2611130001", "OB7731920045", "AAPL",  "US0378331005", day, "BUY",  5_000, 228.40m, 150.00m),   // same trade
        new("CNF2611130002", "OB7731920046", "MSFT",  "US5949181045", day, "BUY",  2_000, 415.20m,  60.00m),   // 3 fills, cash adds up
        new("CNF2611130003", "OB7731920047", "NVDA",  "US67066G1040", day, "SELL", 8_000, 141.10m, 265.30m),   // 3 fills, broker adds a 25.30 fee
        new("CNF2611130004", "OB7731920048", "JPM",   "US46625H1005", day, "BUY",  3_000, 238.75m, 105.00m),   // commission 0.035 vs 0.03
        new("CNF2611130005", "OB7731920049", "XOM",   "US30231G1022", day, "SELL", 6_000, 112.45m, 180.00m),   // different price
        new("CNF2611130006", "OB7731920050", "GOOGL", "US02079K3059", day, "BUY",  2_400,  85.11m,  72.00m),   // different price
        new("CNF2611130007", "OB7731920051", "META",  "US30303M1027", day, "BUY",  1_200, 585.00m,  36.00m),   // no fill in the OMS
    ];

    Util.HorizontalRun(true,
        Util.RawHtml(fills.ToHtmlTable($"OMS fills ({fills.Length})")),
        Util.RawHtml(confirms.ToHtmlTable($"Broker confirms ({confirms.Length})"))).Dump();

    var result = RecBuilder.Between(fills, confirms)
        .MatchOn(f => f.Isin, c => c.Isin)
            .And(f => f.TradeDate, c => c.TradeDate)
            .And(f => f.Side, c => c.Side)
            .And(f => f.Price, c => c.Price)
        .Sum(f => f.Qty, c => c.Qty)
        .Compare(f => f.Cash, c => c.Cash, tolerance: 0.01m)
        .Run();

    // One row per group: status, shape (3 ↔ 1), the fills, the confirm and what differs.
    Util.RawHtml(result.ToSummaryHtml(
        f => $"{f.ExecId} {f.Qty:N0} @ {f.Price:N2}",
        c => $"{c.ConfirmRef} {c.Qty:N0} @ {c.Price:N2}",
        "OMS vs broker")).Dump();

    Util.RawHtml(result.ToStatusCountsHtml("Groups by status")).Dump();

    // Price breaks are a domain rule, so they stay in the query: OMS fills and a confirm on the same security, day, side
    // and quantity, at different prices.
    var priceBreaks = result.LeftOnly
        .GroupBy(f => (f.Isin, f.TradeDate, f.Side))
        .Join(result.RightOnly,
              group => group.Key,
              confirm => (confirm.Isin, confirm.TradeDate, confirm.Side),
              (group, confirm) => (Fills: group.ToArray(), Confirm: confirm))
        .Where(pair => pair.Fills.Sum(f => f.Qty) == pair.Confirm.Qty)
        .Select(pair => new
        {
            Status   = "Break",
            Security = $"{pair.Confirm.Ticker} {pair.Confirm.Side}",
            Fills    = string.Join(" · ", pair.Fills.Select(f => $"{f.ExecId} {f.Qty:N0} @ {f.Price:N2}")),
            Confirm  = $"{pair.Confirm.ConfirmRef} {pair.Confirm.Qty:N0} @ {pair.Confirm.Price:N2}",
            Cause    = "Price differs: not the trade we agreed, so it does not match",
        })
        .ToArray();
    Util.RawHtml(priceBreaks.ToHtmlTable("Price breaks")).Dump();

    Util.RawHtml(result.ToReconciliationHtml("Every row, with its status")).Dump();

    var spec = new SideBySideSpec(["Side"], [], [new MeasureSpec("Qty"), new MeasureSpec("Cash")], null);
    Util.RawHtml(result.ToSideBySideHtml(spec, "Qty and cash by side: Delta = LeftOnly + RightOnly + MatchedDelta")).Dump();

    // The same tables as DataTables, to query, filter or export.
    result.ToReconciliationTable("OmsBroker").Dump("DataTable: every row");
    result.ToSideBySideTable(spec, "CashBySide").Dump("DataTable: side by side");
}

record OmsFill(string ClOrdId, string ExecId, string Venue, string Ticker, string Isin, DateOnly TradeDate, string Side, int Qty, decimal Price, decimal Fees)
{
    public decimal Cash => Side == "BUY" ? Qty * Price + Fees : Qty * Price - Fees;
}

record BrokerConfirm(string ConfirmRef, string BrokerOrderId, string Ticker, string Isin, DateOnly TradeDate, string Side, int Qty, decimal Price, decimal Fees)
{
    public decimal Cash => Side == "BUY" ? Qty * Price + Fees : Qty * Price - Fees;
}
