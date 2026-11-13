using System.Globalization;
using RecDiffy.Results;

namespace RecDiffy.Samples;

public static class EngineVersionReport
{
    public static bool Run()
    {
        SampleReport.Heading("10. The engine version");
        var profile = Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore());
        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("bins", Bins, Map()),
            RecordSources.FromObjects("book", Book, Map()),
            ReferenceDataSet.Empty);
        var version = result.Run.EngineVersion;

        // The package version, without build metadata: a prerelease label survives here, unlike in the assembly version.
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"engine version: {version}"));
        return version.Length > 0 && !version.Contains('+', StringComparison.Ordinal);
    }

    private static SourceMap<BinRow> Map() =>
        new SourceMap<BinRow>()
            .Text("Id", row => row.Id)
            .Text("Sku", row => row.Sku)
            .Text("Quantity", row => row.Quantity);

    private static readonly BinRow[] Bins = [new("A", "SKU-1", "4"), new("B", "SKU-1", "6")];

    private static readonly BinRow[] Book = [new("BOOK", "SKU-1", "10")];

    private sealed record BinRow(string Id, string Sku, string Quantity);

    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "engine-version",
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
