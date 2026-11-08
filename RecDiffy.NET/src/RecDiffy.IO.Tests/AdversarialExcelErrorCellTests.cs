using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace RecDiffy.IO.Tests;

// Spec v3 §1.2 and §3.4: a workbook error cell (#N/A, #DIV/0!) breaks the input contract, so the file is rejected,
// anywhere in the sheet, including a column no rule reads. The message names the sheet, cell and error; it is never
// Sylvan's bare ExcelFormulaException. The source must be fixed.
public class AdversarialExcelErrorCellTests
{
    private const string Fields = """
        { "name": "Id", "type": "Identifier", "role": "Key" },
        { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Economic" }
        """;

    private const string Rules = """
        "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "ID" } ],
        "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ]
        """;

    [Theory(Skip = "🔴 #169 (#174 E): an error cell must reject the file and name the cell")]
    [InlineData("B")]
    [InlineData("C")]
    public void A_workbook_with_an_error_cell_is_rejected_and_the_message_names_the_cell(string column)
    {
        var book = Workbook(TestFiles.Folder(), errorInColumn: column);

        var failure = Assert.Throws<InvalidDataException>(() => Probe.Run(Source(book), Probe.Empty(), Fields, Rules));

        Assert.Contains("'Data'", failure.Message, StringComparison.Ordinal);
        Assert.Contains(column + "3", failure.Message, StringComparison.Ordinal);
        Assert.Contains("#N/A", failure.Message, StringComparison.Ordinal);
    }

    private static RecordSource Source(string book) =>
        FileSources.FromExcel("left", book, new ExcelOptions { HasHeader = true, Sheet = ExcelSheet.Numbered(1) });

    // Id, Amount, Note. Row 2 is clean; row 3 holds #N/A in the given column.
    private static string Workbook(string folder, string errorInColumn)
    {
        var path = Path.Combine(folder, "errors.xlsx");
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = document.AddWorkbookPart();
        workbook.Workbook = new Workbook();
        var part = workbook.AddNewPart<WorksheetPart>();
        part.Worksheet = new Worksheet(new SheetData(
            new Row(Text("A1", "Id"), Text("B1", "Amount"), Text("C1", "Note")) { RowIndex = 1 },
            new Row(Text("A2", "A-1"), Number("B2", 10.5m), Text("C2", "ok")) { RowIndex = 2 },
            new Row(Text("A3", "A-2"), errorInColumn == "B" ? Error("B3") : Number("B3", 20m), errorInColumn == "C" ? Error("C3") : Text("C3", "ok")) { RowIndex = 3 }));
        workbook.Workbook.AppendChild(new Sheets()).Append(new DocumentFormat.OpenXml.Spreadsheet.Sheet { Id = workbook.GetIdOfPart(part), SheetId = 1, Name = "Data" });
        workbook.Workbook.Save();
        return path;
    }

    private static Cell Text(string reference, string text) =>
        new(new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text(text))) { CellReference = reference, DataType = CellValues.InlineString };

    private static Cell Number(string reference, decimal number) =>
        new(new CellValue(number.ToString(CultureInfo.InvariantCulture))) { CellReference = reference, DataType = CellValues.Number };

    private static Cell Error(string reference) =>
        new(new CellValue("#N/A")) { CellReference = reference, DataType = CellValues.Error };
}
