namespace RecDiffy.Capacity;

internal static class CapacityProfile
{
    public const string Id = "capacity.mixed";
    public const string Version = "1.0.0";

    public const string Json = """
        {
          "formatVersion": 1,
          "profileId": "capacity.mixed",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "TradeId" }, "right": { "rowKey": "TradeId" } },
          "fields": [
            { "name": "Counterparty", "type": "Symbol", "role": "Key" },
            { "name": "Instrument", "type": "Symbol", "role": "Key" },
            { "name": "TradeDate", "type": "Date", "role": "Key" },
            { "name": "Side", "type": "Symbol", "role": "Key" },
            { "name": "Currency", "type": "Symbol", "role": "Economic" },
            { "name": "Quantity", "type": "Decimal", "role": "Economic", "scale": 2 },
            { "name": "Price", "type": "Decimal", "role": "Economic", "scale": 6 },
            { "name": "NetCash", "type": "Decimal", "role": "Economic", "scale": 2 }
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
                { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": [ "yyyy-MM-dd" ] },
                { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
                { "rule": "decimal.parse", "from": "Price", "to": "Price" },
                { "rule": "decimal.parse", "from": "NetCash", "to": "NetCash" }
              ]
            },
            "right": {
              "identify": [
                { "rule": "symbol.map", "from": "Counterparty", "to": "Counterparty" },
                { "rule": "symbol.map", "from": "Instrument", "to": "Instrument" },
                { "rule": "symbol.map", "from": "Side", "to": "Side" },
                { "rule": "symbol.map", "from": "Currency", "to": "Currency" }
              ],
              "parse": [
                { "rule": "date.parse", "from": "TradeDate", "to": "TradeDate", "formats": [ "yyyy-MM-dd" ] },
                { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" },
                { "rule": "decimal.parse", "from": "Price", "to": "Price" },
                { "rule": "decimal.parse", "from": "NetCash", "to": "NetCash" }
              ]
            }
          },
          "paths": [
            {
              "id": "all",
              "route": { "rule": "route.all" },
              "passes": [
                { "id": "P2", "pass": "match.naturalKey", "key": [ "Counterparty", "Instrument", "TradeDate", "Side", "Quantity" ] },
                { "id": "P3", "pass": "match.groupSum", "many": "left", "block": [ "Counterparty", "Instrument", "TradeDate", "Side", "Price" ], "conserve": [ "Quantity", "NetCash" ] },
                { "id": "P4", "pass": "match.reducedKey", "key": [ "Counterparty", "Instrument", "TradeDate", "Side" ] }
              ]
            }
          ]
        }
        """;
}
