using System.Data;
using System.Globalization;
using RecDiffy.Results;

namespace RecDiffy.Tests;

// #337: `route.byField` with `"equals": ""` compiled and took no row, while a condition (WhenMatcher) and a
// side-by-side filter (CanonicalRow.Text) both read a blank cell as the empty text. A blank cell is data (R1), so the
// route takes the blank rows and the path is no longer dead, whichever source the rows came from.
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

    private static readonly SourceMap<Blank> Map = new SourceMap<Blank>()
        .Text("Id", row => row.Id)
        .Text("Ref", row => row.Ref)
        .Text("Book", row => row.Book)
        .Text("Amount", row => row.Amount);

    [Fact]
    public void A_route_on_a_blank_value_is_rejected_at_compile_or_takes_the_blank_rows()
    {
        var document = ProfileDocument.Parse(Profile);
        var rejected = Record.Exception(() => Reconciler.Compile(document, RuleRegistry.CreateCore())) is ProfileCompileException;
        (Alignment Alignment, string PathId)[] blankRows = rejected ? [] : BlankRows(document);

        Assert.True(
            rejected || blankRows is [(Alignment.Matched, "blank-book")],
            $"The route compiled, and the blank rows came out as: {string.Join(", ", blankRows)}");
    }

    // A blank cell is null from both an object map and a data reader, and both read it as the empty text here.
    [Theory]
    [InlineData(Source.DataReader)]
    [InlineData(Source.Objects)]
    public void Blank_equals_takes_the_blank_rows_and_the_named_rows_go_to_the_other_path(Source source)
    {
        var result = Run(source, Rows("", "RATES"));

        Assert.Equal(
            [(Alignment.Matched, "blank-book"), (Alignment.Matched, "rates-book")],
            [.. result.Entries.Select(entry => (entry.Alignment, entry.PathId)).OrderBy(entry => entry.PathId, StringComparer.Ordinal)]);
    }

    // Whitespace only is blank at intake (R1), so it reads as the empty text too.
    [Theory]
    [InlineData(Source.DataReader)]
    [InlineData(Source.Objects)]
    public void A_whitespace_only_cell_is_blank_for_the_route_as_it_is_for_a_condition(Source source)
    {
        var entry = Assert.Single(Run(source, Rows("   ", "RATES")).Entries, entry => entry.PathId is "blank-book");

        Assert.Equal(Alignment.Matched, entry.Alignment);
    }

    // A route on a value the rows do not have still takes nothing, so the fix does not route every row to the blank path.
    [Fact]
    public void A_route_on_an_absent_value_takes_no_blank_row() => Assert.All(
        Run(Source.Objects, [new Blank("L1", "T-1", "FX", "100")], [new Blank("R1", "T-1", "FX", "100")]).Entries,
        entry => Assert.Equal(("", "Unrouted"), (entry.PathId, Assert.Single(entry.Explanation.Trail).Detail)));

    private static (Alignment Alignment, string PathId)[] BlankRows(ProfileDocument document) =>
    [
        .. RunOver(
                document,
                RecordSources.FromObjects("left", [new Blank("L1", "T-1", "", "100"), new Blank("L2", "T-2", "RATES", "250")], Map),
                RecordSources.FromObjects("right", [new Blank("R1", "T-1", "   ", "100"), new Blank("R2", "T-2", "RATES", "250")], Map))
            .Entries
            .Where(entry => entry.Left.Concat(entry.Right).Any(row => row.RowKey is "L1" or "R1"))
            .Select(entry => (entry.Alignment, entry.PathId)),
    ];

    private static ReconciliationResult Run(Source source, Blank[] left, Blank[] right) => source switch
    {
        Source.Objects => RunOver(ProfileDocument.Parse(Profile), RecordSources.FromObjects("left", left, Map), RecordSources.FromObjects("right", right, Map)),
        Source.DataReader => RunFromTables(left, right),
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown source."),
    };

    private static ReconciliationResult Run(Source source, Blank[] rows) => Run(source, rows, rows);

    private static ReconciliationResult RunOver(ProfileDocument document, RecordSource left, RecordSource right) =>
        Reconciler.Run(Reconciler.Compile(document, RuleRegistry.CreateCore()), left, right, ReferenceDataSet.Empty);

    // A data reader turns a blank cell into null, which is the path a CSV or Excel file takes.
    private static ReconciliationResult RunFromTables(Blank[] left, Blank[] right)
    {
        using var leftTable = Table(left);
        using var rightTable = Table(right);
        return RunOver(
            ProfileDocument.Parse(Profile),
            RecordSources.FromDataReader("left", leftTable.CreateDataReader, new TabularOptions()),
            RecordSources.FromDataReader("right", rightTable.CreateDataReader, new TabularOptions()));
    }

    private static Blank[] Rows(string blankBook, string otherBook) =>
        [new Blank("L1", "T-1", blankBook, "100"), new Blank("L2", "T-2", otherBook, "250")];

    private static DataTable Table(Blank[] rows)
    {
        var table = new DataTable { Locale = CultureInfo.InvariantCulture };
#pragma warning disable IDISP004 // The table owns the columns and rows it adds.
        foreach (var column in (string[])["Id", "Ref", "Book", "Amount"])
            table.Columns.Add(column, typeof(object));
        foreach (var row in rows)
            table.Rows.Add(row.Id, row.Ref, Cell(row.Book), row.Amount);
#pragma warning restore IDISP004
        return table;
    }

    private static object Cell(string value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    public enum Source
    {
        Objects,
        DataReader,
    }

    public sealed record Blank(string Id, string Ref, string Book, string Amount);
}
