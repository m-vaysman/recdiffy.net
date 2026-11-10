using System.Globalization;
using ExcelDna.Integration;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn;

public static class ExcelOutputConversions
{
    public const int MaxCellText = 32_767;

    private const string Cut = "…";

    private static readonly DateTime FirstDay = new(1900, 1, 1);
    private static readonly DateTime AfterLeapBug = new(1900, 3, 1);
    private static readonly DateTime EpochBeforeLeapBug = new(1899, 12, 31);
    private static readonly DateTime Epoch = new(1899, 12, 30);

    [ExcelReturnConversion]
    public static object ToExcelValue(SheetValue value) => value switch
    {
        SheetValue.Text text => Fitted(text.Value),
        SheetValue.Number number => Double(number.Value) ?? (object)number.Value.ToString(CultureInfo.InvariantCulture),
        SheetValue.Date date => Serial(date.Value) ?? (object)date.Value.ToString(date.HasTime ? "yyyy-MM-dd HH:mm" : "yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => ExcelEmpty.Value,
    };

    public static double? Double(decimal value)
    {
        var number = double.Parse(value.ToString(CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
        return decimal.TryParse(number.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var back) && back == value
            ? number
            : null;
    }

    public static double? Serial(DateTime value) => value switch
    {
        _ when value < FirstDay => null,
        _ when value < AfterLeapBug => (value - EpochBeforeLeapBug).TotalDays,
        _ => (value - Epoch).TotalDays,
    };

    private static string Fitted(string text) =>
        text.Length <= MaxCellText ? text : string.Concat(text.AsSpan(0, MaxCellText - Cut.Length), Cut);
}
