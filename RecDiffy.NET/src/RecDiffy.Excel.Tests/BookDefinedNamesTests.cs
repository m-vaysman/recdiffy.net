using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using RecDiffy.Excel.Tests.Support;

namespace RecDiffy.Excel.Tests;

public class BookDefinedNamesTests
{
    [Fact]
    public void Reads_sheet_scoped_filter_databases_and_a_workbook_scoped_name_with_the_same_text()
    {
        var path = WriteWorkbookWithScopedDefinedNames();

        var book = Book.Read(path);

        Assert.Equal("Sheet1!$A$1:$B$10", book.Names["Sheet1!_xlnm._FilterDatabase"]);
        Assert.Equal("Sheet2!$A$1:$C$20", book.Names["Sheet2!_xlnm._FilterDatabase"]);
        Assert.Equal("Sheet1!$A$1:$C$20", book.Names["_xlnm._FilterDatabase"]);
    }

    private static string WriteWorkbookWithScopedDefinedNames()
    {
        var path = Path.Combine(Paths.Scratch("defined-names"), "scoped-names.xlsx");
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        var sheet1 = workbookPart.AddNewPart<WorksheetPart>();
        sheet1.Worksheet = new Worksheet(new SheetData());
        var sheet2 = workbookPart.AddNewPart<WorksheetPart>();
        sheet2.Worksheet = new Worksheet(new SheetData());
        workbookPart.Workbook = new Workbook(
            new Sheets(
                new Sheet { Id = workbookPart.GetIdOfPart(sheet1), SheetId = 1U, Name = "Sheet1" },
                new Sheet { Id = workbookPart.GetIdOfPart(sheet2), SheetId = 2U, Name = "Sheet2" }),
            new DefinedNames(
                new DefinedName("Sheet1!$A$1:$B$10") { Name = "_xlnm._FilterDatabase", LocalSheetId = 0U },
                new DefinedName("Sheet2!$A$1:$C$20") { Name = "_xlnm._FilterDatabase", LocalSheetId = 1U },
                new DefinedName("Sheet1!$A$1:$C$20") { Name = "_xlnm._FilterDatabase" }));
        return path;
    }
}
