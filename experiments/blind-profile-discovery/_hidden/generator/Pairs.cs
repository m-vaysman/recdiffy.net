using System.Globalization;
using System.Text.Json.Nodes;
using RecDiffy;
using RecDiffy.Results;

namespace BlindDiscovery.Generator;

internal sealed record BuiltPair(
    string Id,
    JsonObject Key,
    string[] LeftHeader,
    IReadOnlyList<string[]> LeftRows,
    string[] RightHeader,
    IReadOnlyList<string[]> RightRows,
    Action<RunSummary> AssertRun);

internal static class Pairs
{
    public static IReadOnlyList<BuiltPair> All() =>
    [
        Composite(),
        Clean(),
        KeyShape(),
        Rounding(),
        Renamed(),
        SignFlip(),
        UnitScale(),
        Fills(),
    ];

    private static BuiltPair Composite()
    {
        string[] header = ["Account", "Ticker", "AsOf", "Quantity", "MarketValue"];
        var rows = new List<string[]>();
        for (var date = 0; date < 4; date++)
        {
            for (var ticker = 0; ticker < 10; ticker++)
            {
                for (var account = 0; account < 12; account++)
                {
                    var quantity = ((account + ticker) % 3) switch { 0 => "100", 1 => "250", _ => "400" };
                    var market = ((account + date) % 3) switch { 0 => "1000.00", 1 => "2500.50", _ => "800.25" };
                    rows.Add([Data.Accounts[account], Data.Tickers[ticker], Data.Day(date * 10), quantity, market]);
                }
            }
        }

        Check.Composite("p01", rows, 0, 1, 2);
        Check.Same("p01", rows, rows);
        return Build(
            "p01",
            "Positions that need a composite key",
            "Same columns and values. No single column, and no pair of columns, identifies a row.",
            "One row to one row on Account + Ticker + AsOf. Quantity and MarketValue repeat.",
            expressible: true,
            gap: null,
            notes: "There is no row-id column. A natural key on any proper subset is ambiguous. Quantity takes three values and MarketValue takes three values.",
            confounds: [],
            match: Match("match.naturalKey", ["Account", "Ticker", "AsOf"]),
            columns:
            [
                Column("Account", "Symbol", "Key", 0, "Account", "Account", "none"),
                Column("Ticker", "Symbol", "Key", 0, "Ticker", "Ticker", "none"),
                Column("AsOf", "Date", "Key", 0, "AsOf", "AsOf", "none"),
                Column("Quantity", "Decimal", "Economic", 0, "Quantity", "Quantity", "none"),
                Column("MarketValue", "Decimal", "Economic", 2, "MarketValue", "MarketValue", "none"),
            ],
            transforms: [],
            rowKeyLeft: null,
            rowKeyRight: null,
            rounding: "ToEven",
            expectClean: true,
            profile: """
            {
              "formatVersion": 1,
              "profileId": "p01",
              "version": "1",
              "culture": "en-US",
              "rounding": { "midpoint": "ToEven" },
              "fields": [
                { "name": "Account", "type": "Symbol", "role": "Key" },
                { "name": "Ticker", "type": "Symbol", "role": "Key" },
                { "name": "AsOf", "type": "Date", "role": "Key" },
                { "name": "Quantity", "type": "Decimal", "role": "Economic", "scale": 0 },
                { "name": "MarketValue", "type": "Decimal", "role": "Economic", "scale": 2 }
              ],
              "normalization": {
                "left": {
                  "identify": [
                    { "rule": "symbol.map", "from": "Account", "to": "Account" },
                    { "rule": "symbol.map", "from": "Ticker", "to": "Ticker" }
                  ],
                  "parse": [
                    { "rule": "date.parse", "from": "AsOf", "to": "AsOf", "formats": ["yyyy-MM-dd"] },
                    { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
                    { "rule": "decimal.parse", "from": "MarketValue", "to": "MarketValue" }
                  ]
                },
                "right": {
                  "identify": [
                    { "rule": "symbol.map", "from": "Account", "to": "Account" },
                    { "rule": "symbol.map", "from": "Ticker", "to": "Ticker" }
                  ],
                  "parse": [
                    { "rule": "date.parse", "from": "AsOf", "to": "AsOf", "formats": ["yyyy-MM-dd"] },
                    { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
                    { "rule": "decimal.parse", "from": "MarketValue", "to": "MarketValue" }
                  ]
                }
              },
              "paths": [
                {
                  "id": "positions",
                  "route": { "rule": "route.all" },
                  "passes": [ { "id": "holding", "pass": "match.naturalKey", "key": ["Account", "Ticker", "AsOf"] } ]
                }
              ]
            }
            """,
            leftHeader: header,
            left: rows,
            rightHeader: header,
            right: rows,
            assert: summary => Expect.Clean(summary, 480, "p01"));
    }

