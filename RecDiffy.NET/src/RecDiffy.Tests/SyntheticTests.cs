using RecDiffy.RulePacks.Trading;

namespace RecDiffy.Tests;

public class SyntheticTests
{
    [Fact]
    public void Warehouse_bins_sum_to_the_book()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse("""
            {
              "formatVersion": 1,
              "profileId": "warehouse",
              "version": "1.0.0",
              "rounding": { "midpoint": "ToEven" },
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "Sku", "type": "Identifier", "role": "Key" },
                { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
              ],
              "normalization": {
                "left": { "identify": [ { "rule": "identifier.map", "from": "Sku", "to": "Sku", "scheme": "sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
                "right": { "identify": [ { "rule": "identifier.map", "from": "Sku", "to": "Sku", "scheme": "sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
              },
              "paths": [ { "id": "stock", "route": { "rule": "route.all" }, "passes": [
                { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Sku"], "conserve": ["Quantity"] }
              ] } ]
            }
            """), RuleRegistry.CreateDefault());
        var left = RecordSources.FromObjects("left", new[] { new Bin("b1", "A", 4), new Bin("b2", "A", 6) }, Map());
        var right = RecordSources.FromObjects("right", new[] { new Bin("book", "A", 10) }, Map());
        var result = Reconciler.Run(profile, left, right, Empty(), new RunOptions(1));
        var entry = Assert.Single(result.Entries);
        Assert.Equal(Alignment.Matched, entry.Alignment);
        Assert.Equal(Composition.ManyToOne, entry.Composition);
        Assert.Equal(FieldOutcome.Exact, entry.Outcome);
    }

    [Fact]
    public void Buy_with_negative_quantity_is_quarantined()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse("""
            {
              "formatVersion": 1,
              "profileId": "sign",
              "version": "1.0.0",
              "rounding": { "midpoint": "ToEven" },
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [
                { "name": "AssetClass", "type": "Symbol", "role": "Key" },
                { "name": "Side", "type": "Symbol", "role": "Key" },
                { "name": "Quantity", "type": "Decimal", "scale": 2, "role": "Economic" }
              ],
              "normalization": {
                "left": { "identify": [ { "rule": "symbol.map", "from": "Asset", "to": "AssetClass", "passthrough": "true" } ], "derive": [ { "rule": "side.resolve", "code": "Code", "quantity": "Qty" }, { "rule": "quantity.absolute", "from": "Qty" } ] },
                "right": { "identify": [ { "rule": "symbol.map", "from": "Asset", "to": "AssetClass", "passthrough": "true" } ] }
              },
              "paths": [ { "id": "trade", "route": { "rule": "route.all" }, "passes": [ { "id": "P4", "pass": "match.reducedKey", "key": ["AssetClass", "Side"] } ] } ]
            }
            """), RuleRegistry.CreateDefault().AddTradingRules());
        var left = RecordSources.FromObjects("left", new[] { new SignRow("L", "Equity", "B", -500000m) }, SignMap());
        var right = RecordSources.FromObjects("right", new[] { new SignRow("R", "Equity", "B", 1m) }, SignMap());
        var result = Reconciler.Run(profile, left, right, Empty(), new RunOptions(1));
        var row = Assert.Single(result.Rows, candidate => candidate.Row.RowKey == "L");
        Assert.Equal(Disposition.Quarantined, row.Disposition);
        Assert.Contains(row.Diagnostics, diagnostic => diagnostic.Code == "SideSignConflict");
    }

    [Fact]
    public void Compile_rejects_a_field_read_before_it_is_written()
    {
        var error = Assert.Throws<ProfileException>(() => Reconciler.Compile(ProfileDocument.Parse("""
            {
              "formatVersion": 1,
              "profileId": "bad",
              "version": "1.0.0",
              "rounding": { "midpoint": "ToEven" },
              "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
              "fields": [ { "name": "TradeDate", "type": "Date", "role": "Key" }, { "name": "Counterparty", "type": "Identifier", "role": "Key" } ],
              "normalization": { "left": { "parse": [ { "rule": "date.parse", "from": "When", "to": "TradeDate", "formatBy": "Counterparty" } ] }, "right": { } },
              "paths": [ { "id": "trade", "route": { "rule": "route.all" }, "passes": [] } ]
            }
            """), RuleRegistry.CreateDefault()));
        Assert.Equal("FieldReadBeforeWrite", error.Code);
    }

    private static SourceMap<Bin> Map() => new SourceMap<Bin>().Text("Id", row => row.Id).Text("Sku", row => row.Sku).Decimal("Quantity", row => row.Quantity);
    private static SourceMap<SignRow> SignMap() => new SourceMap<SignRow>().Text("Id", row => row.Id).Text("Asset", row => row.Asset).Text("Code", row => row.Code).Decimal("Qty", row => row.Quantity);
    private static ReferenceDataSet Empty() => new("empty", new DateOnly(2026, 11, 5), "empty", new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>());

    private sealed record Bin(string Id, string Sku, decimal Quantity);
    private sealed record SignRow(string Id, string Asset, string Code, decimal Quantity);
}
