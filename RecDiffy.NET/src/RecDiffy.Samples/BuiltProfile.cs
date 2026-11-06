using RecDiffy.Results;

namespace RecDiffy.Samples;

public static class BuiltProfile
{
    public static bool Run()
    {
        SampleReport.Heading("7. Write a profile without JSON");
        var registry = RuleRegistry.CreateCore();
        var built = Warehouse().Build(registry);
        if (!built.Succeeded)
            return false;

        Console.WriteLine(built.Document.ToJson());
        var result = Reconciler.Run(
            Reconciler.Compile(built.Document, registry),
            RecordSources.FromObjects("bins", Bins, Map()),
            RecordSources.FromObjects("book", Book, Map()),
            ReferenceDataSet.Empty);
        SampleReport.Result(result);

        // A typo in a field name is a problem with a code and a location, not an exception.
        var typo = Warehouse().Path("typo", path => path.RouteWhen("Sku", "SKU-2").NaturalKey("P1", "Skus")).Build(registry);
        foreach (var problem in typo.Problems)
            Console.WriteLine($"problem {problem.Code} at {problem.Location}: {problem.Message}");

        // Before reading a file, check that it has every column the profile reads.
        var missing = ProfileCheck.AgainstHeaders(built.Document, ["Id", "Sku", "Qty"], ["Id", "Sku", "Quantity"]);
        foreach (var column in missing)
            Console.WriteLine($"header {column.Message}");

        return result.Entries is [{ Alignment: Alignment.Matched, Composition: Composition.ManyToOne, Outcome: FieldOutcome.Exact }]
            && typo.Problems is [{ Code: ProfileProblemCodes.UndeclaredPassField, Location: "paths[typo].passes[P1].key[0]" }]
            && missing is [{ Side: Side.Left, Column: "Quantity", Suggestion: "Qty" }];
    }

    // The warehouse profile from FirstReconciliation, built in code instead of JSON.
    private static ProfileBuilder Warehouse()
    {
        var stock = SideBuilder.Empty
            .MapSymbol("Sku", "Sku")
            .ParseDecimal("Quantity", "Quantity");
        return ProfileBuilder.Create("warehouse", "1.0.0")
            .RowKeys(left: "Id", right: "Id")
            .Key("Sku", FieldType.Symbol)
            .Economic("Quantity", FieldType.Decimal, scale: 0)
            .Left(stock)
            .Right(stock)
            .Path("stock", path => path.GroupSum("P3", Side.Left, block: ["Sku"], conserve: ["Quantity"]));
    }

    private static SourceMap<BinRow> Map() =>
        new SourceMap<BinRow>()
            .Text("Id", row => row.Id)
            .Text("Sku", row => row.Sku)
            .Text("Quantity", row => row.Quantity);

    private static readonly BinRow[] Bins = [new("A", "SKU-1", "4"), new("B", "SKU-1", "6")];

    private static readonly BinRow[] Book = [new("BOOK", "SKU-1", "10")];

    private sealed record BinRow(string Id, string Sku, string Quantity);
}
