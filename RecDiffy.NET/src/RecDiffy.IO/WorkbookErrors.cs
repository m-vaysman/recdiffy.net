using System.Globalization;
using Sylvan.Data.Excel;

namespace RecDiffy.IO;

// RecDiffy reconciles data as promised and does not repair it (spec v3 §1.2). An error cell is a broken promise, so the
// whole file is rejected, with a message that says exactly which cell to fix.
internal static class WorkbookErrors
{
    public static InvalidDataException Rejection(string location, ExcelFormulaException error) =>
        new(string.Create(
            CultureInfo.InvariantCulture,
            $"Cell {Column(error.Column)}{error.Row} of {location} holds {Text(error.ErrorCode)}. RecDiffy does not read error cells: fix or remove the error in the workbook and read it again."),
            error);

    private static string Text(ExcelErrorCode code) => code switch
    {
        ExcelErrorCode.Null => "#NULL!",
        ExcelErrorCode.DivideByZero => "#DIV/0!",
        ExcelErrorCode.Value => "#VALUE!",
        ExcelErrorCode.Reference => "#REF!",
        ExcelErrorCode.Name => "#NAME?",
        ExcelErrorCode.Number => "#NUM!",
        ExcelErrorCode.NotAvailable => "#N/A",
        var other => "an error value (" + other.ToString() + ")",
    };

    // Sylvan's row is 1-based, as Excel numbers rows; its column is 0-based: 0 is A, 25 is Z, 26 is AA.
    private static string Column(int index) =>
        index < 26
            ? ((char)('A' + index)).ToString()
            : Column((index / 26) - 1) + (char)('A' + (index % 26));
}
