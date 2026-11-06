using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace RecDiffy.Excel.Tests.Support;

/// <summary>Skips a test with a clear message when LibreOffice is not available.</summary>
public sealed class LibreOfficeFactAttribute : FactAttribute
{
    public LibreOfficeFactAttribute()
    {
        if (LibreOffice.Available is false)
            Skip = LibreOffice.Missing;
    }
}

internal static class Workbooks
{
    /// <summary>Open XML schema errors, as text; empty when the package is valid.</summary>
    public static IReadOnlyList<string> SchemaErrors(string path)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        return [.. new OpenXmlValidator().Validate(document).Select(error => $"{error.Part?.Uri} {error.Path?.XPath}: {error.Description}")];
    }

    /// <summary>Every cell whose value is a ✓/✗ check, with its address.</summary>
    public static IEnumerable<(string Sheet, BookCell Cell)> Checks(Book book) =>
        book.Sheets.SelectMany(sheet => sheet.Value.Values
            .Where(cell => cell.Formula is not null && cell.Value is { Length: > 0 } value && (value[0] is '✓' or '✗'))
            .Select(cell => (sheet.Key, cell)));

    public static IEnumerable<(string Sheet, BookCell Cell)> Errors(Book book) =>
        book.Sheets.SelectMany(sheet => sheet.Value.Values.Where(cell => cell.IsError || cell.Value is "#REF!" or "#NAME?" or "#VALUE!" or "#DIV/0!" or "#N/A" or "Err:502" or "Err:504" or "Err:508").Select(cell => (sheet.Key, cell)));

    /// <summary>Formula cells that LibreOffice left without a value (a sign that it did not recalculate).</summary>
    public static IEnumerable<(string Sheet, BookCell Cell)> Uncalculated(Book book) =>
        book.Sheets.SelectMany(sheet => sheet.Value.Values.Where(cell => cell.Formula is not null && cell.Value is null).Select(cell => (sheet.Key, cell)));
}
