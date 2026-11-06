using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace RecDiffy.IO.Tests;

internal sealed record Sheet(string Name, IReadOnlyList<object?[]> Rows, int FirstRow = 1, bool Hidden = false);

// Writes the small CSV and xlsx files the tests read. The xlsx writer is OpenXml, independent of the Sylvan reader
// under test: strings are inline, numbers are numbers, and a DateOnly is a date serial with a date number format.
internal static class TestFiles
{
    public static string Folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "recdiffy-io-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && File.Exists(Path.Combine(dir.FullName, "RecDiffy.NET", "RecDiffy.slnx")) is false)
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }

    public static string TestData => Path.Combine(RepoRoot(), "RecDiffy.NET", "test-data");

    public static string FixtureV1 => Path.Combine(TestData, "trade-reconciliation-fixtures", "RecDiffy.NET-fixture-v1.xlsx");

    public static string Csv(string folder, string name, IEnumerable<IReadOnlyList<string?>> rows, char delimiter, Encoding? encoding = null)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, string.Join("\r\n", rows.Select(row => string.Join(delimiter, row.Select(cell => Quote(cell, delimiter))))) + "\r\n", encoding ?? new UTF8Encoding(false));
        return path;
    }

    private static string Quote(string? cell, char delimiter) =>
        cell is null ? ""
        : cell.Contains(delimiter, StringComparison.Ordinal) || cell.Contains('"', StringComparison.Ordinal) || cell.Contains('\n', StringComparison.Ordinal) || cell.Contains('\r', StringComparison.Ordinal)
            ? "\"" + cell.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : cell;

    public static string Xlsx(string folder, string name, params Sheet[] sheets)
    {
        var path = Path.Combine(folder, name);
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = document.AddWorkbookPart();
        workbook.Workbook = new Workbook();
        var styles = workbook.AddNewPart<WorkbookStylesPart>();
        styles.Stylesheet = Stylesheet();
        var list = workbook.Workbook.AppendChild(new Sheets());
        foreach (var (sheet, index) in sheets.Select((sheet, index) => (sheet, index)))
        {
            var part = workbook.AddNewPart<WorksheetPart>();
            part.Worksheet = new Worksheet(new SheetData(sheet.Rows.Select((cells, offset) => Row(cells, (uint)(sheet.FirstRow + offset)))));
            list.Append(new DocumentFormat.OpenXml.Spreadsheet.Sheet
            {
                Id = workbook.GetIdOfPart(part),
                SheetId = (uint)(index + 1),
                Name = sheet.Name,
                State = sheet.Hidden ? SheetStateValues.Hidden : null,
            });
        }

        workbook.Workbook.Save();
        return path;
    }

    private static Row Row(object?[] cells, uint number) =>
        new(cells.Select((value, column) => Cell(value, $"{Letter(column)}{number.ToString(CultureInfo.InvariantCulture)}")).OfType<Cell>()) { RowIndex = number };

    private static Cell? Cell(object? value, string reference) => value switch
    {
        null => null,
        string text => new Cell(new InlineString(new Text(text) { Space = SpaceProcessingModeValues.Preserve })) { CellReference = reference, DataType = CellValues.InlineString },
        DateOnly date => new Cell(new CellValue(date.ToDateTime(TimeOnly.MinValue).ToOADate().ToString(CultureInfo.InvariantCulture))) { CellReference = reference, StyleIndex = 1 },
        decimal number => new Cell(new CellValue(number.ToString(CultureInfo.InvariantCulture))) { CellReference = reference, DataType = CellValues.Number },
        int number => new Cell(new CellValue(number.ToString(CultureInfo.InvariantCulture))) { CellReference = reference, DataType = CellValues.Number },
        double number => new Cell(new CellValue(number.ToString("R", CultureInfo.InvariantCulture))) { CellReference = reference, DataType = CellValues.Number },
        _ => throw new ArgumentException($"Unsupported cell value {value.GetType().Name}.", nameof(value)),
    };

    private static string Letter(int column) => column < 26 ? ((char)('A' + column)).ToString() : Letter((column / 26) - 1) + (char)('A' + (column % 26));

    // Style 1 is built-in number format 14, a short date, so Excel and readers treat the cell as a date.
    private static Stylesheet Stylesheet() => new(
        new Fonts(new Font()),
        new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })),
        new Borders(new Border()),
        new CellStyleFormats(new CellFormat()),
        new CellFormats(new CellFormat(), new CellFormat { NumberFormatId = 14, ApplyNumberFormat = true }));
}
