using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Xunit.Abstractions;

namespace RecDiffy.IO.Tests;

// Adversarial pass on main. README, "Declare how each file is read": "Excel numbers arrive as the decimal the workbook
// stores." A whole number at or above 2^53 is stored as its shortest round-trip text, yet arrives as the double's
// binary value.
public class AdversarialWorkbookIntegerTests(ITestOutputHelper output)
{
    private const string Fields = """
        { "name": "Id", "type": "Identifier", "role": "Key" },
        { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Economic" }
        """;

    private const string Rules = """
        "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "ID" } ],
        "parse": [ { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ]
        """;

    [Theory(Skip = "🔴 #169 (#174 E): whole numbers from 2^53 arrive as the double's binary value")]
    [InlineData("18014398509484530")]
    [InlineData("5.764607523034264E+17")]
    [InlineData("1.2345678901234567E+19")]
    public void A_large_whole_number_arrives_as_the_decimal_the_workbook_stores(string stored)
    {
        var path = Workbook(TestFiles.Folder(), stored);
        var expected = decimal.Parse(stored, NumberStyles.Float, CultureInfo.InvariantCulture);

        var row = Assert.Single(Probe.Left(FileSources.FromExcel("left", path, new ExcelOptions { HasHeader = true, Sheet = ExcelSheet.Numbered(1) }), Fields, Rules));
        using (var reader = Sylvan.Data.Excel.ExcelDataReader.Create(path, new Sylvan.Data.Excel.ExcelDataReaderOptions { Schema = Sylvan.Data.Excel.ExcelSchema.Dynamic }))
        {
            reader.Read();
            output.WriteLine($"stored '{stored}': Sylvan GetValue is {reader.GetValue(1).GetType().Name} {Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture)}; engine reads {row["Amount"].Number.ToString(CultureInfo.InvariantCulture)}");
        }

        Assert.Equal(expected, row["Amount"].Number);
    }

    private static string Workbook(string folder, string stored)
    {
        var path = Path.Combine(folder, "integers.xlsx");
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = document.AddWorkbookPart();
        workbook.Workbook = new Workbook();
        var part = workbook.AddNewPart<WorksheetPart>();
        part.Worksheet = new Worksheet(new SheetData(
            new Row(Text("A1", "Id"), Text("B1", "Amount")) { RowIndex = 1 },
            new Row(Text("A2", "A-1"), new Cell(new CellValue(stored)) { CellReference = "B2", DataType = CellValues.Number }) { RowIndex = 2 }));
        workbook.Workbook.AppendChild(new Sheets()).Append(new DocumentFormat.OpenXml.Spreadsheet.Sheet { Id = workbook.GetIdOfPart(part), SheetId = 1, Name = "Data" });
        workbook.Workbook.Save();
        return path;
    }

    private static Cell Text(string reference, string text) =>
        new(new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text(text))) { CellReference = reference, DataType = CellValues.InlineString };
}
