using System.Globalization;

namespace RecDiffy.Rules;

internal static class DeclaredDateParsing
{
    private const string StandardFormats = "dDfFgGmMoOrRsStTuUyY";

    // A declared format with no year would take the year from the clock, so compile rejects it.
    public static IReadOnlyList<string> RequireYear(IReadOnlyList<string> formats, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(culture);
        ProfileError[] errors = [.. formats.Where(format => WritesYear(format, culture.DateTimeFormat) is false).Select(format =>
            new ProfileError("InvalidParameter", $"date.parse format '{format}' has no year, so the year would come from the clock."))];
        return errors.Length == 0 ? [.. formats] : throw new ProfileCompileException(errors);
    }

    // Text with no format, or with a reference-data format that has no year, is Invalid (DateParse). A typed date still reads.
    public static RowContext Apply(RowContext row, string from, string to, IReadOnlyList<string> formats, string? formatBy, CultureInfo culture, string ruleId)
    {
        var raw = row.Raw.TryGetValue(from, out var value) ? value : null;
        var rawText = InvariantText.Of(raw);
        return Formats(row, formats, formatBy, culture) switch
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

    // A standard format is one letter, read as the culture's patterns for it. A custom pattern writes a year when a 'y'
    // stands outside quotes and escapes.
    public static bool WritesYear(string format, DateTimeFormatInfo info) => format switch
    {
        [var letter] when StandardFormats.Contains(letter, StringComparison.Ordinal) => info.GetAllDateTimePatterns(letter).All(CustomWritesYear),
        [_] => false,
        _ => CustomWritesYear(format),
    };

    private static bool CustomWritesYear(string pattern) =>
        pattern.Aggregate(new PatternScan(null, false, false), (scan, character) => scan switch
        {
            { Year: true } => scan,
            { Escaped: true } => scan with { Escaped = false },
            _ when character == '\\' => scan with { Escaped = true },
            { Quote: { } quote } => character == quote ? scan with { Quote = null } : scan,
            _ => character switch
            {
                '\'' or '"' => scan with { Quote = character },
                'y' => scan with { Year = true },
                _ => scan,
            },
        }).Year;

    private static RowContext Reject(RowContext row, string to, string? rawText, string ruleId) =>
        row.Write(to, CanonicalCell.Null(FieldType.Date), ruleId, rawText, null)
            .Diagnose("DateParse", DiagnosticSeverity.Invalid, ruleId, to, rawText);

    private static IReadOnlyList<string>? Formats(RowContext row, IReadOnlyList<string> formats, string? formatBy, CultureInfo culture) =>
        formatBy switch
        {
            null => formats,
            var field when row.Display(field) is { } party && row.Reference.DateFormatByCode.TryGetValue(party, out var format) =>
                WritesYear(format, culture.DateTimeFormat) ? [format] : [],
            _ => null,
        };

    private static Parsed Read(object? raw, IReadOnlyList<string> formats, CultureInfo culture) => raw switch
    {
        null or "" => Parsed.Blank,
        DateOnly date => Parsed.Of(date),
        DateTime date => Parsed.Of(DateOnly.FromDateTime(date)),
        double serial when FromOADate(serial) is { } date => Parsed.Of(date),
        string text => ParseText(text, formats, culture) is { } date ? Parsed.Of(date) : Parsed.Failed,
        _ => Parsed.Failed,
    };

    private static DateOnly? FromOADate(double serial) =>
        double.IsFinite(serial) && serial is > -657435.0d and < 2958466.0d
            ? DateOnly.FromDateTime(DateTime.FromOADate(serial))
            : null;

    private static DateOnly? ParseText(string text, IReadOnlyList<string> formats, CultureInfo culture) =>
        formats
            .Select(format => DateOnly.TryParseExact(text, format, culture, DateTimeStyles.None, out var exact) ? exact : (DateOnly?)null)
            .FirstOrDefault(date => date is not null);

    private sealed record PatternScan(char? Quote, bool Escaped, bool Year);

    private readonly record struct Parsed(bool Ok, bool Missing, DateOnly Value)
    {
        public static Parsed Blank { get; } = new(true, true, default);

        public static Parsed Failed { get; } = new(false, false, default);

        public static Parsed Of(DateOnly value) => new(true, false, value);
    }
}
