using System.Data;
using System.Globalization;
using RecDiffy.Results;

namespace RecDiffy.Tests;

// The second fixed pair of inputs, for what the trades inputs do not reach: one route for every row, the shared
// reference pass with its default key and the TradeRef veto, a group pass with the many rows on the right, a package on
// the right with summed amounts, a lifecycle on the left where a correction points at the new event, tolerances with two
// bounds and with a condition, a field that is not compared, midpoint away from zero, and typed cells. The left side
// is read from objects with decimal and date members. The right side is read from a data reader.
internal static class GoldenStructures
{
    public const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "golden-structures",
          "version": "1.0.0",
          "rounding": { "midpoint": "AwayFromZero" },
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "TradeRef", "type": "Identifier", "role": "Identity" },
            { "name": "Instrument", "type": "Identifier", "role": "Key" },
            { "name": "AssetClass", "type": "Symbol", "role": "Key" },
            { "name": "TradeDate", "type": "Date", "role": "Key" },
            { "name": "Side", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Price", "type": "Decimal", "scale": 4, "role": "Economic" },
            { "name": "Fees", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "NetCash", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Accrued", "type": "Decimal", "scale": 2, "role": "Economic" },
            { "name": "Desk", "type": "Symbol", "role": "Economic" },
            { "name": "EventAction", "type": "Symbol", "role": "Lifecycle" },
            { "name": "EventTarget", "type": "Text", "role": "Lifecycle" },
            { "name": "PackageRef", "type": "Text", "role": "Package" },
            { "name": "LegRole", "type": "Symbol", "role": "Package" }
          ],
          "normalization": {
            "left": {
              "identify": [
                { "rule": "symbol.map", "from": "Cpty", "to": "Counterparty", "map": { "ACME Ltd": "ACME", "Birch & Co": "BIRCH", "Cobalt": "COBALT" }, "passthrough": "false" },
                { "rule": "identifier.map", "from": "Ref", "to": "TradeRef", "scheme": "shared", "when": { "field": "RefKind", "equals": "S" } },
                { "rule": "identifier.map", "from": "Ref", "to": "TradeRef", "scheme": "broker", "when": { "field": "RefKind", "equals": "B" } },
                { "rule": "identifier.map", "from": "Isin", "to": "Instrument", "scheme": "ISIN", "scope": "global" },
                { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" },
                { "rule": "symbol.map", "from": "DeskCode", "to": "Desk" },
                { "rule": "symbol.map", "from": "Event", "to": "EventAction" },
                { "rule": "text.copy", "from": "Prior", "to": "EventTarget" },
                { "rule": "text.copy", "from": "Parent", "to": "PackageRef" },
                { "rule": "symbol.map", "from": "Leg", "to": "LegRole" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "TradeDt", "to": "TradeDate" },
                { "rule": "decimal.parse", "from": "Px", "to": "Price" },
                { "rule": "decimal.parse", "from": "Fee", "to": "Fees" },
                { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" },
                { "rule": "decimal.parse", "from": "Acc", "to": "Accrued" }
              ],
              "derive": [
                { "rule": "side.resolve", "code": "Dir", "quantity": "Qty" },
                { "rule": "quantity.absolute", "from": "Qty", "to": "Quantity" }
              ]
            },
            "right": {
              "identify": [
                { "rule": "symbol.map", "from": "Cpty", "to": "Counterparty", "map": { "ACME Ltd": "ACME", "Birch & Co": "BIRCH", "Cobalt": "COBALT" }, "passthrough": "false" },
                { "rule": "identifier.map", "from": "Ref", "to": "TradeRef", "scheme": "shared", "when": { "field": "RefKind", "equals": "S" } },
                { "rule": "identifier.map", "from": "Ref", "to": "TradeRef", "scheme": "broker", "when": { "field": "RefKind", "equals": "B" } },
                { "rule": "identifier.map", "from": "Isin", "to": "Instrument", "scheme": "ISIN", "scope": "global" },
                { "rule": "symbol.map", "from": "Asset", "to": "AssetClass" },
                { "rule": "symbol.map", "from": "DeskCode", "to": "Desk" },
                { "rule": "symbol.map", "from": "Event", "to": "EventAction" },
                { "rule": "text.copy", "from": "Prior", "to": "EventTarget" },
                { "rule": "text.copy", "from": "Parent", "to": "PackageRef" },
                { "rule": "symbol.map", "from": "Leg", "to": "LegRole" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "TradeDt", "to": "TradeDate" },
                { "rule": "decimal.parse", "from": "Px", "to": "Price" },
                { "rule": "decimal.parse", "from": "Fee", "to": "Fees" },
                { "rule": "decimal.parse", "from": "Cash", "to": "NetCash" },
                { "rule": "decimal.parse", "from": "Acc", "to": "Accrued" }
              ],
              "derive": [
                { "rule": "side.resolve", "code": "Dir", "quantity": "Qty" },
                { "rule": "quantity.absolute", "from": "Qty", "to": "Quantity" }
              ]
            }
          },
          "consolidation": {
            "left": { "lifecycle": { "action": "EventAction", "target": "EventTarget", "scope": "Counterparty", "corrTarget": "NEW" } },
            "right": { "packages": [ {
              "link": "PackageRef", "role": "LegRole", "requiredRoles": ["NEAR", "FAR"],
              "aggregate": { "Quantity": "Same", "Price": "FromRole(FAR)", "Side": "FromRole(NEAR)", "Fees": "Sum", "NetCash": "Sum" }
            } ] }
          },
          "paths": [ {
            "id": "all",
            "route": { "rule": "route.all" },
            "passes": [
              { "id": "P1", "pass": "match.sharedRef" },
              { "id": "P2", "pass": "match.naturalKey", "key": ["Counterparty", "Instrument", "TradeDate", "Side", "Quantity"] },
              { "id": "P3", "pass": "match.groupSum", "many": "right", "block": ["Counterparty", "Instrument", "TradeDate", "Side", "Price"], "conserve": ["Quantity", "NetCash"] },
              { "id": "P4", "pass": "match.reducedKey", "key": ["Counterparty", "Instrument", "TradeDate", "Side"] }
            ],
            "diff": { "fields": {
              "Price": { "tolerance": { "absolute": 0.0005, "relative": 0.0001 } },
              "Fees": { "tolerance": { "absolute": 0.05, "when": { "field": "AssetClass", "equals": "Equity" } } },
              "Accrued": { "applicableWhen": { "field": "AssetClass", "in": ["Bond"] } }
            } }
          } ],
          "limits": { "maxNodesPerTarget": 40 }
        }
        """;

    private const string Acme = "ACME Ltd";
    private const string Birch = "Birch & Co";
    private const string Cobalt = "Cobalt";

    private static readonly DateOnly Day = new(2026, 4, 1);

    public static readonly Deal[] Left =
    [
        // The shared reference pass with its default key. The price differs by 0.0004, inside the absolute bound.
        new("L-ABS", Acme, "EX-1", "S", "I-ABS", "Equity", Day, "B", 100m, 10.0000m, 1.00m, 1001.00m),
        // The price differs by 0.15: outside the absolute bound, inside the relative one.
        new("L-REL", Acme, "EX-2", "S", "I-REL", "Equity", Day, "B", 10m, 2000.0000m, null, 20000.00m),
        // The price differs by 0.02: outside both bounds.
        new("L-PXB", Acme, "EX-3", "S", "I-PXB", "Equity", Day, "B", 10m, 100.0000m, null, 1000.00m),
        // Fees differ by 0.03. The tolerance holds for an equity, and accrued is not compared for an equity.
        new("L-EQ", Birch, "EX-4", "S", "I-EQ", "Equity", Day, "S", 50m, 20m, 5.00m, 995.00m, 1.00m),
        // The same fee difference on a bond: the tolerance's condition fails, and accrued is compared.
        new("L-BD", Birch, "EX-5", "S", "I-BD", "Bond", Day, "S", 50m, 20m, 5.00m, 995.00m, 3.00m),
        // Two different shared references veto the pair on the natural key.
        new("L-VETO", Cobalt, "EX-A", "S", "I-VETO", "Equity", Day, "B", 100m, 5m, null, 500.00m),
        // Two different broker references veto nothing, and are no key for the shared reference pass.
        new("L-BRK", Cobalt, "BR-1", "B", "I-BRK", "Equity", Day, "B", 70m, 5m, null, 350.00m),
        // 2.125 rounds away from zero to 2.13 and meets 2.13 on the natural key.
        new("L-MID", Cobalt, null, null, "I-MID", "Equity", Day, "B", 2.125m, 4m, null, 8.50m),
        // One row against three on the right that sum to it. The three disagree on the desk.
        new("L-BLK", Acme, null, null, "I-BLK", "Equity", Day, "B", 900m, 10m, null, 9000.00m, Desk: "D1"),
        // One row against a block with two equal rows: one of the two would do, so the group is contested.
        new("L-TWIN", Birch, null, null, "I-TWIN", "Equity", Day, "S", 500m, 20m, null, 10000.00m),
        // One row against seven: the search runs out of its 40 nodes.
        new("L-BUD", Cobalt, null, null, "I-BUD", "Equity", Day, "S", 401m, 30m, null, 12030.00m),
        // Against a two-leg package on the right whose fees and cash are summed.
        new("L-FX", Acme, "EX-6", "S", "I-FX", "FX", Day, "B", 1000m, 1.2500m, 3.00m, 250.00m),
        // Against a package whose summed cash is past the decimal range.
        new("L-OVER", Birch, "EX-7", "S", "I-OVER", "FX", Day, "B", 1m, 1m, null, 1.00m),
        // A lifecycle where the correction points at the new event.
        new("L-LN", Acme, "EX-8", "S", "I-LIFE", "Equity", Day, "B", 40m, 9.0000m, null, 360.00m, Event: "NEW"),
        new("L-LC", Acme, "EX-8", "S", "I-LIFE", "Equity", Day, "B", 40m, 9.5000m, null, 380.00m, Event: "CORR", Prior: "L-LN"),
        // Two corrections that point at each other.
        new("L-CY1", Cobalt, null, null, "I-CY", "Equity", Day, "B", 1m, 1m, null, 1.00m, Event: "CORR", Prior: "L-CY2"),
        new("L-CY2", Cobalt, null, null, "I-CY", "Equity", Day, "B", 1m, 1m, null, 1.00m, Event: "CORR", Prior: "L-CY1"),
        // Two corrections of one event: two survivors.
        new("L-K0", Cobalt, null, null, "I-K", "Equity", Day, "B", 1m, 1m, null, 1.00m, Event: "NEW"),
        new("L-K1", Cobalt, null, null, "I-K", "Equity", Day, "B", 2m, 1m, null, 2.00m, Event: "CORR", Prior: "L-K0"),
        new("L-K2", Cobalt, null, null, "I-K", "Equity", Day, "B", 3m, 1m, null, 3.00m, Event: "CORR", Prior: "L-K0"),
        // A chain whose first event has no direction and no quantity.
        new("L-Q0", Birch, null, null, "I-Q", "Equity", Day, null, 0m, 1m, null, 0.00m, Event: "NEW"),
        new("L-Q1", Birch, null, null, "I-Q", "Equity", Day, "B", 5m, 1m, null, 5.00m, Event: "CORR", Prior: "L-Q0"),
    ];

    public static readonly Deal[] Right =
    [
        new("R-ABS", Acme, "EX-1", "S", "I-ABS", "Equity", Day, "B", null, 10.0004m, 1.00m, 1001.00m, WholeQty: 100),
        new("R-REL", Acme, "EX-2", "S", "I-REL", "Equity", Day, "B", 10m, 2000.1500m, null, 20000.00m),
        new("R-PXB", Acme, "EX-3", "S", "I-PXB", "Equity", Day, "B", 10m, 100.0200m, null, 1000.00m),
        new("R-EQ", Birch, "EX-4", "S", "I-EQ", "Equity", Day, "S", 50m, 20m, 5.03m, 995.00m, 2.00m),
        new("R-BD", Birch, "EX-5", "S", "I-BD", "Bond", Day, "S", 50m, 20m, 5.03m, 995.00m, 3.00m),
        new("R-VETO", Cobalt, "EX-B", "S", "I-VETO", "Equity", Day, "B", 100m, 5m, null, 500.00m),
        new("R-BRK", Cobalt, "BR-2", "B", "I-BRK", "Equity", Day, "B", 70m, 5m, null, 350.00m),
        new("R-MID", Cobalt, null, null, "I-MID", "Equity", Day, "B", 2.13m, 4m, null, 8.50m),
        new("R-A1", Acme, null, null, "I-BLK", "Equity", Day, "B", 300m, 10m, null, 3000.00m, Desk: "D1"),
        new("R-A2", Acme, null, null, "I-BLK", "Equity", Day, "B", 400m, 10m, null, 4000.00m, Desk: "D2"),
        new("R-A3", Acme, null, null, "I-BLK", "Equity", Day, "B", 200m, 10m, null, 2000.00m, Desk: "D1"),
        new("R-T1", Birch, null, null, "I-TWIN", "Equity", Day, "S", 200m, 20m, null, 4000.00m),
        new("R-T2", Birch, null, null, "I-TWIN", "Equity", Day, "S", 300m, 20m, null, 6000.00m),
        new("R-T3", Birch, null, null, "I-TWIN", "Equity", Day, "S", 300m, 20m, null, 6000.00m),
        new("R-U1", Cobalt, null, null, "I-BUD", "Equity", Day, "S", 150m, 30m, null, 4500.00m),
        new("R-U2", Cobalt, null, null, "I-BUD", "Equity", Day, "S", 140m, 30m, null, 4200.00m),
        new("R-U3", Cobalt, null, null, "I-BUD", "Equity", Day, "S", 130m, 30m, null, 3900.00m),
        new("R-U4", Cobalt, null, null, "I-BUD", "Equity", Day, "S", 120m, 30m, null, 3600.00m),
        new("R-U5", Cobalt, null, null, "I-BUD", "Equity", Day, "S", 110m, 30m, null, 3300.00m),
        new("R-U6", Cobalt, null, null, "I-BUD", "Equity", Day, "S", 105m, 30m, null, 3150.00m),
        new("R-U7", Cobalt, null, null, "I-BUD", "Equity", Day, "S", 95m, 30m, null, 2850.00m),
        // The package: fees 1.00 + 2.00 and cash 100.00 + 150.00 are summed, and the price is the far leg's. The legs disagree on the desk.
        new("R-NEAR", Acme, "EX-6", "S", "I-FX", "FX", Day, "B", 1000m, 1.2000m, 1.00m, 100.00m, Desk: "D1", Parent: "PK-1", Leg: "NEAR"),
        new("R-FAR", Acme, "EX-6", "S", "I-FX", "FX", Day, "B", 1000m, 1.2500m, 2.00m, 150.00m, Desk: "D2", Parent: "PK-1", Leg: "FAR"),
        // The cash of the two legs sums past the decimal range.
        new("R-ON", Birch, "EX-7", "S", "I-OVER", "FX", Day, "B", 1m, 1m, null, 60000000000000000000000000000m, Parent: "PK-2", Leg: "NEAR"),
        new("R-OF", Birch, "EX-7", "S", "I-OVER", "FX", Day, "B", 1m, 1m, null, 60000000000000000000000000000m, Parent: "PK-2", Leg: "FAR"),
        // The legs disagree on a quantity that must be the same.
        new("R-BN", Cobalt, null, null, "I-BAD", "FX", Day, "B", 100m, 1m, null, 100.00m, Parent: "PK-3", Leg: "NEAR"),
        new("R-BF", Cobalt, null, null, "I-BAD", "FX", Day, "B", 200m, 1m, null, 200.00m, Parent: "PK-3", Leg: "FAR"),
        new("R-LIFE", Acme, "EX-8", "S", "I-LIFE", "Equity", Day, "B", 40m, 9.5000m, null, 380.00m),
        // Invalid input: a buy with a negative quantity, a counterparty the map does not know, and a price held as a double.
        new("R-SIGN", Cobalt, null, null, "I-SIGN", "Equity", Day, "B", -5m, 1m, null, 5.00m),
        new("R-WHO", "Nobody Inc", null, null, "I-WHO", "Equity", Day, "B", 5m, 1m, null, 5.00m),
        new("R-FLOAT", Cobalt, null, null, "I-FLOAT", "Equity", Day, "B", 5m, null, null, 62.50m, FloatPx: 12.5),
    ];

    private static readonly string[] Columns = ["Id", "Cpty", "Ref", "RefKind", "Isin", "Asset", "TradeDt", "Dir", "Qty", "Px", "Fee", "Cash", "Acc", "DeskCode", "Event", "Prior", "Parent", "Leg"];

    // Decimal and date members go in as decimals and dates, through the typed binders.
    private static readonly SourceMap<Deal> Map = new SourceMap<Deal>()
        .Text("Id", deal => deal.Id).Text("Cpty", deal => deal.Cpty).Text("Ref", deal => deal.Ref).Text("RefKind", deal => deal.RefKind)
        .Text("Isin", deal => deal.Isin).Text("Asset", deal => deal.Asset).Date("TradeDt", deal => deal.Date).Text("Dir", deal => deal.Dir)
        .Decimal("Qty", deal => deal.Qty).Decimal("Px", deal => deal.Px).Decimal("Fee", deal => deal.Fee).Decimal("Cash", deal => deal.Cash).Decimal("Acc", deal => deal.Acc)
        .Text("DeskCode", deal => deal.Desk).Text("Event", deal => deal.Event).Text("Prior", deal => deal.Prior).Text("Parent", deal => deal.Parent).Text("Leg", deal => deal.Leg);

    public static GoldenScenario Scenario { get; } = new(
        "structures",
        Profile,
        ReferenceDataSet.Empty,
        new GoldenSide(Columns, Left.Length, (order, run) => run(RecordSources.FromObjects("left", Ordered(Left, order), Map))),
        new GoldenSide(Columns, Right.Length, (order, run) =>
        {
            using var table = Table(Ordered(Right, order));
            return run(RecordSources.FromDataReader("right", table.CreateDataReader, new TabularOptions()));
        }),
        [
            new GoldenView("by counterparty", new SideBySideSpec(["Counterparty"], [], [new MeasureSpec("Quantity"), new MeasureSpec("NetCash")], null)),
            new GoldenView("equities only, by counterparty and side", new SideBySideSpec(["Counterparty"], ["Side"], [new MeasureSpec("Quantity")], new FilterSpec("AssetClass", "Equity"))),
        ]);

    private static Deal[] Ordered(Deal[] rows, IReadOnlyList<int>? order) =>
        order is null ? rows : [.. order.Select(position => rows[position])];

    // The reader hands over what a database would: a DateTime, a decimal, an int where the quantity is whole, and a double.
    private static DataTable Table(Deal[] rows)
    {
        var table = new DataTable { Locale = CultureInfo.InvariantCulture };
#pragma warning disable IDISP004 // The table owns the columns it adds.
        foreach (var column in Columns)
            table.Columns.Add(column, typeof(object));
#pragma warning restore IDISP004
        foreach (var deal in rows)
        {
            table.Rows.Add(
            [
                .. new object?[]
                {
                    deal.Id, deal.Cpty, deal.Ref, deal.RefKind, deal.Isin, deal.Asset, deal.Date?.ToDateTime(TimeOnly.MinValue), deal.Dir,
                    deal.WholeQty ?? (object?)deal.Qty, deal.FloatPx ?? (object?)deal.Px, deal.Fee, deal.Cash, deal.Acc, deal.Desk, deal.Event, deal.Prior, deal.Parent, deal.Leg,
                }.Select(cell => cell ?? DBNull.Value),
            ]);
        }

        return table;
    }

    internal sealed record Deal(
        string? Id, string? Cpty, string? Ref, string? RefKind, string? Isin, string? Asset, DateOnly? Date, string? Dir, decimal? Qty, decimal? Px,
        decimal? Fee = null, decimal? Cash = null, decimal? Acc = null, string? Desk = null, string? Event = null, string? Prior = null, string? Parent = null, string? Leg = null,
        int? WholeQty = null, double? FloatPx = null);
}