    private static BuiltPair Clean()
    {
        const int count = 800;
        string[] header = ["EntryId", "Account", "ValueDate", "Currency", "Amount", "Balance"];
        var rows = new string[count][];
        for (var i = 0; i < count; i++)
        {
            var finger = i % 40;
            var amount = (finger % 2 == 0 ? 1m : -1m) * (25m + (finger % 8) * 10.5m);
            var balance = 1000m + (finger % 6) * 15.25m;
            rows[i] =
            [
                Data.Id("E-", i + 1, 6),
                Data.Accounts[finger % 12],
                Data.Day(finger % 10),
                Data.Currencies[finger % 5],
                Data.Fixed(amount, 2),
                Data.Fixed(balance, 2),
            ];
        }

        Check.OnlyColumn("p02", rows, 0);
        Check.Same("p02", rows, rows);
        return Build(
            "p02",
            "Clean cash ledger",
            "Same headers, column order, and values. The right file is not in the same row order.",
            "One row to one row on EntryId. Every other column repeats.",
            expressible: true,
            gap: null,
            notes: "Identity mapping. Amount and Balance are exact at scale 2.",
            confounds: [],
            match: Match("match.naturalKey", ["EntryId"]),
            columns:
            [
                Column("EntryId", "Identifier", "Identity", 0, "EntryId", "EntryId", "none"),
                Column("Account", "Symbol", "Key", 0, "Account", "Account", "none"),
                Column("ValueDate", "Date", "Key", 0, "ValueDate", "ValueDate", "none"),
                Column("Currency", "Symbol", "Key", 0, "Currency", "Currency", "none"),
                Column("Amount", "Decimal", "Economic", 2, "Amount", "Amount", "none"),
                Column("Balance", "Decimal", "Economic", 2, "Balance", "Balance", "none"),
            ],
            transforms: [],
            rowKeyLeft: "EntryId",
            rowKeyRight: "EntryId",
            rounding: "ToEven",
            expectClean: true,
            profile: """
            {
              "formatVersion": 1,
              "profileId": "p02",
              "version": "1",
              "culture": "en-US",
              "rounding": { "midpoint": "ToEven" },
              "sources": { "left": { "rowKey": "EntryId" }, "right": { "rowKey": "EntryId" } },
              "fields": [
                { "name": "EntryId", "type": "Identifier", "role": "Identity" },
                { "name": "Account", "type": "Symbol", "role": "Key" },
                { "name": "ValueDate", "type": "Date", "role": "Key" },
                { "name": "Currency", "type": "Symbol", "role": "Key" },
                { "name": "Amount", "type": "Decimal", "role": "Economic", "scale": 2 },
                { "name": "Balance", "type": "Decimal", "role": "Economic", "scale": 2 }
              ],
              "normalization": {
                "left": {
                  "identify": [
                    { "rule": "identifier.map", "from": "EntryId", "to": "EntryId", "scheme": "ENTRY" },
                    { "rule": "symbol.map", "from": "Account", "to": "Account" },
                    { "rule": "symbol.map", "from": "Currency", "to": "Currency" }
                  ],
                  "parse": [
                    { "rule": "date.parse", "from": "ValueDate", "to": "ValueDate", "formats": ["yyyy-MM-dd"] },
                    { "rule": "decimal.parse", "from": "Amount", "to": "Amount" },
                    { "rule": "decimal.parse", "from": "Balance", "to": "Balance" }
                  ]
                },
                "right": {
                  "identify": [
                    { "rule": "identifier.map", "from": "EntryId", "to": "EntryId", "scheme": "ENTRY" },
                    { "rule": "symbol.map", "from": "Account", "to": "Account" },
                    { "rule": "symbol.map", "from": "Currency", "to": "Currency" }
                  ],
                  "parse": [
                    { "rule": "date.parse", "from": "ValueDate", "to": "ValueDate", "formats": ["yyyy-MM-dd"] },
                    { "rule": "decimal.parse", "from": "Amount", "to": "Amount" },
                    { "rule": "decimal.parse", "from": "Balance", "to": "Balance" }
                  ]
                }
              },
              "paths": [
                {
                  "id": "cash",
                  "route": { "rule": "route.all" },
                  "passes": [ { "id": "entry", "pass": "match.naturalKey", "key": ["EntryId"] } ]
                }
              ]
            }
            """,
            leftHeader: header,
            left: rows,
            rightHeader: header,
            right: rows,
            assert: summary => Expect.Clean(summary, count, "p02"));
    }

