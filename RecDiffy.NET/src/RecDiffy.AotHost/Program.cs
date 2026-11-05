using RecDiffy;

namespace RecDiffy.AotHost;

public static class Program
{
    public static int Main()
    {
        var profile = Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore());
        var result = Reconciler.Run(
            profile,
            RecordSources.FromObjects("book", new[] { Row("L") }, Map()),
            RecordSources.FromObjects("book", new[] { Row("R") }, Map()),
            ReferenceDataSet.Empty);
        Console.WriteLine(result.Summary.Exact);
        return result.Summary.Exact == 1 ? 0 : 1;
    }

    private static Dictionary<string, string> Row(string id) => new(StringComparer.Ordinal)
    {
        ["Id"] = id,
        ["Sku"] = "SKU-1",
        ["Quantity"] = "1",
    };

    private static SourceMap<Dictionary<string, string>> Map() =>
        new SourceMap<Dictionary<string, string>>()
            .Text("Id", row => row["Id"])
            .Text("Sku", row => row["Sku"])
            .Text("Quantity", row => row["Quantity"]);

    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "aot-sample",
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
            { "id": "P2", "pass": "match.naturalKey", "key": ["Sku", "Quantity"] }
          ] } ]
        }
        """;
}
