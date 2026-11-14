namespace RecDiffy.Excel.Tests.Support;

// A small dataset for what fixture-v1 lacks: lifecycle chains (right side NEW, CANC, CORR), a two-leg package (left side), a group-sum allocation,
// absolute and relative diff tolerances, net cash, unmatched rows on both sides, and price, quantity and settle-date breaks.
internal static class SyntheticData
{
    public static readonly DateOnly AsOf = new(2026, 11, 4);

    private static readonly Lazy<(CompiledProfile Profile, IReconciliationResult Result)> Cached = new(Run);

    public static CompiledProfile Profile => Cached.Value.Profile;

    public static IReconciliationResult Result => Cached.Value.Result;

    public sealed record Trade(
        string Id,
        string Counterparty,
        string Instrument,
        string Asset,
        string Side,
        string Quantity,
        string Price,
        string NetCash,
        string Currency = "USD",
        string SettleDate = "2026-11-04",
        string TradeDate = "2026-11-02",
        string? Uti = null,
        string? Accrued = null,
        string? Fees = null,
        string? Book = null,
        string? EventAction = null,
        string? EventTarget = null,
        string? PackageRef = null,
        string? LegRole = null);

    public static IReadOnlyList<Trade> Ours { get; } =
    [
        new("L1", "ACME", "BOND-A", "Bond", "BUY", "1000000", "99.5", "996210.00", Uti: "U1", Accrued: "1200.00", Fees: "10.00", Book: "RATES"),
        new("L2", "ACME", "EQ-1", "Equity", "SELL", "5000", "20.00", "99995.00", Fees: "5.00", Book: "CASH"),
        new("L3", "BETA", "BOND-B", "Bond", "SELL", "200000", "101.25", "203000.00", "EUR", "2026-11-05", Uti: "U3", Accrued: "500.00", Book: "RATES"),
        new("L4", "BETA", "EQ-2", "Equity", "BUY", "1000", "50.00", "50000.00", "EUR", "2026-11-03", Uti: "U4", Book: "CASH"),
        new("L5", "ACME", "BOND-A", "Bond", "SELL", "500000", "99.5", "498100.00", Uti: "U5", Accrued: "600.00", Book: "RATES"),
        new("L6a", "GAMMA", "EQ-3", "Equity", "BUY", "300", "10.00", "3000.00", "GBP", Book: "FUND-1"),
        new("L6b", "GAMMA", "EQ-3", "Equity", "BUY", "200", "10.00", "2000.00", "GBP", Book: "FUND-2"),
        new("L6c", "GAMMA", "EQ-3", "Equity", "BUY", "500", "10.00", "5000.00", "GBP", Book: "FUND-3"),
        new("L7a", "DELTA", "SWAP-1", "Swap", "PAY_FIXED", "1000000", "3.5", "278961.22", Uti: "U7", Book: "SWAPS", PackageRef: "PKG-1", LegRole: "LEG_1_PAY"),
        new("L7b", "DELTA", "SWAP-1", "Swap", "REC_FIXED", "1000000", "3.2", "259124.12", Uti: "U7", Book: "SWAPS", PackageRef: "PKG-1", LegRole: "LEG_2_RECEIVE"),
        new("L8", "ACME", "EQ-4", "Equity", "BUY", "2000", "105.149", "210298.00", Uti: "U8", Book: "CASH"),
        new("L9", "EPSILON", "BOND-C", "Bond", "BUY", "250000", "100.4", "251000.00", SettleDate: "2026-11-03", Book: "RATES"),
        new("L11", "ZETA", "EQ-6", "Equity", "SELL", "1000", "10.00", "10000.00", Book: "CASH"),
    ];

    public static IReadOnlyList<Trade> Theirs { get; } =
    [
        new("R1", "ACME", "BOND-A", "Bond", "BUY", "1000000", "99.5", "996210.00", Uti: "U1", Accrued: "1200.00", Fees: "10.00"),
        new("R2", "ACME", "EQ-1", "Equity", "SELL", "5000", "20.002", "99995.00", Fees: "5.00"),
        new("R3", "BETA", "BOND-B", "Bond", "SELL", "200000", "101.25", "203000.00", "EUR", "2026-11-05", Uti: "U3", Accrued: "501.25"),
        new("R4", "BETA", "EQ-2", "Equity", "BUY", "1000", "50.50", "50500.00", "EUR", "2026-11-03", Uti: "U4"),
        new("R5", "ACME", "BOND-A", "Bond", "SELL", "500000", "99.5", "498100.00", SettleDate: "2026-11-05", Uti: "U5", Accrued: "600.00"),
        new("R6", "GAMMA", "EQ-3", "Equity", "BUY", "1000", "10.00", "10000.00", "GBP"),
        new("R7", "DELTA", "SWAP-1", "Swap", "PAY_FIXED", "1000000", "3.5", "19837.10", Uti: "U7"),
        new("R8n", "ACME", "EQ-4", "Equity", "BUY", "2000", "102.604", "205208.00", Uti: "U8", EventAction: "NEW"),
        new("R8c", "ACME", "EQ-4", "Equity", "BUY", "2000", "102.604", "205208.00", Uti: "U8", EventAction: "CANC", EventTarget: "R8n"),
        new("R8r", "ACME", "EQ-4", "Equity", "BUY", "2000", "105.149", "210298.00", Uti: "U8", EventAction: "CORR", EventTarget: "R8c"),
        new("R10", "EPSILON", "EQ-5", "Equity", "SELL", "100", "45.00", "4500.00", "EUR", "2026-11-05"),
        new("R11", "ZETA", "EQ-6", "Equity", "SELL", "800", "10.00", "8000.00"),
    ];

