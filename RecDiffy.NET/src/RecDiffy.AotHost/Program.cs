var profile = RecDiffy.Reconciler.Compile(RecDiffy.ProfileDocument.Parse("""
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
    """), RecDiffy.RuleRegistry.CreateDefault());

var map = new RecDiffy.SourceMap<Bin>().Text("Id", row => row.Id).Text("Sku", row => row.Sku).Decimal("Quantity", row => row.Quantity);
var result = RecDiffy.Reconciler.Run(
    profile,
    RecDiffy.RecordSources.FromObjects("left", new[] { new Bin("b1", "A", 4), new Bin("b2", "A", 6) }, map),
    RecDiffy.RecordSources.FromObjects("right", new[] { new Bin("book", "A", 10) }, map),
    new RecDiffy.ReferenceDataSet("empty", new DateOnly(2026, 11, 5), "empty", new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>()));

return result.Summary.Exact == 1 ? 0 : 1;

internal sealed record Bin(string Id, string Sku, decimal Quantity);
