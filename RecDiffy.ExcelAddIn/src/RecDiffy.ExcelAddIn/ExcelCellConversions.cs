using System.Globalization;
using ExcelDna.Integration;

namespace RecDiffy.ExcelAddIn;

public readonly record struct SourceCell(object? Value, string? Error = null)
{
    public static SourceCell Empty { get; } = new(null);

    public static SourceCell Failed(string error) => new(null, error);
}

public static class ExcelCellConversions
{
    [ExcelParameterConversion]
    public static SourceCell ToSourceCell(object value) => value switch
    {
        null or ExcelEmpty or ExcelMissing => SourceCell.Empty,
        string text => new SourceCell(text),
        double number => new SourceCell(ExactNumber(number)),
        bool flag => new SourceCell(flag),
        ExcelError error => SourceCell.Failed(ErrorText(error)),
        _ => SourceCell.Failed($"an unexpected value ({value.GetType().Name})"),
    };

    public static object ExactNumber(double number) =>
        double.IsFinite(number)
        && decimal.TryParse(number.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var exact)
        && double.Parse(exact.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) == number
            ? exact
            : number;

    private static string ErrorText(ExcelError error) => error switch
    {
        ExcelError.ExcelErrorNull => "#NULL!",
        ExcelError.ExcelErrorDiv0 => "#DIV/0!",
        ExcelError.ExcelErrorValue => "#VALUE!",
        ExcelError.ExcelErrorRef => "#REF!",
        ExcelError.ExcelErrorName => "#NAME?",
        ExcelError.ExcelErrorNum => "#NUM!",
        ExcelError.ExcelErrorNA => "#N/A",
        ExcelError.ExcelErrorGettingData => "#GETTING_DATA",
        _ => "an error value",
    };
}