    public static RecordSource Source(string name, IReadOnlyList<Trade> trades) => RecordSources.FromObjects(name, trades, Map());

    public static ExcelReportOptions Options(params ReportKind[] reports) => new()
    {
        AsOf = AsOf,
        Profile = Profile,
        Title = "Synthetic lifecycle, package, allocation and tolerance run",
        Reports = reports.Length == 0 ? null : reports,
    };

    private static (CompiledProfile, IReconciliationResult) Run()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(ProfileJson), RuleRegistry.CreateCore());
        return (profile, Reconciler.Run(profile, Source("ours", Ours), Source("theirs", Theirs), ReferenceDataSet.Empty));
    }

    private static SourceMap<Trade> Map() =>
        new SourceMap<Trade>()
            .Text("Id", trade => trade.Id)
            .Text("Counterparty", trade => trade.Counterparty)
            .Text("Instrument", trade => trade.Instrument)
            .Text("Asset", trade => trade.Asset)
            .Text("Side", trade => trade.Side)
            .Text("TradeDate", trade => trade.TradeDate)
            .Text("SettleDate", trade => trade.SettleDate)
            .Text("Currency", trade => trade.Currency)
            .Text("Quantity", trade => trade.Quantity)
            .Text("Price", trade => trade.Price)
            .Text("Accrued", trade => trade.Accrued)
            .Text("Fees", trade => trade.Fees)
            .Text("NetCash", trade => trade.NetCash)
            .Text("Uti", trade => trade.Uti)
            .Text("Book", trade => trade.Book)
            .Text("EventAction", trade => trade.EventAction)
            .Text("EventTarget", trade => trade.EventTarget)
            .Text("PackageRef", trade => trade.PackageRef)
            .Text("LegRole", trade => trade.LegRole);

    private const string Normalization = """
        {
          "identify": [
            { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
            { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" },
            { "rule": "symbol.map", "from": "Asset", "to": "Asset" },
            { "rule": "symbol.map", "from": "Side", "to": "Side" },
            { "rule": "symbol.map", "from": "Currency", "to": "Currency" },
            { "rule": "identifier.map", "from": "Uti", "to": "Uti", "scheme": "UTI" },
            { "rule": "text.copy", "from": "Book", "to": "Book" },
            { "rule": "symbol.map", "from": "EventAction", "to": "EventAction" },
            { "rule": "text.copy", "from": "EventTarget", "to": "EventTarget" },
            { "rule": "text.copy", "from": "PackageRef", "to": "PackageRef" },
            { "rule": "symbol.map", "from": "LegRole", "to": "LegRole" }
          ],
          "parse": [
            { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
            { "rule": "date.parse", "from": "SettleDate", "to": "SettleDate", "formats": ["yyyy-MM-dd"] },
            { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
            { "rule": "decimal.parse", "from": "Price", "to": "Price" },
            { "rule": "decimal.parse", "from": "Accrued", "to": "Accrued" },
            { "rule": "decimal.parse", "from": "Fees", "to": "Fees" },
            { "rule": "decimal.parse", "from": "NetCash", "to": "NetCash" }
          ]
        }
        """;

    public static string ProfileJson => $$"""
        {
          "formatVersion": 1,
          "profileId": "synthetic",
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
            { "name": "Accrued", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Fees", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Uti", "type": "Identifier", "role": "Identity" },
            { "name": "Book", "type": "Text", "role": "Text" },
            { "name": "EventAction", "type": "Symbol", "role": "Lifecycle" },
            { "name": "EventTarget", "type": "Text", "role": "Lifecycle" },
            { "name": "PackageRef", "type": "Text", "role": "Package" },
            { "name": "LegRole", "type": "Symbol", "role": "Package" }
          ],
          "normalization": { "left": {{Normalization}}, "right": {{Normalization}} },
          "consolidation": {
            "left": {
              "packages": [ {
                "link": "PackageRef", "role": "LegRole", "requiredRoles": ["LEG_1_PAY", "LEG_2_RECEIVE"],
                "aggregate": { "Quantity": "Same", "Price": "FromRole(LEG_1_PAY)", "Side": "FromRole(LEG_1_PAY)", "NetCash": "DifferenceByRole(LEG_1_PAY,LEG_2_RECEIVE)" }
              } ]
            },
            "right": { "lifecycle": { "action": "EventAction", "target": "EventTarget", "scope": "Counterparty", "corrTarget": "CANC" } }
          },
          "paths": [ {
            "id": "trades",
            "route": { "rule": "route.all" },
            "passes": [
              { "id": "P1", "pass": "match.naturalKey", "key": ["Uti"] },
              { "id": "P2", "pass": "match.naturalKey", "key": ["Counterparty", "Instrument", "TradeDate", "Side", "Quantity"] },
              { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Counterparty", "Instrument", "TradeDate", "Side", "Price"], "conserve": ["Quantity", "NetCash"] },
              { "id": "P4", "pass": "match.reducedKey", "key": ["Counterparty", "Instrument", "TradeDate", "Side"] }
            ],
            "diff": { "fields": {
              "Accrued": { "tolerance": { "absolute": "1.25" } },
              "Fees": { "tolerance": { "absolute": "0.05" } },
              "Price": { "tolerance": { "relative": "0.0001" } }
            } }
          } ]
        }
        """;
}