    private static BuiltPair KeyShape()
    {
        const int count = 640;
        string[] header = ["TradeId", "Book", "Ticker", "TradeDate", "Side", "Quantity", "Price"];
        var left = new string[count][];
        var right = new string[count][];
        for (var i = 1; i <= count; i++)
        {
            var finger = (i - 1) % 80;
            var shared = new[]
            {
                Data.Accounts[finger % 12],
                Data.Tickers[finger % 15],
                Data.Day(finger % 4),
                finger % 2 == 0 ? "BUY" : "SELL",
                (10 + (finger % 20)).ToString(CultureInfo.InvariantCulture),
                Data.Fixed(15m + (finger % 10) * 0.5m, 2),
            };
            var leftId = "TRD-" + i.ToString("000000", CultureInfo.InvariantCulture);
            var rightId = "trd" + i.ToString(CultureInfo.InvariantCulture);
            if (Canon(leftId) != Canon(rightId) || leftId == rightId)
                throw new InvalidOperationException($"p03 id {i.ToInvariant()} did not canonicalize.");
            left[i - 1] = [leftId, .. shared];
            right[i - 1] = [rightId, .. shared];
        }

        Check.OnlyColumn("p03 left", left, 0);
        Check.OnlyColumn("p03 right", right, 0);
        if (CheckDistinct(left, [1, 2, 3, 4, 5, 6]) == count)
            throw new InvalidOperationException("p03 economic columns are unique.");
        return Build(
            "p03",
            "Trade ids that differ by prefix, padding, and case",
            "The same trades. The id text differs; every other column is identical and repeats.",
            "Trade 42 is TRD-000042 on the left and trd42 on the right.",
            expressible: false,
            gap: "No rule case-folds text, strips a prefix, or strips leading zeros. identifier.map and symbol.map keep the trimmed text. Listing all 640 raw values in a symbol.map would match this finite file and is not the representation rule.",
            notes: "The profile in this key maps the columns and matches TradeId as written, so nothing pairs. The records themselves correspond after the transform.",
            confounds: [],
            match: Match("match.naturalKey", ["TradeId"]),
            columns:
            [
                Column("TradeId", "Identifier", "Identity", 0, "TradeId", "TradeId", "strip prefix TRD- or trd, strip leading zeros, compare the integer"),
                Column("Book", "Symbol", "Key", 0, "Book", "Book", "none"),
                Column("Ticker", "Symbol", "Key", 0, "Ticker", "Ticker", "none"),
                Column("TradeDate", "Date", "Key", 0, "TradeDate", "TradeDate", "none"),
                Column("Side", "Symbol", "Key", 0, "Side", "Side", "none"),
                Column("Quantity", "Decimal", "Economic", 0, "Quantity", "Quantity", "none"),
                Column("Price", "Decimal", "Economic", 2, "Price", "Price", "none"),
            ],
            transforms:
            [
                Transform("TradeId", "both", "canonicalize-key", "Remove a leading TRD- or trd prefix, strip leading zeros, and compare the remaining integer. Left TRD-000042 and right trd42 are both 42."),
            ],
            rowKeyLeft: "TradeId",
            rowKeyRight: "TradeId",
            rounding: "ToEven",
            expectClean: false,
            profile: """
            {
              "formatVersion": 1,
              "profileId": "p03",
              "version": "1",
              "culture": "en-US",
              "rounding": { "midpoint": "ToEven" },
              "sources": { "left": { "rowKey": "TradeId" }, "right": { "rowKey": "TradeId" } },
              "fields": [
                { "name": "TradeId", "type": "Identifier", "role": "Identity" },
                { "name": "Book", "type": "Symbol", "role": "Key" },
                { "name": "Ticker", "type": "Symbol", "role": "Key" },
                { "name": "TradeDate", "type": "Date", "role": "Key" },
                { "name": "Side", "type": "Symbol", "role": "Key" },
                { "name": "Quantity", "type": "Decimal", "role": "Economic", "scale": 0 },
                { "name": "Price", "type": "Decimal", "role": "Economic", "scale": 2 }
              ],
              "normalization": {
                "left": {
                  "identify": [
                    { "rule": "identifier.map", "from": "TradeId", "to": "TradeId", "scheme": "TRADE" },
                    { "rule": "symbol.map", "from": "Book", "to": "Book" },
                    { "rule": "symbol.map", "from": "Ticker", "to": "Ticker" },
                    { "rule": "symbol.map", "from": "Side", "to": "Side" }
                  ],
                  "parse": [
                    { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
                    { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
                    { "rule": "decimal.parse", "from": "Price", "to": "Price" }
                  ]
                },
                "right": {
                  "identify": [
                    { "rule": "identifier.map", "from": "TradeId", "to": "TradeId", "scheme": "TRADE" },
                    { "rule": "symbol.map", "from": "Book", "to": "Book" },
                    { "rule": "symbol.map", "from": "Ticker", "to": "Ticker" },
                    { "rule": "symbol.map", "from": "Side", "to": "Side" }
                  ],
                  "parse": [
                    { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
                    { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
                    { "rule": "decimal.parse", "from": "Price", "to": "Price" }
                  ]
                }
              },
              "paths": [
                {
                  "id": "trades",
                  "route": { "rule": "route.all" },
                  "passes": [ { "id": "trade", "pass": "match.naturalKey", "key": ["TradeId"] } ]
                }
              ]
            }
            """,
            leftHeader: header,
            left: left,
            rightHeader: header,
            right: right,
            assert: summary => Expect.Unmatched(summary, count, "p03"));
    }

    private static BuiltPair Rounding()
    {
        const int count = 700;
        string[] header = ["TradeId", "Ticker", "Quantity", "Price", "NetCash"];
        var left = new string[count][];
        var right = new string[count][];
        var noisy = 0;
        for (var i = 0; i < count; i++)
        {
            var finger = i % 35;
            var price = 8m + (finger % 20) + (finger % 8) * 0.0025m;
            var rounded = decimal.Round(price, 2, MidpointRounding.AwayFromZero);
            var gap = decimal.Abs(price - rounded);
            if (gap > 0.005m)
                throw new InvalidOperationException($"p04 price gap {gap.ToInvariant()} exceeds half a cent.");
            if (price != rounded)
                noisy++;
            var quantity = 10 + (finger % 10);
            var cash = quantity * rounded;
            var id = Data.Id("R-", i + 1, 6);
            var ticker = Data.Tickers[finger % 15];
            var qty = quantity.ToString(CultureInfo.InvariantCulture);
            var net = Data.Fixed(cash, 2);
            left[i] = [id, ticker, qty, Data.Fixed(price, 4), net];
            right[i] = [id, ticker, qty, Data.Fixed(rounded, 2), net];
        }

        if (noisy == 0)
            throw new InvalidOperationException("p04 has no sub-cent prices.");
        Check.OnlyColumn("p04 left", left, 0);
        Check.OnlyColumn("p04 right", right, 0);
        return Build(
            "p04",
            "Prices rounded to cents",
            "Same trades. The right price is the left price rounded to cents, midpoint away from zero. Quantity and NetCash are identical; NetCash uses the rounded price.",
            "One row to one row on TradeId. The largest absolute price difference is 0.005.",
            expressible: true,
            gap: null,
            notes: "Price scale 2 with midpoint AwayFromZero makes the two prices the same number. Quantity is scale 0 and NetCash is scale 2.",
            confounds: ["An absolute tolerance of 0.005 also accepts every price on this file. A wider tolerance accepts a difference this file does not contain."],
            match: Match("match.naturalKey", ["TradeId"]),
            columns:
            [
                Column("TradeId", "Identifier", "Identity", 0, "TradeId", "TradeId", "none"),
                Column("Ticker", "Symbol", "Key", 0, "Ticker", "Ticker", "none"),
                Column("Quantity", "Decimal", "Economic", 0, "Quantity", "Quantity", "none"),
                Column("Price", "Decimal", "Economic", 2, "Price", "Price", "round half away from zero to 2 decimal places"),
                Column("NetCash", "Decimal", "Economic", 2, "NetCash", "NetCash", "none"),
            ],
            transforms:
            [
                Transform("Price", "right", "round", "Right price is the left price rounded to 2 decimal places, midpoint away from zero. Max abs(left - right) is 0.005."),
            ],
            rowKeyLeft: "TradeId",
            rowKeyRight: "TradeId",
            rounding: "AwayFromZero",
            expectClean: true,
            profile: """
            {
              "formatVersion": 1,
              "profileId": "p04",
              "version": "1",
              "culture": "en-US",
              "rounding": { "midpoint": "AwayFromZero" },
              "sources": { "left": { "rowKey": "TradeId" }, "right": { "rowKey": "TradeId" } },
              "fields": [
                { "name": "TradeId", "type": "Identifier", "role": "Identity" },
                { "name": "Ticker", "type": "Symbol", "role": "Key" },
                { "name": "Quantity", "type": "Decimal", "role": "Economic", "scale": 0 },
                { "name": "Price", "type": "Decimal", "role": "Economic", "scale": 2 },
                { "name": "NetCash", "type": "Decimal", "role": "Economic", "scale": 2 }
              ],
              "normalization": {
                "left": {
                  "identify": [
                    { "rule": "identifier.map", "from": "TradeId", "to": "TradeId", "scheme": "TRADE" },
                    { "rule": "symbol.map", "from": "Ticker", "to": "Ticker" }
                  ],
                  "parse": [
                    { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
                    { "rule": "decimal.parse", "from": "Price", "to": "Price" },
                    { "rule": "decimal.parse", "from": "NetCash", "to": "NetCash" }
                  ]
                },
                "right": {
                  "identify": [
                    { "rule": "identifier.map", "from": "TradeId", "to": "TradeId", "scheme": "TRADE" },
                    { "rule": "symbol.map", "from": "Ticker", "to": "Ticker" }
                  ],
                  "parse": [
                    { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
                    { "rule": "decimal.parse", "from": "Price", "to": "Price" },
                    { "rule": "decimal.parse", "from": "NetCash", "to": "NetCash" }
                  ]
                }
              },
              "paths": [
                {
                  "id": "trades",
                  "route": { "rule": "route.all" },
                  "passes": [ { "id": "trade", "pass": "match.naturalKey", "key": ["TradeId"] } ]
                }
              ]
            }
            """,
            leftHeader: header,
            left: left,
            rightHeader: header,
            right: right,
            assert: summary => Expect.Clean(summary, count, "p04"));
    }

