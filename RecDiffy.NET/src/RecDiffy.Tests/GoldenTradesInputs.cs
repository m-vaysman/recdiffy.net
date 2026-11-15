using System.Data;
using System.Globalization;
using RecDiffy.Results;

namespace RecDiffy.Tests;

// The fixed inputs: an internal blotter on the left, read as en-US, and a street report on the right, read as de-DE with
// dates in each counterparty's format. Core rules and the trading pack normalize them, and three routes match them.
internal static class GoldenTrades
{
    public const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "golden-results",
          "version": "1.0.0",
          "rounding": { "midpoint": "ToEven" },
          "culture": "en-US",
          "sources": { "left": { "rowKey": "Trade_Id" }, "right": { "rowKey": "Report_Id", "culture": "de-DE" } },
          "fields": [
            { "name": "Counterparty", "type": "Identifier", "role": "Key" },
            { "name": "Instrument", "type": "Identifier", "role": "Key" },
            { "name": "AssetClass", "type": "Symbol", "role": "Key" },
            { "name": "TradeDate", "type": "Date", "role": "Key" },
            { "name": "Side", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Price", "type": "Decimal", "scale": 6, "role": "Economic" },
            { "name": "Accrued", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Fees", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "SettleDate", "type": "Date", "role": "Economic" },
            { "name": "Uti", "type": "Identifier", "role": "Identity" },
            { "name": "EventAction", "type": "Symbol", "role": "Lifecycle" },
            { "name": "EventTarget", "type": "Text", "role": "Lifecycle" },
            { "name": "PackageRef", "type": "Text", "role": "Package" },
            { "name": "LegRole", "type": "Symbol", "role": "Package" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "party.resolveAlias", "from": "Cpty", "to": "Counterparty" },
                { "rule": "identifier.map", "from": "Isin", "to": "Instrument", "scheme": "ISIN" },
                { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" },
                { "rule": "identifier.map", "from": "Uti_Ref", "to": "Uti", "scheme": "UTI" },
                { "rule": "text.copy", "from": "Parent", "to": "PackageRef" },
                { "rule": "symbol.map", "from": "Leg", "to": "LegRole" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "Trade_Dt", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
                { "rule": "date.parse", "from": "Settle_Dt", "to": "SettleDate", "formats": ["yyyy-MM-dd"] },
                { "rule": "decimal.parse", "from": "Px", "to": "Price" },
                { "rule": "decimal.parse", "from": "Acc", "to": "Accrued" },
                { "rule": "decimal.parse", "from": "Fee", "to": "Fees" },
                { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" }
              ],
              "derive": [
                { "rule": "side.resolve", "code": "Dir", "quantity": "Qty" },
                { "rule": "quantity.absolute", "from": "Qty", "to": "Quantity" }
              ],
              "validate": [ { "rule": "required.present", "fields": ["Counterparty", "TradeDate"] } ]
            },
            "right": {
              "identify": [
                { "rule": "party.resolveLei", "from": "Lei", "to": "Counterparty" },
                { "rule": "identifier.map", "from": "Isin", "to": "Instrument", "scheme": "ISIN" },
                { "rule": "symbol.map", "from": "Product", "to": "AssetClass", "map": { "BD": "Bond", "EQ": "Equity", "SW": "Swap" } },
                { "rule": "identifier.map", "from": "Uti_Ref", "to": "Uti", "scheme": "UTI" },
                { "rule": "symbol.map", "from": "Event", "to": "EventAction" },
                { "rule": "text.copy", "from": "Prior", "to": "EventTarget" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "Trade_Dt", "to": "TradeDate", "formatBy": "Counterparty" },
                { "rule": "date.parse", "from": "Settle_Dt", "to": "SettleDate", "formatBy": "Counterparty" },
                { "rule": "price.from32nds", "from": "Px", "to": "Price", "when": { "field": "Conv", "equals": "32NDS" } },
                { "rule": "decimal.parse", "from": "Px", "to": "Price", "when": { "field": "Conv", "notEquals": "32NDS" } },
                { "rule": "decimal.parse", "from": "Acc", "to": "Accrued" },
                { "rule": "decimal.parse", "from": "Fee", "to": "Fees" },
                { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" }
              ],
              "derive": [
                { "rule": "side.resolve", "code": "Dir", "quantity": "Qty", "invertWhen": "DEALER", "perspective": "View" },
                { "rule": "quantity.absolute", "from": "Qty", "to": "Quantity" }
              ],
              "validate": [ { "rule": "required.present", "fields": ["Counterparty", "TradeDate"] } ]
            }
          },
          "consolidation": {
            "left": { "packages": [ {
              "link": "PackageRef", "role": "LegRole", "requiredRoles": ["PAY", "REC"],
              "aggregate": { "Quantity": "Same", "Price": "FromRole(PAY)", "Side": "FromRole(PAY)", "NetCash": "DifferenceByRole(PAY,REC)", "Fees": "Sum" }
            } ] },
            "right": { "lifecycle": { "action": "EventAction", "target": "EventTarget", "scope": "Counterparty", "corrTarget": "CANC" } }
          },
          "paths": [
            {
              "id": "bonds",
              "route": { "rule": "route.byField", "field": "AssetClass", "equals": "Bond" },
              "passes": [
                { "id": "P1", "pass": "match.sharedRef", "key": ["Uti"] },
                { "id": "P2", "pass": "match.naturalKey", "key": ["Counterparty", "Instrument", "TradeDate", "Side", "Quantity"] },
                { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Counterparty", "Instrument", "TradeDate", "Side", "Price"], "conserve": ["Quantity", "NetCash"] },
                { "id": "P4", "pass": "match.reducedKey", "key": ["Counterparty", "Instrument", "TradeDate", "Side"] }
              ],
              "diff": { "fields": {
                "Quantity": { "tolerance": { "relative": 0.0001 } },
                "Accrued": { "tolerance": { "absolute": 1.25 }, "attributed": "trading.proRataToQuantity", "applicableWhen": { "field": "AssetClass", "in": ["Bond", "Swap"] } },
                "NetCash": { "tolerance": { "attributed": "trading.netCash" } },
                "SettleDate": { "tolerance": { "days": 1 } }
              } }
            },
            {
              "id": "swaps",
              "route": { "rule": "route.byField", "field": "AssetClass", "equals": "Swap" },
              "passes": [
                { "id": "P1", "pass": "match.sharedRef", "key": ["Uti"] },
                { "id": "P2", "pass": "match.naturalKey", "key": ["Counterparty", "Instrument", "TradeDate", "Side", "Quantity"] }
              ],
              "diff": { "fields": { "NetCash": { "tolerance": { "absolute": 0.05 } } } }
            },
            {
              "id": "equities",
              "route": { "rule": "route.byField", "field": "AssetClass", "equals": "Equity" },
              "passes": [
                { "id": "P2", "pass": "match.naturalKey", "key": ["Counterparty", "Instrument", "TradeDate", "Side", "Quantity"] },
                { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Counterparty", "Instrument", "TradeDate", "Side", "Price"], "conserve": ["Quantity", "NetCash"] }
              ]
            }
          ],
          "limits": { "maxBlockItems": 4 }
        }
        """;

    private const string Alfa = "5493000000000000ALFA";
    private const string Brav = "5493000000000000BRAV";
    private const string Char = "5493000000000000CHAR";

    public static readonly ReferenceDataSet Reference = ReferenceDataSet.Create(
        "golden-reference",
        new DateOnly(2026, 3, 6),
        "golden-reference-hash",
        new Dictionary<string, string>(StringComparer.Ordinal) { [Alfa] = "ALFA", [Brav] = "BRAV", [Char] = "CHAR" },
        new Dictionary<string, string>(StringComparer.Ordinal) { ["Alfa Bank"] = "ALFA", ["Bravo Sec"] = "BRAV", ["Charlie Capital"] = "CHAR" },
        new Dictionary<string, string>(StringComparer.Ordinal) { ["ALFA"] = "yyyy-MM-dd", ["BRAV"] = "dd/MM/yyyy", ["CHAR"] = "MM/dd/yyyy" });

    public static readonly Internal[] Left =
    [
        // An exact match on the shared reference. The street quotes the price in 32nds and reports from the dealer's side.
        new("OMS-EXACT", "Alfa Bank", "US0000000001", "Bond", "2026-03-02", "B", "3,000,000.00", "96.078125", "12,500.00", "150.00", "2,894,993.75", "2026-03-04", "UTI-EXACT"),
        // Within tolerance on the natural key: accrued is 0.94 lower, net cash follows it, and settlement is a day later.
        new("OMS-WITHIN", "Bravo Sec", "US0000000002", "Bond", "2026-03-02", "S", "-2,000,000", "101.5", "8,333.33", "100.00", "2,038,233.33", "2026-03-04"),
        // A break on the shared reference: the price differs, and net cash is attributed to it.
        new("OMS-BREAK", "Alfa Bank", "US0000000003", "Bond", "2026-03-03", "B", "1,000,000", "96.572", "5,000.00", "50.00", "970,770.00", "2026-03-05", "UTI-BREAK"),
        // Left only, twice on one route.
        new("OMS-ORPHAN", "Charlie Capital", "US0000000004", "Bond", "2026-03-03", "S", "500,000", "100", null, null, "500,000.00", "2026-03-05"),
        new("OMS-ORPHAN-2", "Bravo Sec", "US0000000019", "Bond", "2026-03-05", "B", "250,000", "101", null, null, "252,500.00", "2026-03-09"),
        // An ambiguous key: two identical trades against one report.
        new("OMS-AMB-1", "Bravo Sec", "US0000000006", "Equity", "2026-03-02", "B", "2,200", "114", null, null, "250,800.00"),
        new("OMS-AMB-2", "Bravo Sec", "US0000000006", "Equity", "2026-03-02", "B", "2,200", "114", null, null, "250,800.00"),
        // Invalid input: a quantity that is not a number, a row with no key, and a row with no trade date.
        new("OMS-BADQTY", "Alfa Bank", "US0000000007", "Bond", "2026-03-02", "B", "abc", "100", null, null, "1,000.00"),
        new(null, "Alfa Bank", "US0000000007", "Bond", "2026-03-02", "B", "1,000", "100", null, null, "1,000.00"),
        new("OMS-NODATE", "Alfa Bank", "US0000000007", "Bond", null, "B", "1,000", "100", null, null, "1,000.00"),
        // A conflicting duplicate: one key, two quantities.
        new("OMS-CONFLICT", "Charlie Capital", "US0000000013", "Equity", "2026-03-04", "B", "100", "10", null, null, "1,000.00"),
        new("OMS-CONFLICT", "Charlie Capital", "US0000000013", "Equity", "2026-03-04", "B", "200", "10", null, null, "2,000.00"),
        // A collapsed duplicate: the same row twice, matched once.
        new("OMS-TWICE", "Charlie Capital", "US0000000008", "Equity", "2026-03-04", "S", "1,500", "20.5", null, null, "30,750.00"),
        new("OMS-TWICE", "Charlie Capital", "US0000000008", "Equity", "2026-03-04", "S", "1,500", "20.5", null, null, "30,750.00"),
        // The trade a lifecycle chain on the street side ends in.
        new("OMS-LIFE", "Alfa Bank", "US0000000009", "Bond", "2026-03-03", "B", "2,000,000", "105.149", null, null, "2,102,980.00", "2026-03-05", "UTI-LIFE"),
        // A package: two swap legs against one street row. A second package has one leg only.
        new("OMS-LEG-PAY", "Bravo Sec", "XS0000000010", "Swap", "2026-03-02", "P", "13,000,000", "4.268", null, null, "278,961.22", null, "UTI-SWAP", "PKG-1", "PAY"),
        new("OMS-LEG-REC", "Bravo Sec", "XS0000000010", "Swap", "2026-03-02", "R", "13,000,000", "3.9645", null, null, "259,124.12", null, "UTI-SWAP", "PKG-1", "REC"),
        new("OMS-HALF-PAY", "Bravo Sec", "XS0000000015", "Swap", "2026-03-02", "P", "5,000,000", "4.1", null, null, "100,000.00", null, null, "PKG-2", "PAY"),
        // Many to one: three allocations that sum to one street block.
        new("OMS-ALLOC-1", "Charlie Capital", "US0000000011", "Bond", "2026-03-04", "B", "2,200", "114", null, null, "2,508.00", "2026-03-06"),
        new("OMS-ALLOC-2", "Charlie Capital", "US0000000011", "Bond", "2026-03-04", "B", "5,400", "114", null, null, "6,156.00", "2026-03-06"),
        new("OMS-ALLOC-3", "Charlie Capital", "US0000000011", "Bond", "2026-03-04", "B", "1,400", "114", null, null, "1,596.00", "2026-03-06"),
        // An attributed break on the reduced key: an 80% fill, with accrued pro rata and net cash following both.
        new("OMS-FILL", "Alfa Bank", "US0000000012", "Bond", "2026-03-04", "S", "1,000,000", "100", "4,784.72", "50.00", "1,004,734.72", "2026-03-06"),
        // No route takes FX.
        new("OMS-FX", "Alfa Bank", "EURUSD", "FX", "2026-03-02", "B", "1,000,000", "1.0930", null, null, "1,093,000.00"),
        // A null on one side of a compared field: the street row has no settle date.
        new("OMS-NOSETTLE", "Bravo Sec", "US0000000014", "Bond", "2026-03-02", "B", "400,000", "99.5", null, null, "398,000.00", "2026-03-04", "UTI-NULL"),
        // A block over the limit of four items: the search is not run.
        new("OMS-BIG-1", "Alfa Bank", "US0000000017", "Equity", "2026-03-05", "B", "100", "50", null, null, "5,000.00"),
        new("OMS-BIG-2", "Alfa Bank", "US0000000017", "Equity", "2026-03-05", "B", "100", "50", null, null, "5,000.00"),
        new("OMS-BIG-3", "Alfa Bank", "US0000000017", "Equity", "2026-03-05", "B", "100", "50", null, null, "5,000.00"),
        new("OMS-BIG-4", "Alfa Bank", "US0000000017", "Equity", "2026-03-05", "B", "100", "50", null, null, "5,000.00"),
        new("OMS-BIG-5", "Alfa Bank", "US0000000017", "Equity", "2026-03-05", "B", "100", "50", null, null, "5,000.00"),
    ];

    public static readonly Street[] Right =
    [
        new("ST-EXACT", Alfa, "US0000000001", "BD", "2026-03-02", "S", "DEALER", "3.000.000,00", "96-02+", "32NDS", "12.500,00", "150,00", "2.894.993,75", "2026-03-04", "UTI-EXACT"),
        new("ST-WITHIN", Brav, "US0000000002", "BD", "02/03/2026", "B", "DEALER", "2.000.000", "101,5", null, "8.332,39", "100,00", "2.038.232,39", "05/03/2026"),
        new("ST-BREAK", Alfa, "US0000000003", "BD", "2026-03-03", "B", "CLIENT", "1.000.000", "99,966", null, "5.000,00", "50,00", "1.004.710,00", "2026-03-05", "UTI-BREAK"),
        // Right only.
        new("ST-ALLEGED", Char, "US0000000005", "BD", "03/04/2026", "B", "CLIENT", "750.000", "100", null, null, null, "750.000,00", "03/06/2026"),
        new("ST-AMB", Brav, "US0000000006", "EQ", "02/03/2026", "B", "CLIENT", "2.200", "114", null, null, null, "250.800,00"),
        // Invalid input: a price that is not in 32nds, and a counterparty the reference data does not know.
        new("ST-BADPX", Alfa, "US0000000007", "BD", "2026-03-02", "B", "CLIENT", "1.000", "98-1X", "32NDS", null, null, "1.000,00"),
        new("ST-NOPARTY", "5493000000000000ZULU", "US0000000007", "BD", "2026-03-02", "B", "CLIENT", "1.000", "100", null, null, null, "1.000,00"),
        new("ST-TWICE", Char, "US0000000008", "EQ", "03/04/2026", "B", "DEALER", "1.500", "20,5", null, null, null, "30.750,00"),
        // A lifecycle chain: a new report, its cancel, and the correction that replaces it.
        new("ST-LIFE-NEW", Alfa, "US0000000009", "BD", "2026-03-03", "B", "CLIENT", "2.000.000", "102,604", null, null, null, "2.052.080,00", "2026-03-05", "UTI-LIFE", "NEW"),
        new("ST-LIFE-CANC", Alfa, "US0000000009", "BD", "2026-03-03", "B", "CLIENT", "2.000.000", "102,604", null, null, null, "2.052.080,00", "2026-03-05", "UTI-LIFE", "CANC", "ST-LIFE-NEW"),
        new("ST-LIFE-CORR", Alfa, "US0000000009", "BD", "2026-03-03", "B", "CLIENT", "2.000.000", "105,149", null, null, null, "2.102.980,00", "2026-03-05", "UTI-LIFE", "CORR", "ST-LIFE-CANC"),
        // A chain that is cancelled and not replaced, and a correction whose target does not exist.
        new("ST-GONE-NEW", Brav, "US0000000016", "BD", "02/03/2026", "B", "CLIENT", "300.000", "100", null, null, null, "300.000,00", null, null, "NEW"),
        new("ST-GONE-CANC", Brav, "US0000000016", "BD", "02/03/2026", "B", "CLIENT", "300.000", "100", null, null, null, "300.000,00", null, null, "CANC", "ST-GONE-NEW"),
        new("ST-LOST-CORR", Brav, "US0000000018", "BD", "02/03/2026", "B", "CLIENT", "300.000", "100", null, null, null, "300.000,00", null, null, "CORR", "ST-NOWHERE"),
        // The street reports the swap as one row, with a blank direction and a signed notional.
        new("ST-SWAP", Brav, "XS0000000010", "SW", "02/03/2026", null, "CLIENT", "-13.000.000", "4,268", null, null, null, "19.837,10", null, "UTI-SWAP"),
        new("ST-BLOCK", Char, "US0000000011", "BD", "03/04/2026", "B", "CLIENT", "9.000", "114", null, null, null, "10.260,00", "03/06/2026"),
        new("ST-FILL", Alfa, "US0000000012", "BD", "2026-03-04", "B", "DEALER", "800.000", "100", null, "3.827,78", "50,00", "803.777,78", "2026-03-06"),
        new("ST-FX", Alfa, "EURUSD", "FX", "2026-03-02", "B", "CLIENT", "1.000.000", "1,0930", null, null, null, "1.093.000,00"),
        new("ST-NOSETTLE", Brav, "US0000000014", "BD", "02/03/2026", "B", "CLIENT", "400.000", "99,5", null, null, null, "398.000,00", null, "UTI-NULL"),
        new("ST-BIG", Alfa, "US0000000017", "EQ", "2026-03-05", "B", "CLIENT", "500", "50", null, null, null, "25.000,00"),
    ];

    private static readonly (string Column, Func<Internal, string?> Read)[] LeftColumns =
    [
        ("Trade_Id", row => row.Id), ("Cpty", row => row.Cpty), ("Isin", row => row.Isin), ("Asset", row => row.Asset), ("Trade_Dt", row => row.TradeDate),
        ("Dir", row => row.Dir), ("Qty", row => row.Qty), ("Px", row => row.Px), ("Acc", row => row.Accrued), ("Fee", row => row.Fee), ("Cash", row => row.Cash),
        ("Settle_Dt", row => row.Settle), ("Uti_Ref", row => row.Uti), ("Parent", row => row.Parent), ("Leg", row => row.Leg),
    ];

    private static readonly (string Column, Func<Street, string?> Read)[] RightColumns =
    [
        ("Report_Id", row => row.Id), ("Lei", row => row.Lei), ("Isin", row => row.Isin), ("Product", row => row.Product), ("Trade_Dt", row => row.TradeDate),
        ("Dir", row => row.Dir), ("View", row => row.View), ("Qty", row => row.Qty), ("Px", row => row.Px), ("Conv", row => row.Convention), ("Acc", row => row.Accrued),
        ("Fee", row => row.Fee), ("Cash", row => row.Cash), ("Settle_Dt", row => row.Settle), ("Uti_Ref", row => row.Uti), ("Event", row => row.Event), ("Prior", row => row.Prior),
    ];

    public static GoldenScenario Scenario { get; } = new(
        "trades",
        Profile,
        Reference,
        new GoldenSide([.. LeftColumns.Select(column => column.Column)], Left.Length, (order, run) => run(LeftSource(order))),
        new GoldenSide([.. RightColumns.Select(column => column.Column)], Right.Length, (order, run) => run(RightSource(order))),
        [
            new GoldenView("by counterparty and asset class", new SideBySideSpec(["Counterparty"], ["AssetClass"], [new MeasureSpec("Quantity"), new MeasureSpec("NetCash")], null)),
            new GoldenView("bonds only, by side", new SideBySideSpec(["Side"], [], [new MeasureSpec("NetCash")], new FilterSpec("AssetClass", "Bond"))),
        ]);

    public static RecordSource LeftSource(IReadOnlyList<int>? order = null) =>
        RecordSources.FromObjects("left", Ordered(Left, order), Map(LeftColumns));

    public static RecordSource RightSource(IReadOnlyList<int>? order = null) =>
        RecordSources.FromObjects("right", Ordered(Right, order), Map(RightColumns));

    public static DataTable LeftTable() => Table(Left, LeftColumns);

    public static DataTable RightTable() => Table(Right, RightColumns);

    private static T[] Ordered<T>(T[] rows, IReadOnlyList<int>? order) =>
        order is null ? rows : [.. order.Select(position => rows[position])];

    private static SourceMap<T> Map<T>((string Column, Func<T, string?> Read)[] columns) =>
        columns.Aggregate(new SourceMap<T>(), (map, column) => map.Text(column.Column, column.Read));

    private static DataTable Table<T>(T[] rows, (string Column, Func<T, string?> Read)[] columns)
    {
        var table = new DataTable { Locale = CultureInfo.InvariantCulture };
#pragma warning disable IDISP004 // The table owns the columns it adds.
        foreach (var (column, _) in columns)
            table.Columns.Add(column, typeof(string));
#pragma warning restore IDISP004
        foreach (var row in rows)
            table.Rows.Add([.. columns.Select(column => (object?)column.Read(row) ?? DBNull.Value)]);
        return table;
    }

    internal sealed record Internal(
        string? Id, string? Cpty, string? Isin, string? Asset, string? TradeDate, string? Dir, string? Qty, string? Px,
        string? Accrued = null, string? Fee = null, string? Cash = null, string? Settle = null, string? Uti = null, string? Parent = null, string? Leg = null);

    internal sealed record Street(
        string? Id, string? Lei, string? Isin, string? Product, string? TradeDate, string? Dir, string? View, string? Qty, string? Px,
        string? Convention = null, string? Accrued = null, string? Fee = null, string? Cash = null, string? Settle = null, string? Uti = null, string? Event = null, string? Prior = null);
}
