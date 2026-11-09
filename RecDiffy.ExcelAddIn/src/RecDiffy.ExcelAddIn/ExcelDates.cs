using System.Text.RegularExpressions;

namespace RecDiffy.ExcelAddIn;

public enum CellShape
{
    Value,
    Date,
    Time,
}

public static partial class ExcelDates
{
    private const double SecondsPerDay = 86_400;

    public static CellShape ShapeOfFormat(string numberFormat)
    {
        ArgumentNullException.ThrowIfNull(numberFormat);
        var plain = NotCodes().Replace(numberFormat, string.Empty);
        var codes = (plain.IndexOf(';', StringComparison.Ordinal) is var end and >= 0 ? plain[..end] : plain)
            .ToLowerInvariant()
            .Replace("general", string.Empty, StringComparison.Ordinal);
        return codes switch
        {
            _ when codes.AsSpan().IndexOfAny('y', 'd') >= 0 => CellShape.Date,
            _ when codes.AsSpan().IndexOfAny('h', 's') >= 0 || codes.Contains("am/pm", StringComparison.Ordinal) || codes.Contains("a/p", StringComparison.Ordinal) => CellShape.Time,
            _ when codes.Contains('m', StringComparison.Ordinal) => CellShape.Date,
            _ => CellShape.Value,
        };
    }

    public static CellShape ShapeOf(string formatCode)
    {
        ArgumentNullException.ThrowIfNull(formatCode);
        return formatCode switch
        {
            ['D', >= '1' and <= '5'] => CellShape.Date,
            ['D', >= '6' and <= '9'] => CellShape.Time,
            _ => CellShape.Value,
        };
    }

    public static object? Typed(object? value, CellShape shape, bool uses1904) =>
        (shape, Serial(value)) switch
        {
            (CellShape.Date, { } serial) => DateOf(serial, uses1904) ?? value,
            (CellShape.Time, { } serial) => TimeOf(serial) ?? value,
            _ => value,
        };

    public static DateTime? DateOf(double serial, bool uses1904)
    {
        DateTime? epoch = (uses1904, serial) switch
        {
            (true, >= 0) => new DateTime(1904, 1, 1),
            (false, >= 61) => new DateTime(1899, 12, 30),
            (false, >= 1 and < 60) => new DateTime(1899, 12, 31),
            _ => null,
        };
        var seconds = Math.Round(serial * SecondsPerDay);
        return epoch is { } start && seconds <= (DateTime.MaxValue - start).TotalSeconds ? start.AddSeconds(seconds) : null;
    }

    public static TimeOnly? TimeOf(double serial) =>
        serial >= 0
            ? TimeOnly.FromTimeSpan(TimeSpan.FromSeconds(Math.Round((serial - Math.Floor(serial)) * SecondsPerDay) % SecondsPerDay))
            : null;

    private static double? Serial(object? value) => value switch
    {
        decimal number => (double)number,
        double number => number,
        _ => null,
    };

    [GeneratedRegex("""\"[^"]*"|\\.|_.|\*.|\[(?![hHmMsS]+\])[^\]]*\]""", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NotCodes();
}
