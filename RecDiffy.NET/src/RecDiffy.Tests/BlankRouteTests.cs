using RecDiffy.Results;

namespace RecDiffy.Tests;

public class BlankRouteTests
{
    private const string Profile = """
        {
          "formatVersion": 1,
          "profileId": "blank-route",
          "version": "1.0.0",
          "culture": "",
          "sources": { "left": { "rowKey": "Id" }, "right": { "rowKey": "Id" } },
          "fields": [
            { "name": "Ref", "type": "Identifier", "role": "Key" },
            { "name": "Book", "type": "Text", "role": "Text" },
            { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Economic" }
          ],
          "normalization": {
            "left": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" }, { "rule": "text.copy", "from": "Book", "to": "Book" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ]
            },
            "right": {
              "identify": [ { "rule": "identifier.map", "from": "Ref", "to": "Ref", "scheme": "ref" }, { "rule": "text.copy", "from": "Book", "to": "Book" } ],
              "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ]
            }
          },
          "paths": [
            { "id": "blank-book", "route": { "rule": "route.byField", "field": "Book", "equals": "" },
              "passes": [ { "id": "P1", "pass": "match.naturalKey", "key": ["Ref"] } ] },
            { "id": "rates-book", "route": { "rule": "route.byField", "field": "Book", "equals": "RATES" },
              "passes": [ { "id": "P2", "pass": "match.naturalKey", "key": ["Ref"] } ] }
          ]
        }
        """;

    private static readonly SourceMap<(string Id, string Ref, string Book, string Amount)> Map = new SourceMap<(string Id, string Ref, string Book, string Amount)>()
        .Text("Id", row => row.Id)
        .Text("Ref", row => row.Ref)
        .Text("Book", row => row.Book)
        .Text("Amount", row => row.Amount);

    [Fact(Skip = "#337: route.byField with equals \"\" compiles and takes no row. Unskip with the fix.")]
    public void A_route_on_a_blank_value_is_rejected_at_compile_or_takes_the_blank_rows()
    {
        var document = ProfileDocument.Parse(Profile);
        var rejected = Record.Exception(() => Reconciler.Compile(document, RuleRegistry.CreateCore())) is ProfileCompileException;
        (Alignment Alignment, string PathId)[] blankRows = rejected ? [] : BlankRows(document);

        Assert.True(
            rejected || blankRows is [(Alignment.Matched, "blank-book")],
            $"The route compiled, and the blank rows came out as: {string.Join(", ", blankRows)}");
    }

    private static (Alignment Alignment, string PathId)[] BlankRows(ProfileDocument document) =>
    [
        .. Reconciler.Run(
                Reconciler.Compile(document, RuleRegistry.CreateCore()),
                RecordSources.FromObjects("left", [("L1", "T-1", "", "100"), ("L2", "T-2", "RATES", "250")], Map),
                RecordSources.FromObjects("right", [("R1", "T-1", "   ", "100"), ("R2", "T-2", "RATES", "250")], Map),
                ReferenceDataSet.Empty)
            .Entries
            .Where(entry => entry.Left.Concat(entry.Right).Any(row => row.RowKey is "L1" or "R1"))
            .Select(entry => (entry.Alignment, entry.PathId)),
    ];
}
