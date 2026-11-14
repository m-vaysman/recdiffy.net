using RecDiffy.Results;

namespace RecDiffy.Samples;

public static class ResultReading
{
    public static bool Run()
    {
        SampleReport.Heading("2. Read the result");
        return Break() && Beside() && Quarantined();
    }

    private static bool Break()
    {
        Console.WriteLine("break");
        var result = Run(SkuProfile, [new StockRow("A", "SKU-1", "4")], [new StockRow("BOOK", "SKU-1", "10")]);
        return result.Entries is [{ Alignment: Alignment.Matched, Composition: Composition.OneToOne, Outcome: FieldOutcome.Break }];
    }

    private static bool Beside()
    {
        Console.WriteLine("left only");
        var result = Run(
            SkuProfile,
            [new StockRow("A", "SKU-1", "4"), new StockRow("B", "SKU-2", "1")],
            [new StockRow("BOOK", "SKU-1", "4")]);
        return result.Entries.Any(entry => entry is { Alignment: Alignment.Matched, Outcome: FieldOutcome.Exact })
            && result.Rows.Any(row => row is { Disposition: Disposition.LeftOnly, Row.RowKey: "B" });
    }

    private static bool Quarantined()
    {
        Console.WriteLine("quarantined");
        var result = Run(WarehouseProfile, [new StockRow(null, "SKU-1", "4")], [new StockRow("BOOK", "SKU-1", "4")]);
        return result.Rows.Any(row => row.Disposition == Disposition.Quarantined && row.Diagnostics.Any(diagnostic => diagnostic.Code == "MissingKey"));
    }

    private static IReconciliationResult Run(string profile, StockRow[] left, StockRow[] right)
    {
        var compiled = Reconciler.Compile(ProfileDocument.Parse(profile), RuleRegistry.CreateCore());
        var result = Reconciler.Run(
            compiled,
            RecordSources.FromObjects("bins", left, Map()),
            RecordSources.FromObjects("book", right, Map()),
            ReferenceDataSet.Empty);
        SampleReport.Result(result);
        return result;
    }

    private sealed record StockRow(string? Id, string Sku, string Quantity);

    private static SourceMap<StockRow> Map() =>
        new SourceMap<StockRow>()
            .Text("Id", row => row.Id)
            .Text("Sku", row => row.Sku)
            .Text("Quantity", row => row.Quantity);

    private const string SkuProfile = """
        {
          "formatVersion": 1,
          "profileId": "sku",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Sku", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
            "right": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
          },
          "paths": [ { "id": "stock", "route": { "rule": "route.all" }, "passes": [
            { "id": "sku", "pass": "match.naturalKey", "key": ["Sku"] }
          ] } ]
        }
        """;

    private const string WarehouseProfile = """
        {
          "formatVersion": 1,
          "profileId": "warehouse",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Sku", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
            "right": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] }
          },
          "paths": [ { "id": "stock", "route": { "rule": "route.all" }, "passes": [
            { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Sku"], "conserve": ["Quantity"] }
          ] } ]
        }
        """;
}
