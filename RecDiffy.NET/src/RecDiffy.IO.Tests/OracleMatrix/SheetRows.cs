using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace RecDiffy.IO.Tests.OracleMatrix;

// Reads a sheet below its header row with the OpenXml SDK, independent of the readers under test: text as string,
// numbers as the decimal the cell stores, and a number in a date format as a DateOnly. A blank cell is absent.
internal static class SheetRows
{
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> Read(string path, int sheetNumber, int headerRow)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        var workbook = document.WorkbookPart ?? throw new InvalidDataException(path);
        var sheets = workbook.Workbook?.Sheets ?? throw new InvalidDataException(path);
        var sheet = sheets.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>().ElementAt(sheetNumber - 1);
        var part = (WorksheetPart)workbook.GetPartById(sheet.Id?.Value ?? throw new InvalidDataException(path));
        var strings = workbook.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(item => item.InnerText).ToArray() ?? [];
        var dates = DateStyles(workbook.WorkbookStylesPart?.Stylesheet);
        var rows = part.Worksheet?.Descendants<Row>().ToArray() ?? throw new InvalidDataException(path);
        var header = rows.Single(row => row.RowIndex?.Value == headerRow).Elements<Cell>()
            .Select(cell => (Column: Column(cell), Name: Value(cell, strings, dates) as string))
            .Where(pair => pair.Name is { Length: > 0 })
            .ToDictionary(pair => pair.Column, pair => pair.Name!, StringComparer.Ordinal);
        return [.. rows
            .Where(row => row.RowIndex?.Value > headerRow)
            .Select(row => (IReadOnlyDictionary<string, object?>)row.Elements<Cell>()
                .Where(cell => header.ContainsKey(Column(cell)))
                .Select(cell => (Name: header[Column(cell)], Value: Value(cell, strings, dates)))
                .Where(pair => pair.Value is not null)
                .ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal))
            .Where(row => row.Count > 0)];
    }

    public static IReadOnlyList<string> Keys(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string column) =>
        [.. rows.Select(row => Text(row.GetValueOrDefault(column))).OfType<string>()];

    public static string? Text(object? value) => value switch
    {
        null => null,
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private static string Column(Cell cell) => (cell.CellReference?.Value ?? "").TrimEnd("0123456789".ToCharArray());

    private static object? Value(Cell cell, string[] strings, IReadOnlySet<uint> dates) =>
        (cell.DataType?.Value, cell.CellValue?.Text, cell.InlineString?.InnerText) switch
        {
            (var type, { } index, _) when type == CellValues.SharedString => Trimmed(strings[int.Parse(index, CultureInfo.InvariantCulture)]),
            (_, _, { } inline) => Trimmed(inline),
            (var type, { } text, _) when type == CellValues.String || type == CellValues.Boolean => Trimmed(text),
            (_, "", _) => null,
            (var type, { } text, _) when type == CellValues.Date => DateOnly.FromDateTime(DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)),
            (_, { } text, _) when dates.Contains(cell.StyleIndex?.Value ?? 0) => DateOnly.FromDateTime(DateTime.FromOADate(double.Parse(text, CultureInfo.InvariantCulture))),
            (_, { } text, _) => decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
            _ => null,
        };

    private static string? Trimmed(string text) => text.Trim() is { Length: > 0 } trimmed ? trimmed : null;

    // Style indexes whose number format shows a date: the built-in date formats, or a custom code with a day or year.
    private static HashSet<uint> DateStyles(Stylesheet? styles)
    {
        var custom = styles?.NumberingFormats?.Elements<NumberingFormat>()
            .Where(format => format.NumberFormatId?.Value is { } && IsDateCode(format.FormatCode?.Value ?? ""))
            .Select(format => format.NumberFormatId!.Value)
            .ToHashSet() ?? [];
        return [.. (styles?.CellFormats?.Elements<CellFormat>() ?? [])
            .Select((format, index) => (Index: (uint)index, Id: format.NumberFormatId?.Value ?? 0))
            .Where(pair => pair.Id is (>= 14 and <= 22) or (>= 45 and <= 47) || custom.Contains(pair.Id))
            .Select(pair => pair.Index)];
    }

    private static bool IsDateCode(string code) =>
        string.Concat(code.Split('"').Where((_, index) => index % 2 == 0)).ToLowerInvariant() is var bare && (bare.Contains('y') || bare.Contains('d'));
}

// A sheet's rows as a record source, so tests can reorder rows or rename their IDs. Each column binds by what its
// cells hold: decimals as decimals, dates as dates, and anything mixed or textual as invariant text.
internal static class ObjectSources
{
    public static RecordSource From(string name, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) =>
        RecordSources.FromObjects(name, rows, rows.SelectMany(row => row.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Aggregate(new SourceMap<IReadOnlyDictionary<string, object?>>(), (map, column) => Bind(map, column, rows)));

    private static SourceMap<IReadOnlyDictionary<string, object?>> Bind(SourceMap<IReadOnlyDictionary<string, object?>> map, string column, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) =>
        rows.Select(row => row.GetValueOrDefault(column)?.GetType()).OfType<Type>().Distinct().ToArray() switch
        {
            [var type] when type == typeof(decimal) => map.Decimal(column, row => row.GetValueOrDefault(column) as decimal?),
            [var type] when type == typeof(DateOnly) => map.Date(column, row => row.GetValueOrDefault(column) as DateOnly?),
            _ => map.Text(column, row => SheetRows.Text(row.GetValueOrDefault(column))),
        };
}
