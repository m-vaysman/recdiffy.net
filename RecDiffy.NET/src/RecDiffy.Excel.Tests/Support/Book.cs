using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace RecDiffy.Excel.Tests.Support;

/// <summary>One cell as read back from a written or recalculated workbook.</summary>
internal sealed record BookCell(string Address, string? Formula, string? Value, bool IsError)
{
    public decimal? Number => decimal.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
}

/// <summary>A workbook read back with the Open XML SDK: sheet order, cells with formula text and cached values, defined names.</summary>
internal sealed record Book(IReadOnlyList<string> SheetNames, IReadOnlyDictionary<string, IReadOnlyDictionary<string, BookCell>> Sheets, IReadOnlyDictionary<string, string> Names)
{
    public IReadOnlyDictionary<string, BookCell> this[string sheet] => Sheets[sheet];

    public string? Text(string sheet, string address) => Sheets[sheet].TryGetValue(address, out var cell) ? cell.Value : null;

    public static Book Read(string path)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        var workbook = document.WorkbookPart!;
        string[] strings = workbook.SharedStringTablePart?.SharedStringTable is { } table ? [.. table.Elements<SharedStringItem>().Select(item => item.InnerText)] : [];
        var sheets = workbook.Workbook!.Sheets!.Elements<Sheet>().ToArray();
        return new Book(
            [.. sheets.Select(sheet => sheet.Name!.Value!)],
            sheets.ToDictionary(
                sheet => sheet.Name!.Value!,
                sheet => (IReadOnlyDictionary<string, BookCell>)((WorksheetPart)workbook.GetPartById(sheet.Id!.Value!)).Worksheet!
                    .Descendants<Cell>()
                    .Select(cell => Cell(cell, strings))
                    .ToDictionary(cell => cell.Address, StringComparer.Ordinal),
                StringComparer.Ordinal),
            workbook.Workbook.DefinedNames?.Elements<DefinedName>().ToDictionary(name => name.Name!.Value!, name => name.Text, StringComparer.Ordinal)
                ?? new Dictionary<string, string>(StringComparer.Ordinal));
    }

    private static BookCell Cell(Cell cell, string[] strings)
    {
        var raw = cell.CellValue?.Text;
        var type = cell.DataType?.Value;
        var value = type == CellValues.SharedString && raw is not null ? strings[int.Parse(raw, CultureInfo.InvariantCulture)]
            : type == CellValues.InlineString ? cell.InlineString?.InnerText
            : raw;
        return new BookCell(cell.CellReference!.Value!, cell.CellFormula?.Text, value, type == CellValues.Error);
    }
}
