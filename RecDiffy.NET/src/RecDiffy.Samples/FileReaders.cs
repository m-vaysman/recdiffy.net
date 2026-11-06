using RecDiffy.IO;
using RecDiffy.Results;

namespace RecDiffy.Samples;

public static class FileReaders
{
    public static bool Run()
    {
        SampleReport.Heading("8. Read CSV files");
        var folder = Path.Combine(Path.GetTempPath(), "recdiffy-samples", "files");
        Directory.CreateDirectory(folder);
        var bins = Path.Combine(folder, "bins.csv");
        var book = Path.Combine(folder, "book.txt");
        File.WriteAllText(bins, "Id,Sku,Quantity\r\nA,SKU-1,4\r\nB,SKU-1,6\r\n");
        File.WriteAllText(book, "BOOK|SKU-1|10\r\n");

        // Every option that changes what a file means is stated: whether it has a header, and its delimiter.
        var left = FileSources.FromCsv("bins", bins, new CsvOptions { HasHeader = true, Delimiter = CsvDelimiter.Comma });
        var right = FileSources.FromCsv("book", book, new CsvOptions { HasHeader = false, Delimiter = CsvDelimiter.Pipe });
        var peek = FileSources.PeekCsv(book, new CsvOptions { HasHeader = false, Delimiter = CsvDelimiter.Pipe }, sampleSize: 10, seed: 0);
        Console.WriteLine($"book columns: {string.Join(", ", peek.Headers)}; rows: {peek.RowCount}");

        var registry = RuleRegistry.CreateCore();
        var result = Reconciler.Run(Reconciler.Compile(ProfileDocument.Parse(Profile), registry), left, right, ReferenceDataSet.Empty);
        SampleReport.Result(result);
        return result.Entries is [{ Alignment: Alignment.Matched, Composition: Composition.ManyToOne, Outcome: FieldOutcome.Exact }]
            && peek.Headers is ["Column1", "Column2", "Column3"];
    }

    // The book file has no header, so its side maps columns by position: Column1, Column2, Column3.
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "warehouse-files",
          "version": "1.0.0",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Column1" } },
          "fields": [
            { "name": "Sku", "type": "Symbol", "role": "Key" },
            { "name": "Quantity", "type": "Decimal", "scale": 0, "role": "Economic" }
          ],
          "normalization": {
            "left": { "identify": [ { "rule": "symbol.map", "from": "Sku", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Quantity", "to": "Quantity" } ] },
            "right": { "identify": [ { "rule": "symbol.map", "from": "Column2", "to": "Sku" } ], "parse": [ { "rule": "decimal.parse", "from": "Column3", "to": "Quantity" } ] }
          },
          "paths": [ { "id": "stock", "route": { "rule": "route.all" }, "passes": [
            { "id": "P3", "pass": "match.groupSum", "many": "left", "block": ["Sku"], "conserve": ["Quantity"] }
          ] } ]
        }
        """;
}
