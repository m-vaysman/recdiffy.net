using DocumentFormat.OpenXml.Packaging;
using RecDiffy.Excel.Model;
using RecDiffy.Excel.Tests.Support;
using RecDiffy.Excel.Writing;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace RecDiffy.Excel.Tests;

// StreamingSpreadsheetWriter replaces OpenXmlSpreadsheetWriter (#398): cells go straight to the package instead of
// being built into a worksheet object tree first. The tests below prove the output is the same workbook the DOM
// writer produced, and that the unpaired-surrogate handling from #256 still holds.
public class StreamingSpreadsheetWriterTests
{
    private const char High = (char)0xD800;
    private const char Low = (char)0xDC00;
    private const char Replacement = (char)0xFFFD;
    private const string Pair = "\U0001F600";

    [Fact]
    public void Unpaired_surrogates_in_text_and_formulas_are_replaced_and_the_workbook_is_valid()
    {
        var sheet = Sheet.Empty("S")
            .Put(1, 1, $"A{High}B")
            .Put(2, 1, $"A{Low}B")
            .Put(3, 1, $"A{Pair}B")
            .PutFormula(4, 1, $"\"A{High}\"")
            .Rule(new ExpressionRule(new CellRange(1, 1, 4, 1), $"A1=\"{Low}\"", Styles.Red));

        var path = Write(new Workbook([sheet], []));

        var cells = Book.Read(path)["S"];
        Assert.Equal($"A{Replacement}B", cells["A1"].Value);
        Assert.Equal($"A{Replacement}B", cells["A2"].Value);
        Assert.Equal($"A{Pair}B", cells["A3"].Value);
        Assert.Equal($"\"A{Replacement}\"", cells["A4"].Formula);
        Assert.Equal($"A1=\"{Replacement}\"", ConditionalFormula(path));
        Assert.Empty(Workbooks.SchemaErrors(path));
    }

    [Fact]
    public void Texts_that_clean_to_the_same_string_share_one_entry()
    {
        var sheet = Sheet.Empty("S")
            .Put(1, 1, $"A{High}")
            .Put(2, 1, $"A{Low}")
            .Put(3, 1, $"A{Replacement}");

        var path = Write(new Workbook([sheet], []));

        var cells = Book.Read(path)["S"];
        Assert.All(["A1", "A2", "A3"], address => Assert.Equal($"A{Replacement}", cells[address].Value));
        Assert.Equal(1, SharedStringCount(path));
        Assert.Empty(Workbooks.SchemaErrors(path));
    }

    [Fact]
    public void Streamed_workbook_matches_the_dom_writer_cell_for_cell_and_sheet_for_sheet()
    {
        var workbook = Sample();
        var streamed = Write(workbook, "streamed.xlsx");
        var legacy = WriteLegacy(workbook, "legacy.xlsx");

        var streamedBook = Book.Read(streamed);
        var legacyBook = Book.Read(legacy);
        Assert.Equal(legacyBook.SheetNames, streamedBook.SheetNames);
        AssertSameNames(legacyBook.Names, streamedBook.Names);
        foreach (var sheet in legacyBook.SheetNames)
        {
            Assert.Equal(legacyBook[sheet].Count, streamedBook[sheet].Count);
            foreach (var (address, cell) in legacyBook[sheet])
                Assert.Equal(cell, streamedBook[sheet][address]);
        }

        for (var index = 0; index < legacyBook.SheetNames.Count; index++)
        {
            Assert.Equal(Rows(legacy, index), Rows(streamed, index));
            Assert.Equal(Columns(legacy, index), Columns(streamed, index));
            Assert.Equal(SheetParts(legacy, index), SheetParts(streamed, index));
        }

        Assert.Empty(Workbooks.SchemaErrors(streamed));
        Assert.Empty(Workbooks.SchemaErrors(legacy));
    }

    [Fact]
    public void Rows_that_only_carry_a_height_or_outline_are_still_written()
    {
        var sheet = Sheet.Empty("S")
            .Put(1, 1, "first")
            .Height(3, 42)
            .Outline(5, 2)
            .Put(5, 1, "fifth");

        var path = Write(new Workbook([sheet], []));

        Assert.Equal(["1/-/-", "3/42/-", "5/-/2"], Rows(path));
        Assert.Empty(Workbooks.SchemaErrors(path));
    }

    [Fact]
    public void A_sheet_without_cells_is_still_a_valid_worksheet()
    {
        var path = Write(new Workbook([Sheet.Empty("Empty")], []));

        Assert.Empty(Book.Read(path)["Empty"]);
        Assert.Empty(Workbooks.SchemaErrors(path));
    }

