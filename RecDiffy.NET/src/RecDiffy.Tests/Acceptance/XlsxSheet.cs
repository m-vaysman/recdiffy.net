using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace RecDiffy.Tests.Acceptance;

internal static class XlsxSheet
{
    private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> Read(string path, int sheetIndex)
    {
        using var zip = ZipFile.OpenRead(path);
        var strings = SharedStrings(zip);
        var sheet = zip.GetEntry($"xl/worksheets/sheet{sheetIndex}.xml") ?? throw new FileNotFoundException(path);
        using var stream = sheet.Open();
        var rows = XDocument.Load(stream).Descendants(Spreadsheet + "row").Select(row => Cells(row, strings)).ToArray();
        var header = rows[0];
        return [.. rows.Skip(1).Select(row => (IReadOnlyDictionary<string, object?>)header.Keys.ToDictionary(
            column => header[column]?.ToString() ?? column,
            column => row.TryGetValue(column, out var value) ? value : null,
            StringComparer.Ordinal))];
    }

    private static IReadOnlyList<string> SharedStrings(ZipArchive zip)
    {
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
            return [];
        using var stream = entry.Open();
        return [.. XDocument.Load(stream).Descendants(Spreadsheet + "si").Select(item => string.Concat(item.Descendants(Spreadsheet + "t").Select(text => text.Value)))];
    }

    private static Dictionary<string, object?> Cells(XElement row, IReadOnlyList<string> strings) =>
        row.Elements(Spreadsheet + "c").ToDictionary(cell => Column(cell.Attribute("r")!.Value), cell => Value(cell, strings), StringComparer.Ordinal);

    private static string Column(string reference) => new(reference.TakeWhile(char.IsLetter).ToArray());

    private static object? Value(XElement cell, IReadOnlyList<string> strings)
    {
        var inline = cell.Element(Spreadsheet + "is");
        var text = inline is not null
            ? string.Concat(inline.Descendants(Spreadsheet + "t").Select(item => item.Value))
            : cell.Element(Spreadsheet + "v")?.Value;
        if (string.IsNullOrEmpty(text))
            return null;
        var kind = cell.Attribute("t")?.Value;
        var raw = kind is "s" ? strings[int.Parse(text, CultureInfo.InvariantCulture)] : text;
        if (raw.Length > 10 && raw[10] is 'T' && raw.EndsWith('Z'))
            return raw[..10];
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) && kind is not "s"
            ? number
            : raw;
    }
}