    private static BuiltPair Renamed()
    {
        const int count = 1200;
        string[] leftHeader = ["TradeId", "Book", "Ticker", "TradeDate", "Side", "Quantity", "Price", "NetCash"];
        string[] rightHeader = ["cash", "px", "qty", "bs", "td", "sym", "desk", "ref"];
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ref"] = "TradeId",
            ["desk"] = "Book",
            ["sym"] = "Ticker",
            ["td"] = "TradeDate",
            ["bs"] = "Side",
            ["qty"] = "Quantity",
            ["px"] = "Price",
            ["cash"] = "NetCash",
        };
        var left = new string[count][];
        var right = new string[count][];
        for (var i = 0; i < count; i++)
        {
            var finger = i % 60;
            var quantity = 5 + (finger % 15);
            var price = 12m + (finger % 9) * 0.25m;
            var fields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TradeId"] = Data.Id("T-", i + 1, 6),
                ["Book"] = Data.Accounts[finger % 12],
                ["Ticker"] = Data.Tickers[finger % 15],
                ["TradeDate"] = Data.Day(finger % 12),
                ["Side"] = finger % 2 == 0 ? "BUY" : "SELL",
                ["Quantity"] = quantity.ToString(CultureInfo.InvariantCulture),
                ["Price"] = Data.Fixed(price, 2),
                ["NetCash"] = Data.Fixed(quantity * price, 2),
            };
            left[i] = [.. leftHeader.Select(column => fields[column])];
            right[i] = [.. rightHeader.Select(column => fields[map[column]])];
        }

        Check.OnlyColumn("p05 left", left, 0);
        Check.OnlyColumn("p05 right", right, 7);
        return Build(
            "p05",
            "Renamed and reordered trade columns",
            "Same values. The right file uses different header names and a different column order.",
            "ref=TradeId, desk=Book, sym=Ticker, td=TradeDate, bs=Side, qty=Quantity, px=Price, cash=NetCash.",
            expressible: true,
            gap: null,
            notes: "Column names compare ordinally. The values need no transform.",
            confounds: [],
            match: Match("match.naturalKey", ["TradeId"]),
            columns:
            [
                Column("TradeId", "Identifier", "Identity", 0, "TradeId", "ref", "none"),
                Column("Book", "Symbol", "Key", 0, "Book", "desk", "none"),
                Column("Ticker", "Symbol", "Key", 0, "Ticker", "sym", "none"),
                Column("TradeDate", "Date", "Key", 0, "TradeDate", "td", "none"),
                Column("Side", "Symbol", "Key", 0, "Side", "bs", "none"),
                Column("Quantity", "Decimal", "Economic", 0, "Quantity", "qty", "none"),
                Column("Price", "Decimal", "Economic", 2, "Price", "px", "none"),
                Column("NetCash", "Decimal", "Economic", 2, "NetCash", "cash", "none"),
            ],
            transforms: [],
            rowKeyLeft: "TradeId",
            rowKeyRight: "ref",
            rounding: "ToEven",
            expectClean: true,
            profile: """
            {
              "formatVersion": 1,
              "profileId": "p05",
              "version": "1",
              "culture": "en-US",
              "rounding": { "midpoint": "ToEven" },
              "sources": { "left": { "rowKey": "TradeId" }, "right": { "rowKey": "ref" } },
              "fields": [
                { "name": "TradeId", "type": "Identifier", "role": "Identity" },
                { "name": "Book", "type": "Symbol", "role": "Key" },
                { "name": "Ticker", "type": "Symbol", "role": "Key" },
                { "name": "TradeDate", "type": "Date", "role": "Key" },
                { "name": "Side", "type": "Symbol", "role": "Key" },
                { "name": "Quantity", "type": "Decimal", "role": "Economic", "scale": 0 },
                { "name": "Price", "type": "Decimal", "role": "Economic", "scale": 2 },
                { "name": "NetCash", "type": "Decimal", "role": "Economic", "scale": 2 }
              ],
              "normalization": {
                "left": {
                  "identify": [
                    { "rule": "identifier.map", "from": "TradeId", "to": "TradeId", "scheme": "TRADE" },
                    { "rule": "symbol.map", "from": "Book", "to": "Book" },
                    { "rule": "symbol.map", "from": "Ticker", "to": "Ticker" },
                    { "rule": "symbol.map", "from": "Side", "to": "Side" }
                  ],
                  "parse": [
                    { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
                    { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
                    { "rule": "decimal.parse", "from": "Price", "to": "Price" },
                    { "rule": "decimal.parse", "from": "NetCash", "to": "NetCash" }
                  ]
                },
                "right": {
                  "identify": [
                    { "rule": "identifier.map", "from": "ref", "to": "TradeId", "scheme": "TRADE" },
                    { "rule": "symbol.map", "from": "desk", "to": "Book" },
                    { "rule": "symbol.map", "from": "sym", "to": "Ticker" },
                    { "rule": "symbol.map", "from": "bs", "to": "Side" }
                  ],
                  "parse": [
                    { "rule": "date.parse", "from": "td", "to": "TradeDate", "formats": ["yyyy-MM-dd"] },
                    { "rule": "decimal.parse", "from": "qty", "to": "Quantity" },
                    { "rule": "decimal.parse", "from": "px", "to": "Price" },
                    { "rule": "decimal.parse", "from": "cash", "to": "NetCash" }
                  ]
                }
              },
              "paths": [
                {
                  "id": "trades",
                  "route": { "rule": "route.all" },
                  "passes": [ { "id": "trade", "pass": "match.naturalKey", "key": ["TradeId"] } ]
                }
              ]
            }
            """,
            leftHeader: leftHeader,
            left: left,
            rightHeader: rightHeader,
            right: right,
            assert: summary => Expect.Clean(summary, count, "p05"));
    }

    private static BuiltPair SignFlip()
    {
        const int count = 900;
        string[] leftHeader = ["PositionId", "Account", "Ticker", "Quantity", "MarketValue"];
        string[] rightHeader = ["PositionId", "Account", "Ticker", "Quantity", "MarketValue", "BookView"];
        var left = new string[count][];
        var right = new string[count][];
        var flipped = 0;
        var negative = 0;
        for (var i = 0; i < count; i++)
        {
            var finger = i % 45;
            var quantity = (finger % 3 == 0 ? -1m : 1m) * (100 + (finger % 20));
            var market = quantity * (20m + (finger % 10) * 0.5m);
            var counterparty = finger % 5 < 2;
            if (counterparty)
                flipped++;
            if (quantity < 0m)
                negative++;
            var id = Data.Id("P-", i + 1, 6);
            var account = Data.Accounts[finger % 12];
            var ticker = Data.Tickers[finger % 15];
            left[i] = [id, account, ticker, Data.Fixed(quantity, 2), Data.Fixed(market, 2)];
            right[i] =
            [
                id,
                account,
                ticker,
                Data.Fixed(counterparty ? -quantity : quantity, 2),
                Data.Fixed(counterparty ? -market : market, 2),
                counterparty ? "COUNTERPARTY" : "INTERNAL",
            ];
            var back = right[i][5] == "COUNTERPARTY" ? -Check.Dec(right[i][3]) : Check.Dec(right[i][3]);
            if (back != quantity)
                throw new InvalidOperationException($"p06 row {i.ToInvariant()} sign did not invert back.");
        }

        if (flipped == 0 || flipped == count || negative == 0 || negative == count)
            throw new InvalidOperationException("p06 needs both views and both signs.");
        Check.OnlyColumn("p06 left", left, 0);
        Check.OnlyColumn("p06 right", right, 0);
        return Build(
            "p06",
            "Position signs that depend on book view",
            "Same positions. When BookView is COUNTERPARTY the right quantity and market value are the negation of the left. INTERNAL rows already use the left sign. BookView is only on the right.",
            "One row to one row on PositionId after negating COUNTERPARTY amounts.",
            expressible: true,
            gap: null,
            notes: "cash.toPerspective negates Quantity and MarketValue on the right when BookView is COUNTERPARTY. The column is raw text; it is not a canonical field.",
            confounds: ["quantity.absolute on both sides also compares equal on this file, because every right quantity is the left quantity or its exact negation. That drops the sign. The representation is the conditional negation, which keeps the internal sign."],
            match: Match("match.naturalKey", ["PositionId"]),
            columns:
            [
                Column("PositionId", "Identifier", "Identity", 0, "PositionId", "PositionId", "none"),
                Column("Account", "Symbol", "Key", 0, "Account", "Account", "none"),
                Column("Ticker", "Symbol", "Key", 0, "Ticker", "Ticker", "none"),
                Column("Quantity", "Decimal", "Economic", 2, "Quantity", "Quantity", "negate when BookView is COUNTERPARTY"),
                Column("MarketValue", "Decimal", "Economic", 2, "MarketValue", "MarketValue", "negate when BookView is COUNTERPARTY"),
            ],
            transforms:
            [
                Transform("Quantity", "right", "negate-when", "Negate the right quantity when BookView is COUNTERPARTY. Leave INTERNAL rows unchanged."),
                Transform("MarketValue", "right", "negate-when", "Same convention as Quantity."),
            ],
            rowKeyLeft: "PositionId",
            rowKeyRight: "PositionId",
            rounding: "ToEven",
            expectClean: true,
            profile: """
            {
              "formatVersion": 1,
              "profileId": "p06",
              "version": "1",
              "culture": "en-US",
              "rounding": { "midpoint": "ToEven" },
              "sources": { "left": { "rowKey": "PositionId" }, "right": { "rowKey": "PositionId" } },
              "fields": [
                { "name": "PositionId", "type": "Identifier", "role": "Identity" },
                { "name": "Account", "type": "Symbol", "role": "Key" },
                { "name": "Ticker", "type": "Symbol", "role": "Key" },
                { "name": "Quantity", "type": "Decimal", "role": "Economic", "scale": 2 },
                { "name": "MarketValue", "type": "Decimal", "role": "Economic", "scale": 2 }
              ],
              "normalization": {
                "left": {
                  "identify": [
                    { "rule": "identifier.map", "from": "PositionId", "to": "PositionId", "scheme": "POSITION" },
                    { "rule": "symbol.map", "from": "Account", "to": "Account" },
                    { "rule": "symbol.map", "from": "Ticker", "to": "Ticker" }
                  ],
                  "parse": [
                    { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
                    { "rule": "decimal.parse", "from": "MarketValue", "to": "MarketValue" }
                  ]
                },
                "right": {
                  "identify": [
                    { "rule": "identifier.map", "from": "PositionId", "to": "PositionId", "scheme": "POSITION" },
                    { "rule": "symbol.map", "from": "Account", "to": "Account" },
                    { "rule": "symbol.map", "from": "Ticker", "to": "Ticker" }
                  ],
                  "parse": [
                    { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
                    { "rule": "decimal.parse", "from": "MarketValue", "to": "MarketValue" }
                  ],
                  "derive": [
                    { "rule": "cash.toPerspective", "to": "Quantity", "perspective": "BookView", "invertWhen": "COUNTERPARTY" },
                    { "rule": "cash.toPerspective", "to": "MarketValue", "perspective": "BookView", "invertWhen": "COUNTERPARTY" }
                  ]
                }
              },
              "paths": [
                {
                  "id": "positions",
                  "route": { "rule": "route.all" },
                  "passes": [ { "id": "position", "pass": "match.naturalKey", "key": ["PositionId"] } ]
                }
              ]
            }
            """,
            leftHeader: leftHeader,
            left: left,
            rightHeader: rightHeader,
            right: right,
            assert: summary => Expect.Clean(summary, count, "p06"));
    }

    private static BuiltPair UnitScale()
    {
        const int count = 1500;
        string[] header = ["LotId", "Account", "Ticker", "Quantity", "Price", "MarketValue"];
        var left = new string[count][];
        var right = new string[count][];
        for (var i = 0; i < count; i++)
        {
            var finger = i % 50;
            var shares = 100m * (1 + (finger % 40));
            var price = 10m + (finger % 15) * 0.25m;
            var market = shares * price;
            if (shares / 1000m * 1000m != shares || market / 1000m * 1000m != market)
                throw new InvalidOperationException($"p07 row {i.ToInvariant()} is not an exact thousandth.");
            var id = Data.Id("L-", i + 1, 6);
            var account = Data.Accounts[finger % 12];
            var ticker = Data.Tickers[finger % 15];
            var px = Data.Fixed(price, 2);
            left[i] = [id, account, ticker, Data.Plain(shares), px, Data.Plain(market)];
            right[i] = [id, account, ticker, Data.Plain(shares / 1000m), px, Data.Plain(market / 1000m)];
        }

        Check.OnlyColumn("p07 left", left, 0);
        Check.OnlyColumn("p07 right", right, 0);
        return Build(
            "p07",
            "Quantities and market values in thousands",
            "Same lots. Right Quantity and MarketValue are the left values divided by 1000. Price is the same per-share number on both sides.",
            "One row to one row on LotId after multiplying the right quantity and market value by 1000.",
            expressible: false,
            gap: "No rule multiplies or divides a decimal by a constant. A relative tolerance is not this transform: the ratio is exactly 0.001, so the relative gap is 0.999.",
            notes: "The profile in this key maps the columns and compares exactly, so every matched row breaks on Quantity and MarketValue and Price stays exact. The records themselves correspond after multiplying the right figures by 1000.",
            confounds: [],
            match: Match("match.naturalKey", ["LotId"]),
            columns:
            [
                Column("LotId", "Identifier", "Identity", 0, "LotId", "LotId", "none"),
                Column("Account", "Symbol", "Key", 0, "Account", "Account", "none"),
                Column("Ticker", "Symbol", "Key", 0, "Ticker", "Ticker", "none"),
                Column("Quantity", "Decimal", "Economic", 4, "Quantity", "Quantity", "multiply right by 1000"),
                Column("Price", "Decimal", "Economic", 4, "Price", "Price", "none"),
                Column("MarketValue", "Decimal", "Economic", 4, "MarketValue", "MarketValue", "multiply right by 1000"),
            ],
            transforms:
            [
                Transform("Quantity", "right", "multiply", "Right quantity is in thousands of shares. Multiply by 1000 to reach the left share count."),
                Transform("MarketValue", "right", "multiply", "Right market value is in thousands. Same factor as Quantity. Price is not scaled."),
            ],
            rowKeyLeft: "LotId",
            rowKeyRight: "LotId",
            rounding: "ToEven",
            expectClean: false,
            profile: """
            {
              "formatVersion": 1,
              "profileId": "p07",
              "version": "1",
              "culture": "en-US",
              "rounding": { "midpoint": "ToEven" },
              "sources": { "left": { "rowKey": "LotId" }, "right": { "rowKey": "LotId" } },
              "fields": [
                { "name": "LotId", "type": "Identifier", "role": "Identity" },
                { "name": "Account", "type": "Symbol", "role": "Key" },
                { "name": "Ticker", "type": "Symbol", "role": "Key" },
                { "name": "Quantity", "type": "Decimal", "role": "Economic", "scale": 4 },
                { "name": "Price", "type": "Decimal", "role": "Economic", "scale": 4 },
                { "name": "MarketValue", "type": "Decimal", "role": "Economic", "scale": 4 }
              ],
              "normalization": {
                "left": {
                  "identify": [
                    { "rule": "identifier.map", "from": "LotId", "to": "LotId", "scheme": "LOT" },
                    { "rule": "symbol.map", "from": "Account", "to": "Account" },
                    { "rule": "symbol.map", "from": "Ticker", "to": "Ticker" }
                  ],
                  "parse": [
                    { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
                    { "rule": "decimal.parse", "from": "Price", "to": "Price" },
                    { "rule": "decimal.parse", "from": "MarketValue", "to": "MarketValue" }
                  ]
                },
                "right": {
                  "identify": [
                    { "rule": "identifier.map", "from": "LotId", "to": "LotId", "scheme": "LOT" },
                    { "rule": "symbol.map", "from": "Account", "to": "Account" },
                    { "rule": "symbol.map", "from": "Ticker", "to": "Ticker" }
                  ],
                  "parse": [
                    { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
                    { "rule": "decimal.parse", "from": "Price", "to": "Price" },
                    { "rule": "decimal.parse", "from": "MarketValue", "to": "MarketValue" }
                  ]
                }
              },
              "paths": [
                {
                  "id": "lots",
                  "route": { "rule": "route.all" },
                  "passes": [ { "id": "lot", "pass": "match.naturalKey", "key": ["LotId"] } ]
                }
              ]
            }
            """,
            leftHeader: header,
            left: left,
            rightHeader: header,
            right: right,
            assert: summary => Expect.AllBreak(summary, count, "p07"));
    }

    private static BuiltPair Fills()
    {
        const int orders = 300;
        string[] leftHeader = ["FillId", "OrderId", "Ticker", "Side", "Quantity", "Price", "NetCash"];
        string[] rightHeader = ["OrderId", "Ticker", "Side", "Quantity", "Price", "NetCash"];
        var left = new List<string[]>();
        var right = new List<string[]>();
        for (var order = 0; order < orders; order++)
        {
            var fills = 2 + (order % 5);
            var total = 30 + (order % 20);
            var price = 5m + (order % 8) * 0.25m;
            var parts = Split(total, fills);
            var orderId = Data.Id("O-", order + 1, 4);
            var ticker = Data.Tickers[order % 15];
            var side = order % 2 == 0 ? "BUY" : "SELL";
            var priceText = Data.Fixed(price, 2);
            foreach (var (leg, part) in parts.Index())
            {
                left.Add(
                [
                    Data.Id("F-", order + 1, 4) + "-" + (leg + 1).ToString("00", CultureInfo.InvariantCulture),
                    orderId,
                    ticker,
                    side,
                    part.ToString(CultureInfo.InvariantCulture),
                    priceText,
                    Data.Fixed(part * price, 2),
                ]);
            }

            right.Add([orderId, ticker, side, total.ToString(CultureInfo.InvariantCulture), priceText, Data.Fixed(total * price, 2)]);
        }

        if (left.Count != 1200)
            throw new InvalidOperationException($"p08 produced {left.Count.ToInvariant()} fills.");
        foreach (var order in right)
        {
            var fills = left.Where(row => row[1] == order[0]).ToArray();
            if (fills.Length is < 2 or > 6)
                throw new InvalidOperationException($"p08 {order[0]} has {fills.Length.ToInvariant()} fills.");
            var quantity = fills.Sum(row => Check.Dec(row[4]));
            var cash = fills.Sum(row => Check.Dec(row[6]));
            if (quantity != Check.Dec(order[3]) || cash != Check.Dec(order[5]))
                throw new InvalidOperationException($"p08 {order[0]} does not sum.");
            if (fills.Any(row => row[2] != order[1] || row[3] != order[2] || row[5] != order[4]))
                throw new InvalidOperationException($"p08 {order[0]} fills disagree on ticker, side, or price.");
        }

        Check.OnlyColumn("p08 left", left, 0);
        Check.OnlyColumn("p08 right", right, 0);
        return Build(
            "p08",
            "Fills that sum to orders",
            "Each right row is one order. The left rows are its fills: 2 to 6 of them. Fill quantities and net cash add up exactly to the order. Ticker, side, and price are the same on every fill and on the order.",
            "Many fills to one order, blocked by OrderId + Ticker + Side + Price, conserving Quantity and NetCash.",
            expressible: true,
            gap: null,
            notes: "match.groupSum, many = left. Price is a block field, so the engine does not sum it. There is no sum tolerance; the sums are exact. Fill counts are 2 to 6, under the group cap of 16. A block that omits OrderId can exceed the default maxBlockItems of 24. The left row key is FillId, not OrderId: duplicate order ids on the fills are not row-key conflicts.",
            confounds: [],
            match: Match("match.groupSum", [], ["OrderId", "Ticker", "Side", "Price"], ["Quantity", "NetCash"], "left"),
            columns:
            [
                Column("OrderId", "Identifier", "Identity", 0, "OrderId", "OrderId", "none"),
                Column("Ticker", "Symbol", "Key", 0, "Ticker", "Ticker", "none"),
                Column("Side", "Symbol", "Key", 0, "Side", "Side", "none"),
                Column("Quantity", "Decimal", "Economic", 0, "Quantity", "Quantity", "sum fills"),
                Column("Price", "Decimal", "Economic", 2, "Price", "Price", "same on every fill"),
                Column("NetCash", "Decimal", "Economic", 2, "NetCash", "NetCash", "sum fills"),
            ],
            transforms:
            [
                Transform("Quantity", "left", "sum", "Sum fill quantity to the order quantity. Exact, no tolerance."),
                Transform("NetCash", "left", "sum", "Sum fill net cash to the order net cash. Exact, no tolerance."),
            ],
            rowKeyLeft: "FillId",
            rowKeyRight: "OrderId",
            rounding: "ToEven",
            expectClean: true,
            profile: """
            {
              "formatVersion": 1,
              "profileId": "p08",
              "version": "1",
              "culture": "en-US",
              "rounding": { "midpoint": "ToEven" },
              "sources": { "left": { "rowKey": "FillId" }, "right": { "rowKey": "OrderId" } },
              "fields": [
                { "name": "OrderId", "type": "Identifier", "role": "Identity" },
                { "name": "Ticker", "type": "Symbol", "role": "Key" },
                { "name": "Side", "type": "Symbol", "role": "Key" },
                { "name": "Quantity", "type": "Decimal", "role": "Economic", "scale": 0 },
                { "name": "Price", "type": "Decimal", "role": "Economic", "scale": 2 },
                { "name": "NetCash", "type": "Decimal", "role": "Economic", "scale": 2 }
              ],
              "normalization": {
                "left": {
                  "identify": [
                    { "rule": "identifier.map", "from": "OrderId", "to": "OrderId", "scheme": "ORDER" },
                    { "rule": "symbol.map", "from": "Ticker", "to": "Ticker" },
                    { "rule": "symbol.map", "from": "Side", "to": "Side" }
                  ],
                  "parse": [
                    { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
                    { "rule": "decimal.parse", "from": "Price", "to": "Price" },
                    { "rule": "decimal.parse", "from": "NetCash", "to": "NetCash" }
                  ]
                },
                "right": {
                  "identify": [
                    { "rule": "identifier.map", "from": "OrderId", "to": "OrderId", "scheme": "ORDER" },
                    { "rule": "symbol.map", "from": "Ticker", "to": "Ticker" },
                    { "rule": "symbol.map", "from": "Side", "to": "Side" }
                  ],
                  "parse": [
                    { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
                    { "rule": "decimal.parse", "from": "Price", "to": "Price" },
                    { "rule": "decimal.parse", "from": "NetCash", "to": "NetCash" }
                  ]
                }
              },
              "paths": [
                {
                  "id": "orders",
                  "route": { "rule": "route.all" },
                  "passes": [
                    {
                      "id": "fills",
                      "pass": "match.groupSum",
                      "many": "left",
                      "block": ["OrderId", "Ticker", "Side", "Price"],
                      "conserve": ["Quantity", "NetCash"],
                      "maxGroupSize": 8
                    }
                  ]
                }
              ]
            }
            """,
            leftHeader: leftHeader,
            left: left,
            rightHeader: rightHeader,
            right: right,
            assert: summary => Expect.Clean(summary, orders, "p08"));
    }

    private static int[] Split(int total, int parts)
    {
        var amounts = new int[parts];
        var each = total / parts;
        var remainder = total % parts;
        for (var leg = 0; leg < parts; leg++)
            amounts[leg] = each + (leg < remainder ? 1 : 0);
        return amounts;
    }

    private static string Canon(string raw)
    {
        var body = raw.StartsWith("TRD-", StringComparison.Ordinal) ? raw[4..]
            : raw.StartsWith("trd", StringComparison.Ordinal) ? raw[3..]
            : raw;
        var trimmed = body.TrimStart('0');
        return trimmed.Length == 0 ? "0" : trimmed;
    }

    private static int CheckDistinct(IReadOnlyList<string[]> rows, int[] columns)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
            seen.Add(string.Join('\u001f', columns.Select(column => row[column])));
        return seen.Count;
    }

    private static BuiltPair Build(
        string id,
        string title,
        string phenomenon,
        string correspondence,
        bool expressible,
        string? gap,
        string? notes,
        string[] confounds,
        JsonObject match,
        JsonObject[] columns,
        JsonObject[] transforms,
        string? rowKeyLeft,
        string? rowKeyRight,
        string rounding,
        bool expectClean,
        string profile,
        string[] leftHeader,
        IReadOnlyList<string[]> left,
        string[] rightHeader,
        IReadOnlyList<string[]> right,
        Action<RunSummary> assert) =>
        new(
            id,
            Json.O(
                ("pairId", Json.S(id)),
                ("title", Json.S(title)),
                ("phenomenon", Json.S(phenomenon)),
                ("correspondence", Json.S(correspondence)),
                ("plantedBreaks", Json.N(0)),
                ("engineExpressible", Json.B(expressible)),
                ("expectClean", Json.B(expectClean)),
                ("engineGap", gap is null ? null : Json.S(gap)),
                ("engineNotes", notes is null ? null : Json.S(notes)),
                ("confounds", Confounds(confounds)),
                ("rounding", Json.S(rounding)),
                ("rowKey", Json.O(("left", rowKeyLeft is null ? null : Json.S(rowKeyLeft)), ("right", rowKeyRight is null ? null : Json.S(rowKeyRight)))),
                ("match", match),
                ("columns", Json.List(columns)),
                ("transforms", Json.List(transforms)),
                ("profile", JsonNode.Parse(profile)),
                ("rightRowOrder", Json.S("shuffled")),
                ("dataCheck", Json.S("Every record matches its counterpart after the transforms in this key. No planted breaks. Row order is not part of the correspondence."))),
            leftHeader,
            left,
            rightHeader,
            Shuffled(right, id),
            assert);

    private static IReadOnlyList<string[]> Shuffled(IReadOnlyList<string[]> rows, string seedText)
    {
        var copy = rows.ToArray();
        var state = 2166136261u;
        foreach (var character in seedText)
            state = (state ^ character) * 16777619u;
        for (var index = copy.Length - 1; index > 0; index--)
        {
            state = state * 1664525u + 1013904223u;
            var swap = (int)(state % (uint)(index + 1));
            (copy[index], copy[swap]) = (copy[swap], copy[index]);
        }

        return copy;
    }

    private static JsonObject Match(string pass, string[] key, string[]? block = null, string[]? conserve = null, string? many = null) =>
        Json.O(
            ("pass", Json.S(pass)),
            ("key", Json.Strings(key)),
            ("block", Json.Strings(block ?? [])),
            ("conserve", Json.Strings(conserve ?? [])),
            ("many", many is null ? null : Json.S(many)));

    private static JsonObject Column(string field, string type, string role, int scale, string left, string right, string transform) =>
        Json.O(
            ("field", Json.S(field)),
            ("type", Json.S(type)),
            ("role", Json.S(role)),
            ("scale", Json.N(scale)),
            ("left", Json.S(left)),
            ("right", Json.S(right)),
            ("transform", Json.S(transform)));

    private static JsonObject Transform(string field, string side, string op, string detail) =>
        Json.O(("field", Json.S(field)), ("side", Json.S(side)), ("op", Json.S(op)), ("detail", Json.S(detail)));

    private static JsonArray Confounds(string[] items)
    {
        var array = new JsonArray();
        foreach (var item in items)
            array.Add(JsonValue.Create(item));
        return array;
    }
}
