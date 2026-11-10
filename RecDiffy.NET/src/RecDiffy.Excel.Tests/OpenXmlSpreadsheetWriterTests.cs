using DocumentFormat.OpenXml.Packaging;
using RecDiffy.Excel.Model;
using RecDiffy.Excel.Tests.Support;
using RecDiffy.Excel.Writing;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace RecDiffy.Excel.Tests;

// OpenXmlSpreadsheetWriter replaces OpenXmlWorkbookWriter (#256). Text, cell formulas and conditional-format formulas all go through
// CellText.Clean, so an unpaired surrogate in any of them becomes U+FFFD and the workbook is still valid.
public class OpenXmlSpreadsheetWriterTests
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

    private static string Write(Workbook workbook)
    {
        var path = Path.Combine(Paths.Scratch("spreadsheet-writer"), "book.xlsx");
        using (var file = File.Create(path))
            new OpenXmlSpreadsheetWriter().Write(workbook, file);
        return path;
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
}