    private static Workbook Sample()
    {
        var data = Sheet.Empty("Data")
            .Header(1, ["Id", "Amount", "Date", "Note"])
            .Put(2, 1, "A-1")
            .Put(2, 2, 12.5m)
            .Put(2, 3, new DateOnly(2026, 11, 13))
            .Put(2, 4, "hello")
            .Put(3, 1, "A-2")
            .Put(3, 2, 7m)
            .PutFormula(3, 4, "LEN(D2)")
            .Merge(3, 3, 3, 4)
            .Width(1, 20)
            .Width(2, 14)
            .Height(5, 42)
            .Outline(6, 1)
            .FreezeAt(2, 1)
            .Filter(1, 1, 3, 4)
            .Gridlines(false)
            .SummaryAbove()
            .Rule(new CellIsRule(new CellRange(2, 2, 3, 2), CellOperator.GreaterThan, ["10"], Styles.Amber))
            .Rule(new ExpressionRule(new CellRange(2, 4, 3, 4), "LEN(D2)>3", Styles.Grey))
            .Chart(new BarChart(
                "Amounts",
                new CellAddress(8, 1),
                12,
                6,
                "'Data'!$A$2:$A$3",
                [new ChartSeries("'Data'!$B$1", "'Data'!$B$2:$B$3", "4472C4")],
                "Amount",
                false));
        var second = Sheet.Empty("Second").Put(1, 1, "x").Put(1, 2, 1m);
        return new Workbook([data, second], [new DefinedName("AsOf", "'Data'!$B$1")]);
    }

    private static string Write(Workbook workbook, string name = "book.xlsx")
    {
        var path = Path.Combine(Paths.Scratch("streaming-writer"), name);
        using (var file = File.Create(path))
            new StreamingSpreadsheetWriter().Write(workbook, file);
        return path;
    }

    private static string WriteLegacy(Workbook workbook, string name)
    {
        var path = Path.Combine(Paths.Scratch("streaming-writer"), name);
        using (var file = File.Create(path))
#pragma warning disable CS0618
            new OpenXmlSpreadsheetWriter().Write(workbook, file);
#pragma warning restore CS0618
        return path;
    }

    private static void AssertSameNames(IReadOnlyDictionary<string, string> expected, IReadOnlyDictionary<string, string> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        foreach (var (name, reference) in expected)
            Assert.Equal(reference, actual[name]);
    }

    private static string? ConditionalFormula(string path)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        return document.WorkbookPart!.WorksheetParts.Single().Worksheet!.Descendants<S.ConditionalFormattingRule>().Single().GetFirstChild<S.Formula>()?.Text;
    }

    private static int SharedStringCount(string path)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        return document.WorkbookPart!.SharedStringTablePart!.SharedStringTable!.Elements<S.SharedStringItem>().Count();
    }

    private static WorksheetPart Part(SpreadsheetDocument document, int index)
    {
        var id = document.WorkbookPart!.Workbook!.Sheets!.Elements<S.Sheet>().ElementAt(index).Id!.Value!;
        return (WorksheetPart)document.WorkbookPart.GetPartById(id);
    }

    private static IReadOnlyList<string> Rows(string path, int index = 0)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        return [.. Part(document, index).Worksheet!.Descendants<S.Row>()
            .Select(row => $"{row.RowIndex!.Value.ToInvariant()}/{row.Height?.Value.ToInvariant() ?? "-"}/{row.OutlineLevel?.Value.ToInvariant() ?? "-"}")];
    }

    private static IReadOnlyList<string> Columns(string path, int index)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        return [.. Part(document, index).Worksheet!.Descendants<S.Column>()
            .Select(column => $"{column.Min!.Value.ToInvariant()}-{column.Max!.Value.ToInvariant()}@{column.Width!.Value.ToInvariant()}")];
    }

    private static string SheetParts(string path, int index)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        var worksheet = Part(document, index).Worksheet!;
        var pane = worksheet.Descendants<S.Pane>().SingleOrDefault();
        var filter = worksheet.Descendants<S.AutoFilter>().SingleOrDefault();
        var rules = worksheet.Descendants<S.ConditionalFormattingRule>()
            .Select(rule => $"{rule.Type?.Value}/{rule.Priority?.Value}/{rule.Elements<S.Formula>().FirstOrDefault()?.Text}");
        var merges = worksheet.Descendants<S.MergeCell>().Select(merge => merge.Reference!.Value);
        var drawing = Part(document, index).DrawingsPart is { } drawings ? drawings.ChartParts.Count().ToInvariant() : "0";
        return string.Join(
            "|",
            $"pane={pane?.TopLeftCell?.Value}/{pane?.State?.Value}",
            $"filter={filter?.Reference?.Value}",
            $"merges={string.Join(",", merges)}",
            $"rules={string.Join(",", rules)}",
            $"charts={drawing}");
    }
}
