using RecDiffy.Results;

namespace RecDiffy.Samples;

public static class AsyncRun
{
    public static async Task<bool> Run()
    {
        SampleReport.Heading("4. Async sources");
        var profile = Reconciler.Compile(ProfileDocument.Parse(Profile), RuleRegistry.CreateCore());
        var synchronous = Reconciler.Run(
            profile,
            RecordSources.FromObjects("bins", Bins, Map()),
            RecordSources.FromObjects("book", Book, Map()),
            ReferenceDataSet.Empty);
        var asynchronous = await Reconciler.RunAsync(
            profile,
            RecordSources.FromObjects("bins", Stream(Bins), Map()),
            RecordSources.FromObjects("book", Stream(Book), Map()),
            ReferenceDataSet.Empty);
        SampleReport.Result(asynchronous);
        var same = synchronous.Summary == asynchronous.Summary;
        Console.WriteLine(same ? "async summary matches the synchronous run" : "async summary differs from the synchronous run");
        return same
            && asynchronous.Entries is [{ Alignment: Alignment.Matched, Composition: Composition.ManyToOne, Outcome: FieldOutcome.Exact }];
    }

    private static async IAsyncEnumerable<BinRow> Stream(IReadOnlyList<BinRow> rows)
    {
        foreach (var row in rows)
        {
            await Task.Yield();
            yield return row;
        }
    }

    private static SourceMap<BinRow> Map() =>
        new SourceMap<BinRow>()
            .Text("Id", row => row.Id)
            .Text("Sku", row => row.Sku)
            .Text("Quantity", row => row.Quantity);

    private sealed record BinRow(string Id, string Sku, string Quantity);

    private static readonly BinRow[] Bins = [new("A", "SKU-1", "4"), new("B", "SKU-1", "6")];

    private static readonly BinRow[] Book = [new("BOOK", "SKU-1", "10")];

    private const string Profile = """
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
