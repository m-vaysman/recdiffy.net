using System.Globalization;

namespace RecDiffy.Rules;

internal static class CultureDateParsing
{
    // In a .NET format, "/" and ":" also match the culture's own separators, so de-DE reads "11.06.2025" with "dd/MM/yyyy".
    // With the culture's separators set to "/" and ":", a format accepts exactly the characters it shows. Month and day names stay the culture's.
    public static CultureInfo Strict(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var strict = (CultureInfo)culture.Clone();
        strict.DateTimeFormat.DateSeparator = "/";
        strict.DateTimeFormat.TimeSeparator = ":";
        return CultureInfo.ReadOnly(strict);
    }

    public static RowContext Apply(RowContext row, string from, string to, IReadOnlyList<string> formats, string? formatBy, CultureInfo culture, string ruleId)
    {
        var raw = row.Raw.TryGetValue(from, out var value) ? value : null;
        var rawText = InvariantText.Of(raw);
        return Formats(row, formats, formatBy) switch
        {
            null => Reject(row, to, rawText, ruleId),
            var active => Read(raw, active, culture) switch
            {
                { Missing: true } => row.Write(to, CanonicalCell.Null(FieldType.Date), ruleId, null, null),
                { Ok: true, Value: var date } => row.Write(to, CanonicalCell.FromDate(date), ruleId, rawText, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                _ => Reject(row, to, rawText, ruleId),
            },
        };
    }

    private static RowContext Reject(RowContext row, string to, string? rawText, string ruleId) =>
        row.Write(to, CanonicalCell.Null(FieldType.Date), ruleId, rawText, null)
            .Diagnose("DateParse", DiagnosticSeverity.Invalid, ruleId, to, rawText);

    private static IReadOnlyList<string>? Formats(RowContext row, IReadOnlyList<string> formats, string? formatBy) =>
        formatBy switch
        {
            null => formats,
            var field when row.Display(field) is { } party && row.Reference.DateFormatByCode.TryGetValue(party, out var format) => [format],
            _ => null,
        };

    private static Parsed Read(object? raw, IReadOnlyList<string> formats, CultureInfo culture) => raw switch
    {
        null or "" => Parsed.Blank,
        DateOnly date => Parsed.Of(date),
        DateTime date => Parsed.Of(DateOnly.FromDateTime(date)),
        double serial when TryFromOADate(serial, out var date) => Parsed.Of(date),
        string text => ParseText(text, formats, culture) is { } date ? Parsed.Of(date) : Parsed.Failed,
        _ => Parsed.Failed,
    };

    private static bool TryFromOADate(double serial, out DateOnly date)
    {
        date = default;
        if (double.IsFinite(serial) is false || serial <= -657435.0d || serial >= 2958466.0d)
            return false;
        date = DateOnly.FromDateTime(DateTime.FromOADate(serial));
        return true;
    }

    private static DateOnly? ParseText(string text, IReadOnlyList<string> formats, CultureInfo culture) =>
        formats.Count == 0
            ? DateOnly.TryParse(text, culture, DateTimeStyles.None, out var parsed) ? parsed : null
            : formats
                .Select(format => DateOnly.TryParseExact(text, format, culture, DateTimeStyles.None, out var exact) ? exact : (DateOnly?)null)
                .FirstOrDefault(date => date is not null);

    private readonly record struct Parsed(bool Ok, bool Missing, DateOnly Value)
    {
        public static Parsed Blank { get; } = new(true, true, default);

        public static Parsed Failed { get; } = new(false, false, default);

        public static Parsed Of(DateOnly value) => new(true, false, value);
    }
}
